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
        'warning': 'Legacy unassociated rolls are not proven fake; these counts do not validate preview accuracy or state/RNG preservation.'
    }


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('input', type=Path)
    args = parser.parse_args()
    print(json.dumps(summarize(read_rows(args.input)), ensure_ascii=False, indent=2))
