"""Summarize local diagnostic logs without exposing unit/weapon names or raw rows."""
import argparse
from collections import Counter
import json
from pathlib import Path
import zipfile


def read_rows(path):
    if zipfile.is_zipfile(path):
        with zipfile.ZipFile(path) as archive:
            candidates = [i for i in archive.infolist() if i.filename.lower().endswith('/combatpreview.diagnostics.jsonl')
                          or i.filename.lower() == 'combatpreview.diagnostics.jsonl']
            if len(candidates) != 1:
                raise ValueError('Expected exactly one current diagnostics JSONL file in the ZIP')
            if candidates[0].file_size > 8388608:
                raise ValueError('Diagnostic input exceeds the 8 MiB analysis limit')
            content = archive.read(candidates[0]).decode('utf-8-sig')
    else:
        if path.stat().st_size > 8388608:
            raise ValueError('Diagnostic input exceeds the 8 MiB analysis limit')
        content = path.read_text(encoding='utf-8-sig')
    rows = [json.loads(line) for line in content.splitlines() if line.strip()]
    if not rows:
        raise ValueError('No diagnostic records; an empty log is not a successful observation test')
    if any(not isinstance(r, dict) or r.get('schema') not in (1, 2) for r in rows):
        raise ValueError('Unsupported diagnostic schema')
    return rows


def summarize(rows):
    observations = [r for r in rows if r.get('kind') in ('observation', 'unassociated-observation')]
    damage = [r for r in rows if r.get('kind') == 'damage-observation']
    def key(r, field='eventId'):
        # Legacy records cannot prove identity across process restarts. Schema 2
        # explicitly namespaces event IDs; do not link equal IDs across sessions.
        return r.get('sessionId', 'legacy-no-session'), r.get(field)
    rolls = {key(r): r for r in observations}
    associated = [r for r in observations if r.get('hasWeaponAttack', r.get('fullAttack') is not None and r.get('attackIndex', -1) >= 0)]
    damage_keys = {key(r, 'rollEventId') for r in damage}
    ids = [key(r) for r in rows if r.get('eventId') is not None]
    versions = sorted({r.get('runtime', {}).get('gameVersion') for r in rows if r.get('runtime', {}).get('gameVersion')})
    beta_previews = {(r.get('sessionId'), r.get('previewId')) for r in rows if r.get('kind') == 'beta-preview'}
    beta_comparisons = [r for r in rows if r.get('kind') == 'beta-comparison']
    return {
        'records': len(rows), 'kinds': dict(Counter(r.get('kind') for r in rows)),
        'observedRolls': len(observations), 'associatedWeaponRolls': len(associated),
        'unassociatedRolls': len(observations) - len(associated),
        'fullAttackRolls': sum(r.get('fullAttack') is True for r in associated),
        'confirmedCriticalRolls': sum(r.get('criticalConfirmed') is True for r in associated),
        'associatedHits': sum(r.get('hit') is True for r in associated), 'damageRecords': len(damage),
        'damageWithoutRoll': sum(key(r, 'rollEventId') not in rolls for r in damage),
        'associatedHitsWithoutDamage': sum(r.get('hit') is True and key(r) not in damage_keys for r in associated),
        'duplicateEventIdsWithinSession': len(ids) - len(set(ids)),
        'truncatedLegacyDefenseEvidence': sum('[truncated]' in r.get('defenseEvidence', '') for r in damage),
        'legacyRecordsWithoutSessionId': sum(not r.get('sessionId') for r in rows),
        'gameVersions': versions,
        'comparisons': dict(Counter(r.get('status') for r in observations)),
        'betaComparisons': dict(Counter(r.get('status') for r in beta_comparisons)),
        'betaModelDifferences': sum(r.get('status') == 'ComparedPartialModel' and bool(r.get('differentFields')) for r in beta_comparisons),
        'betaComparisonsWithoutPreview': sum((r.get('sessionId'), r.get('previewId')) not in beta_previews for r in beta_comparisons),
        'betaComparisonsWithoutRoll': sum(key(r, 'rollEventId') not in rolls for r in beta_comparisons),
        'warning': 'Legacy unassociated rolls are not proven fake; these counts do not validate preview accuracy or state/RNG preservation.'
    }


def summarize_file(rows):
    # A game restart appends to the existing file. Report each identified session
    # separately so legacy failures are not attributed to the latest mod build.
    groups = {}
    for row in rows:
        groups.setdefault(row.get('sessionId'), []).append(row)
    sessions = []
    runtime_keys = ('modVersion', 'gameVersion', 'gameAssemblyMvid', 'ummVersion', 'harmonyVersion', 'tttAssemblyDetected')
    for session_id, group in groups.items():
        variants = {}
        for row in group:
            runtime = {k: row.get('runtime', {}).get(k) for k in runtime_keys if k in row.get('runtime', {})}
            if runtime:
                variants.setdefault(json.dumps(runtime, sort_keys=True), runtime)
        sequence = [r.get('recordSequence') for r in group]
        contiguous = None if not session_id else (
            all(type(n) is int and n > 0 for n in sequence)
            and all(b == a + 1 for a, b in zip(sequence, sequence[1:])))
        sessions.append({
            'sessionId': session_id, 'summary': summarize(group),
            'runtimeMetadata': list(variants.values()),
            'runtimeMetadataConsistent': bool(variants) and len(variants) == 1,
            'recordSequenceContiguous': contiguous,
        })
    latest_id = next((r.get('sessionId') for r in reversed(rows) if r.get('sessionId')), None)
    return {**summarize(rows), 'sessions': sessions,
            'latestIdentifiedSession': next((s for s in sessions if s['sessionId'] == latest_id), None) if latest_id else None}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('input', type=Path)
    args = parser.parse_args()
    print(json.dumps(summarize_file(read_rows(args.input)), ensure_ascii=False, indent=2))
