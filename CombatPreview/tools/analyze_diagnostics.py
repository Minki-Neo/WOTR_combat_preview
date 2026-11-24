"""Summarize local diagnostic logs without exposing unit/weapon names or raw rows."""
import math
import argparse
from collections import Counter
import json
from pathlib import Path
import zipfile


def spell_target_scaling(previews):
    terms = [term for row in previews
             for term in (((row.get('spellMetadata') or {}).get('spellDamage') or {}).get('terms') or [])
             if term.get('hpDamage') is True]
    scaled = [term for term in terms if isinstance(term.get('targetDurability'), (int, float))
              and term['targetDurability'] < 1]
    return {
        'areaDamageTerms': sum(term.get('isAreaDamage') is True for term in terms),
        'areaHalfTerms': len(scaled),
        'handlerOccurrences': dict(Counter(source.get('component', 'unknown') for term in scaled
                                          for source in term.get('areaHalfSources') or [])),
        'energyBonusTerms': sum(type(term.get('energyBonusPercent')) is int and term['energyBonusPercent'] != 0 for term in terms),
        'energyVulnerableTerms': sum(isinstance(term.get('energyVulnerability'), (int, float))
                                    and term['energyVulnerability'] > 1 for term in terms),
        'energyHandlerOccurrences': dict(Counter(source.get('component', 'unknown') for term in terms
                                                 for source in term.get('energyScalingSources') or [])),
        'unavailableReasons': dict(Counter(term['targetScalingUnavailableReason'] for term in terms
                                           if term.get('targetScalingUnavailableReason'))),
        'warning': 'Preview handler counts are repeated samples, not unique effects or verified damage matches.'
    }


def parameter_comparison_review(previews):
    valid = matching = different = invalid = unavailable = missing = 0
    for row in previews:
        comparison = (row.get('spellMetadata') or {}).get('readonlyParameterComparison')
        if not isinstance(comparison, dict):
            missing += 1
        elif comparison.get('available') is not True:
            unavailable += 1
        else:
            values = [comparison.get(key) for key in ('dcDelta', 'casterLevelDelta', 'rankBonusDelta')]
            if not all(type(value) in (int, float) and -float('inf') < value < float('inf') for value in values):
                invalid += 1
            else:
                valid += 1
                if all(value == 0 for value in values):
                    matching += 1
                else:
                    different += 1
    return {'validComparisons': valid, 'matchingNumericSamples': matching, 'differentNumericSamples': different,
            'invalidDeltaSamples': invalid, 'unavailableComparisons': unavailable, 'noComparisonSamples': missing,
            'warning': 'Repeated tracked-state parameter comparisons only. Matching DC/CL/rank does not validate complete spell outcomes, expected damage or all game handlers.'}


def conditional_save_review(previews):
    models = [check.get('d20Model') or {} for row in previews for check in row.get('checkDetails') or []
              if check.get('kind') == 'save']
    typed = [model for model in models if any(type(modifier.get('saveBonusDescriptor')) is int
             for modifier in model.get('modifiers') or [])]
    metadata = [row.get('spellMetadata') or {} for row in previews]
    return {'typedSaveChecks': len(typed), 'completeTypedSaveChecks': sum(model.get('complete') is True for model in typed),
            'incompleteTypedSaveChecks': sum(model.get('complete') is not True for model in typed),
            'inheritedParameterPreviews': sum(data.get('parameterSource') == 'inherited-source-parameters' for data in metadata),
            'inheritedIndependentComparisons': sum((data.get('readonlyParameterComparison') or {}).get('source')
                 == 'inherited-source-parameters' for data in metadata),
            'warning': 'Repeated preview/check samples. Typed bonus presence and parameter provenance do not validate actual save odds or damage.'}


def area_scheduling(previews):
    entries = [(row.get('spellMetadata') or {}).get('areaScheduling') for row in previews]
    entries = [entry for entry in entries if isinstance(entry, dict)
               and type(entry.get('targetCount')) is int and entry['targetCount'] > 1]
    times = sorted(entry['computeMsBeforeLogging'] for entry in entries
                   if type(entry.get('computeMsBeforeLogging')) in (int, float)
                   and 0 <= entry['computeMsBeforeLogging'] < float('inf'))
    return {
        'samples': len(entries), 'timedSamples': len(times),
        'maxTargetComputeMs': max(times, default=None),
        'meanTargetComputeMs': sum(times) / len(times) if times else None,
        'targetOver4ms': sum(value > 4 for value in times),
        'targetOver16ms': sum(value > 16 for value in times),
        'maxPending': max((entry.get('pendingAfterTarget', 0) for entry in entries), default=0),
        'maxRecordedProcessedThisFrame': max((entry.get('processedThisFrame', 0) for entry in entries), default=0),
        'warning': 'Rate-limited preview samples; target timing excludes logging and is not full-frame CPU or FPS.'
    }


def read_rows(path):
    if zipfile.is_zipfile(path):
        with zipfile.ZipFile(path) as archive:
            candidates = [i for i in archive.infolist() if i.filename.lower().endswith('/combatpreview.diagnostics.jsonl')
                          or i.filename.lower() == 'combatpreview.diagnostics.jsonl']
            if len(candidates) != 1:
                raise ValueError('Expected exactly one current diagnostics JSONL file in the ZIP')
            current = candidates[0]
            previous = [i for i in archive.infolist() if i.filename.lower() == current.filename.lower() + '.prev']
            if len(previous) > 1:
                raise ValueError('Duplicate rotated diagnostic files')
            selected = previous + [current]
            if sum(i.file_size for i in selected) > 33554432:
                raise ValueError('Diagnostic input exceeds the 32 MiB analysis limit')
            content = '\n'.join(archive.read(i).decode('utf-8-sig') for i in selected)
    else:
        previous = Path(str(path) + '.prev')
        selected = ([previous] if previous.exists() else []) + [path]
        if sum(p.stat().st_size for p in selected) > 33554432:
            raise ValueError('Diagnostic input exceeds the 32 MiB analysis limit')
        content = '\n'.join(p.read_text(encoding='utf-8-sig') for p in selected)
    rows = [json.loads(line) for line in content.splitlines() if line.strip()]
    if not rows:
        raise ValueError('No diagnostic records; an empty log is not a successful observation test')
    if any(not isinstance(r, dict) or r.get('schema') not in (1, 2) for r in rows):
        raise ValueError('Unsupported diagnostic schema')
    return rows


def spell_damage_candidates(previews, observations):
    """Pair nearby records for review, retaining the uncertainty of timing-based matches."""
    from bisect import bisect_right
    import re

    by_identity = {}
    for preview in previews:
        metadata = preview.get('spellMetadata') or {}
        identity = tuple(preview.get(k) for k in ('sessionId', 'attacker', 'target', 'weapon'))
        if all(identity) and isinstance(preview.get('elapsedMs'), int) and metadata.get('spellDamage'):
            by_identity.setdefault(identity, []).append(preview)
    for records in by_identity.values():
        records.sort(key=lambda row: row['elapsedMs'])

    statuses = Counter()
    cases = []
    casts = set()
    for observation in observations:
        detail = observation.get('details') or {}
        if detail.get('checkKind') != 'damage':
            continue
        identity = tuple(observation.get(k) for k in ('sessionId', 'attacker', 'target')) + (detail.get('abilityInstanceId'),)
        timestamp = observation.get('elapsedMs')
        if not all(identity) or not detail.get('castContextId') or not isinstance(timestamp, int):
            statuses['MissingCastIdentity'] += 1
            continue
        casts.add((identity[0], detail['castContextId'], identity[2]))
        records = by_identity.get(identity, [])
        index = bisect_right(records, timestamp, key=lambda row: row['elapsedMs']) - 1
        # Projectile travel can exceed the tight hover window. Allow up to 4s
        # only when the same turn is independently identified on both records.
        same_turn = (index >= 0 and observation.get('turn') not in (None, '', 'none', 'unknown')
                     and observation.get('turn') == records[index].get('turn'))
        if index < 0 or timestamp - records[index]['elapsedMs'] > (4000 if same_turn else 1500):
            statuses['NoRecentPreview'] += 1
            continue
        preview = records[index]
        metadata = preview['spellMetadata']
        if not detail.get('blueprintId') or metadata.get('blueprintId') != detail['blueprintId']:
            statuses['BlueprintMismatch'] += 1
            continue
        damage = metadata['spellDamage']
        trace = detail.get('damageTrace') or {}
        terms = damage.get('terms', [])
        parts = trace.get('components', [])
        if (damage.get('scanTruncated') or trace.get('componentsTruncated')
                or len(terms) != 1 or len(parts) != 1
                or terms[0].get('branchCandidate') or terms[0].get('path') != 'cast'
                or terms[0].get('hpDamage') is not True
                or terms[0].get('unavailableReason')):
            statuses['ComplexDamageShape'] += 1
            continue
        term, part = terms[0], parts[0]
        if term.get('damageType') != part.get('kind'):
            statuses['DamageKindMismatch'] += 1
            continue
        match = re.fullmatch(r'(\d+)d(\d+)', str(part.get('diceModified') or part.get('diceBase')), re.IGNORECASE)
        if not match or type(term.get('dice')) is not int or type(term.get('sides')) is not int:
            statuses['DiceNotComparable'] += 1
            continue
        actual_dice = tuple(map(int, match.groups()))
        status = 'CandidateDiceMatch' if actual_dice == (term['dice'], term['sides']) else 'CandidateDiceDifference'
        statuses[status] += 1
        if len(cases) < 32:
            cases.append({
                'status': status, 'previewSequence': preview.get('recordSequence'),
                'observationSequence': observation.get('recordSequence'), 'sessionId': identity[0],
                'castContextId': detail['castContextId'], 'ageMs': timestamp - preview['elapsedMs'],
                'sameTurn': same_turn,
                'previewDice': [term['dice'], term['sides']], 'actualDice': actual_dice,
                'previewBonus': term.get('bonus'), 'actualBonus': part.get('bonus'),
                'actualTargetRelatedBonus': part.get('bonusTargetRelated'),
                'actualTotalStoredBonus': part['bonus'] + part['bonusTargetRelated']
                    if type(part.get('bonus')) is int and type(part.get('bonusTargetRelated')) is int else None,
                'previewDamageSources': term.get('additionalDamageSources'),
                'actualDamageModifiers': part.get('damageModifiers'),
                'observedReduction': part.get('reduction'), 'observedFinal': part.get('finalValue')
            })
    return {'statuses': dict(statuses), 'castTargetGroups': len(casts), 'cases': cases,
            'warning': 'Nearby previews are candidates. Dice differences do not establish a model error.'}


def probability_review(rows):
    """Readiness and observed rerolls; no inference of advantage from a buff name."""
    previews = [r for r in rows if r.get('kind') == 'spell-preview']
    damage_previews = [r for r in previews if any(
        term.get('hpDamage') is True for term in ((r.get('spellMetadata') or {}).get('spellDamage') or {}).get('terms', []))]
    groups = {}
    for row in previews:
        key = tuple(row.get(k) for k in ('sessionId', 'attacker', 'target', 'weapon', 'turn'))
        if all(key) and type(row.get('elapsedMs')) is int:
            groups.setdefault(key, []).append(row)
    regressions, recoveries = [], 0
    for group in groups.values():
        group.sort(key=lambda row: row['elapsedMs'])
        for previous, current in zip(group, group[1:]):
            before, after = previous.get('spellMetadata') or {}, current.get('spellMetadata') or {}
            left = (before.get('healthProjection') or {}).get('expectedHpLoss')
            right = (after.get('healthProjection') or {}).get('expectedHpLoss')
            recoveries += left is None and right is not None
            if before.get('parameterStatus') in ('Current', 'ReadOnlyCalculated') and after.get('parameterStatus') == 'StateChanged':
                regressions.append({'beforeSequence': previous.get('recordSequence'), 'afterSequence': current.get('recordSequence'),
                    'gapMs': current['elapsedMs'] - previous['elapsedMs'], 'inputStampChanged': before.get('inputStamp') != after.get('inputStamp'),
                    'beforeHpLoss': left, 'afterHpLoss': right})
    saves = [r for r in rows if r.get('kind') == 'spell-observation' and (r.get('details') or {}).get('checkKind') == 'save']
    multiple = [r for r in saves if len((r.get('details') or {}).get('rollHistory') or []) > 1]
    dice_traces = []
    for row in rows:
        if row.get('kind') == 'spell-observation':
            details = row.get('details') or {}
            trace = details.get('d20') or (details.get('visibility') or {}).get('d20')
            kind = details.get('checkKind')
        elif row.get('kind') in ('observation', 'unassociated-observation'):
            trace = (row.get('visibility') or {}).get('d20')
            kind = 'weapon'
        else:
            continue
        if trace:
            dice_traces.append({'recordSequence': row.get('recordSequence'), 'kind': kind, **trace})
    timelines = [r.get('spellMetadata', {}).get('hoverReadiness') for r in previews]
    timelines = [timeline for timeline in timelines if timeline]
    latency = [timeline['firstHpProjectionReadyMs'] for timeline in timelines
               if type(timeline.get('firstHpProjectionReadyMs')) in (int, float)]
    parameter_differences = [r for r in previews if (r.get('spellMetadata', {}).get('readonlyParameterComparison') or {}).get('available')
                             and any((r['spellMetadata']['readonlyParameterComparison'].get(key) or 0) != 0
                                     for key in ('dcDelta', 'casterLevelDelta', 'rankBonusDelta'))]
    return {
        'directDamagePreviews': len(damage_previews),
        'directDamageHpProjectionMissing': sum((r.get('spellMetadata', {}).get('healthProjection') or {}).get('expectedHpLoss') is None for r in damage_previews),
        'sameIdentityMissingToAvailable': recoveries,
        'sameIdentityParameterRegressions': regressions[:16],
        'observedSaveRolls': len(saves), 'observedSaveMultiRolls': len(multiple),
        'saveStatModifierTraces': sum((r.get('details') or {}).get('statModifiers') is not None for r in saves),
        'saveStatModifierSources': dict(Counter(entry.get('source') or 'unlabelled' for r in saves
            for entry in ((r.get('details') or {}).get('statModifiers') or {}).get('entries', []))),
        'multiRollPersistentFlags': dict(Counter(str(r['details'].get('persistent')) for r in multiple)),
        'multiRollCases': [{'recordSequence': r.get('recordSequence'), 'rollHistory': r['details'].get('rollHistory'),
                          'keptDie': r['details'].get('die'), 'persistent': r['details'].get('persistent'),
                          'rerollSourcesLogged': bool(r['details'].get('rerolls') or (r['details'].get('d20') or {}).get('sources'))} for r in multiple[:16]],
        'd20SourceTraces': len(dice_traces),
        'd20SourceKinds': dict(Counter(trace['kind'] for trace in dice_traces)),
        'd20SourceCases': [trace for trace in dice_traces if trace.get('sources') or trace.get('replaceOneWithMax')][:16],
        'hoverTimelineSamples': len(timelines),
        'maximumObservedHpReadinessMs': max(latency, default=None),
        'readonlyParameterDifferenceCases': [{'recordSequence': r.get('recordSequence'),
            **r['spellMetadata']['readonlyParameterComparison']} for r in parameter_differences[:16]],
        'parameterFallbackReasons': dict(Counter(r.get('spellMetadata', {}).get('parameterFallbackReason') for r in previews
                                                if r.get('spellMetadata', {}).get('parameterFallbackReason'))),
        'warning': 'Unavailable HP is not zero damage. Roll history alone does not identify the source or certify advantage. Refresh transitions are sampled logs, not a cursor-event trace.'
    }


def weapon_roll_damage_review(rows):
    def number(value):
        return type(value) in (int, float) and -float('inf') < value < float('inf')
    def identity(value):
        return str(value).split(':', 1)[0] if value is not None else None
    previews = {(row.get('sessionId'), row.get('previewId')): row for row in rows if row.get('kind') == 'beta-preview'}
    rolls = {(row.get('sessionId'), str(row.get('eventId'))): row for row in rows if row.get('kind') == 'observation'}
    grouped = {}
    for row in rows:
        if row.get('kind') == 'damage-observation' and row.get('hasWeaponAttack') is True and row.get('rollEventId'):
            key = (row.get('sessionId'), str(row['rollEventId']), identity(row.get('target')))
            grouped.setdefault(key, []).append(row)
    checked = explained = roll_review_count = 0
    roll_cases = []
    for row in rolls.values():
        visibility = row.get('visibility') or {}
        d20 = visibility.get('d20') or {}
        die, ab, ac = d20.get('result'), row.get('ab'), row.get('ac')
        if row.get('isFake') is True or type(row.get('hit')) is not bool or row.get('rolled') is not True or type(die) is not int or not 1 <= die <= 20 or not number(ab) or not number(ac):
            continue
        checked += 1
        ac_pass = die == 20 or die > 1 and die + ab >= ac
        if ac_pass == row.get('hit'):
            continue
        known_miss = visibility.get('concealmentPassed') is False or (visibility.get('hitMirrorImageIndex') or 0) > 0
        if ac_pass and known_miss:
            explained += 1
            continue
        roll_review_count += 1
        if len(roll_cases) < 16:
            roll_cases.append({'sessionId': row.get('sessionId'), 'recordSequence': row.get('recordSequence'),
                'eventId': row.get('eventId'), 'die': die, 'ab': ab, 'ac': ac, 'hit': row.get('hit'),
                'acOnlyPass': ac_pass, 'rollHistory': d20.get('history'), 'signedRerollCount': d20.get('signedRerollCount'),
                'resultOverride': d20.get('resultOverride'), 'attackResult': visibility.get('attackResult'),
                'status': 'ReviewSpecialRuleOrArithmetic'})
    damage_cases = []
    unavailable = Counter()
    compared = outlier_count = 0
    for (session, roll_id, target), events in grouped.items():
        roll = rolls.get((session, roll_id))
        if not roll or identity(roll.get('target')) != target or roll.get('hit') is not True:
            unavailable['NoAssociatedLandedRoll'] += 1
            continue
        traces = [event.get('damageTrace') or {} for event in events]
        ids = {trace.get('previewId') for trace in traces}
        preview = previews.get((session, next(iter(ids)))) if len(ids) == 1 else None
        if not preview or identity(preview.get('betaActorId')) != identity(roll.get('attacker')) or identity(preview.get('betaTargetId')) != target or identity(preview.get('betaWeaponId')) != identity(roll.get('weapon')):
            unavailable['NoAssociatedPreviewIdentity'] += 1
            continue
        indices = {trace.get('previewRowIndex') for trace in traces}
        index = next(iter(indices)) if len(indices) == 1 else None
        exact_index = type(index) is int
        if not exact_index:
            index = roll.get('attackIndex')
        model_rows = preview.get('rows') or []
        if type(index) is not int or index < 0 or index >= len(model_rows):
            unavailable['PreviewRowUnavailable'] += 1
            continue
        model = model_rows[index]
        critical = roll.get('criticalConfirmed') is True
        mean_basis = 'Critical' if critical else 'Normal'
        mean = model.get('meanOnCriticalHitAfterDefense' if critical else 'meanOnNormalHitAfterDefense')
        if mean is None:
            if critical:
                unavailable['CriticalMeanNotLogged'] += 1
                continue
            mean = model.get('meanOnHitAfterDefense')
            mean_basis = 'LegacyConditionalIncludingCriticalChance'
        actuals = [event.get('reportedDamage') for event in events]
        if not number(mean) or mean < 0 or not all(number(value) and value >= 0 for value in actuals):
            unavailable['DamageOrConditionalMeanUnavailable'] += 1
            continue
        compared += 1
        actual = sum(actuals)
        outlier = actual > 0 if mean == 0 else actual > 2 * mean or actual < .25 * mean
        if outlier:
            outlier_count += 1
        if outlier and len(damage_cases) < 24:
            damage_cases.append({'sessionId': session, 'rollEventId': roll_id, 'previewId': next(iter(ids)),
                'recordSequences': [event.get('recordSequence') for event in events], 'damageEventCount': len(events),
                'actualDamageSum': actual, 'previewMeanOnHit': mean, 'meanBasis': mean_basis, 'previewChanceWeightedExpected': model.get('expectedAfterDefense'),
                'previewHitChance': model.get('hit'), 'ratioToMeanOnHit': actual / mean if mean else None,
                'previewDamageComponents': preview.get('damageComponents'),
                'actualComponentTraces': [{'recordSequence': event.get('recordSequence'), 'eventId': event.get('eventId'),
                    'difficultyModifier': (event.get('damageTrace') or {}).get('difficultyModifier'),
                    'components': [{key: part.get(key) for key in ('kind', 'diceBase', 'diceModified', 'bonus', 'rollResult',
                        'valueWithoutReduction', 'reduction', 'finalValue', 'precision', 'sneak', 'criticalApplied')}
                        for part in (event.get('damageTrace') or {}).get('components') or []]} for event in events],
                'previewRowIndex': index, 'rowAssociation': 'RecordedRow' if exact_index else 'AttackIndexCandidate',
                'comparisonStatuses': sorted({str(trace.get('previewComparisonStatus')) for trace in traces}),
                'd20': (roll.get('visibility') or {}).get('d20'), 'status': 'OutlierCandidateNotConfirmedError'})
    return {'checkedActualRolls': checked, 'explainedConcealmentOrImageMisses': explained,
        'rollReviewCaseCount': roll_review_count, 'rollReviewCases': roll_cases, 'damageGroupsWithConditionalMean': compared,
        'damageUnavailableReasons': dict(unavailable), 'damageOutlierCount': outlier_count, 'damageOutlierCandidates': damage_cases,
        'warning': 'Damage events are grouped by attack roll and target; outliers use conditional hit mean, not hit-weighted expectation. Critical means and unmatched identities are excluded. Candidate row/state changes, variance, extra damage and difficulty require review; a single outlier or roll does not validate or invalidate a probability.'}


def raw_evidence_coverage(rows):
    attacks = [row.get('rawInputs') or {} for row in rows if row.get('kind') == 'observation']
    traces = [row.get('damageTrace') or {} for row in rows if row.get('kind') == 'damage-observation']
    spells = [row.get('details') or {} for row in rows if row.get('kind') == 'spell-observation']
    traces += [detail.get('damageTrace') or {} for detail in spells if detail.get('checkKind') == 'damage']
    raw_counters = [row.get('rawRateLimitedRecords') for row in rows if type(row.get('rawRateLimitedRecords')) is int]
    return {'attackRowsWithFinalRawInputs': sum(raw.get('version') == 1 for raw in attacks),
        'attackRowsWithCapturedACModifiers': sum(isinstance((raw.get('armorClassRule') or {}).get('statModifiersAtTheMoment'), dict) for raw in attacks),
        'damageRowsWithCompletionDefense': sum(isinstance(trace.get('defenseAtCompletion'), dict) for trace in traces),
        'weaponRowsWithRetainedDisplay': sum(isinstance(raw.get('displayedInputs'), dict) for raw in attacks),
        'spellRowsWithFrozenDisplay': sum(isinstance(detail.get('frozenDisplayedInputs'), dict) for detail in spells),
        'rowsWithRawBudgetCounter': len(raw_counters), 'maxRawDroppedCounter': max(raw_counters, default=None),
        'warning': 'Coverage is per retained row and can include the same event in multiple logs. Nonzero coverage is not exhaustive provenance or numerical correctness; inspect captureFailure, omitted/truncated fields and raw rate-limit counters.'}


def health_projection_review(rows):
    """Review bounds/coverage, never compare a full-sequence mean to one hit."""
    def finite(value):
        return type(value) in (int, float) and math.isfinite(value)
    counts, issues, bases = Counter(), [], Counter()
    def review(value, row, source):
        if not isinstance(value, dict):
            return
        counts[source] += 1
        bases[value.get('basis', 'unspecified')] += 1
        hp, maximum, temp = (value.get(k) for k in ('currentHp', 'maximumHp', 'temporaryHp'))
        body, absorbed = value.get('expectedHpLoss'), value.get('expectedTemporaryHpLoss')
        problems = []
        if finite(temp) and temp > 0:
            counts['withTemporaryHp'] += 1
        if value.get('approximate') is True:
            counts['approximate'] += 1
        if body is None:
            counts['mainLossUnavailable'] += 1
        if absorbed is None:
            counts['temporaryLossUnavailable'] += 1
        for name, number, cap in [('expectedHpLoss', body, min(hp, maximum) if finite(hp) and finite(maximum) else None),
                                  ('expectedTemporaryHpLoss', absorbed, temp)]:
            if number is not None and (not finite(number) or number < -1e-8 or finite(cap) and number > max(0, cap) + 1e-8):
                problems.append(name + '-outside-resource-bounds')
        if value.get('status') == 'Computed' and (body is None or absorbed is None):
            problems.append('computed-projection-missing-loss')
        if problems:
            counts['invalidProjectionRecords'] += 1
            if len(issues) < 20:
                issues.append({'sessionId': row.get('sessionId'), 'recordSequence': row.get('recordSequence'),
                               'source': source, 'issues': problems, 'healthProjection': value})
    for row in rows:
        if row.get('kind') == 'beta-preview':
            review(row.get('healthProjection'), row, 'weaponPreview')
        if row.get('kind') == 'spell-preview':
            review((row.get('spellMetadata') or {}).get('healthProjection'), row, 'spellPreview')
        if row.get('kind') == 'observation':
            review((((row.get('rawInputs') or {}).get('displayedInputs') or {}).get('healthProjection')), row, 'retainedWeaponDisplay')
        if row.get('kind') == 'spell-observation':
            detail = row.get('details') or {}
            review((detail.get('frozenDisplayedInputs') or {}).get('healthProjection'), row, 'retainedSpellDisplay')
        traces = [row.get('damageTrace'), (row.get('details') or {}).get('damageTrace')]
        for trace in traces:
            if isinstance(trace, dict) and isinstance(trace.get('healthAtCompletion'), dict):
                counts['completedHealthRecords'] += 1
                if (trace['healthAtCompletion'].get('temporaryHp') or 0) > 0:
                    counts['completedWithTemporaryHp'] += 1
    return {'counts': dict(counts), 'bases': dict(bases), 'issues': issues,
            'warning': 'Counts are per retained record, not unique casts/attacks. Bounds only; a valid record is not proof of prediction accuracy. Whole-sequence HP loss must not be compared to one hit. Final HP alone does not reveal absorbed temporary HP or intervening effects.'}


def performance_review(rows):
    records = [r for r in rows if r.get('kind') == 'performance-window' and isinstance(r.get('performance'), dict)]
    windows = [r['performance'] for r in records]
    stages = {}
    for record in records:
        window = record['performance']
        for metric in window.get('stages') or []:
            if not isinstance(metric, dict) or not isinstance(metric.get('stage'), str):
                continue
            stage = stages.setdefault(metric['stage'], {'stage': metric['stage'], 'calls': 0, 'maximumMs': 0, 'over4ms': 0, 'over16ms': 0})
            for key in ('calls', 'over4ms', 'over16ms'):
                value = metric.get(key)
                if type(value) is int and value >= 0:
                    stage[key] += value
            maximum = metric.get('maximumMs')
            if type(maximum) in (int, float) and math.isfinite(maximum) and maximum >= stage['maximumMs']:
                stage.update({'maximumMs': maximum, 'maximumFrame': metric.get('maximumFrame'), 'sessionId': record.get('sessionId'), 'gc0AtMaximum': metric.get('gc0AtMaximum'),
                              'allocatedMaximumBytes': metric.get('allocatedMaximumBytes')})
    return {'windows': len(windows), 'frames': sum(w.get('frames', 0) for w in windows),
            'slowFramesOver33ms': sum(w.get('over33ms', 0) for w in windows),
            'slowFramesOver100ms': sum(w.get('over100ms', 0) for w in windows),
            'stagesByMaximumMs': sorted(stages.values(), key=lambda m: m['maximumMs'], reverse=True)[:30],
            'manualScenarioWindows': sum((w.get('settings') or {}).get('manualScenario') is True for w in windows),
            'warning': 'No windows means performance is unmeasured. All-game frame times include engine/other mods; nested stages overlap and must not be summed. Frame numbers are local to each session.'}


def retained_input_review(rows):
    counts, examples = Counter(), []
    for row in rows:
        if row.get('kind') != 'observation':
            continue
        raw = row.get('rawInputs') or {}
        shown = raw.get('displayedInputs') or {}
        if not isinstance(shown, dict) or shown.get('ab') is None or shown.get('ac') is None:
            continue
        counts['retainedRows'] += 1
        actual_ab, actual_ac = raw.get('finalAB'), raw.get('finalAC')
        if actual_ab is None or actual_ac is None:
            counts['actualInputsUnavailable'] += 1
            continue
        same = actual_ab == shown['ab'] and actual_ac == shown['ac']
        counts['abAcNumericallyEqual' if same else 'abAcNumericallyDifferent'] += 1
        if not same and len(examples) < 20:
            examples.append({'sessionId': row.get('sessionId'), 'recordSequence': row.get('recordSequence'),
                'previewId': shown.get('previewId'), 'shownAb': shown['ab'], 'actualAb': actual_ab,
                'shownAc': shown['ac'], 'actualAc': actual_ac,
                'association': shown.get('rowAssociation'), 'comparisonStatus': shown.get('comparisonStatus'),
                'verdict': 'InputDifferenceOnlyNotConfirmedModelError'})
    return {'counts': dict(counts), 'examples': examples,
            'warning': 'Exact input values, not sampled hit rates. A retained previous row or changed conditions do not certify that the actual attack used the displayed conditions. Equal values alone do not validate all roll semantics.'}


def attack_association_review(rows):
    counts, statuses, examples = Counter(), Counter(), []
    for row in rows:
        if row.get('kind') != 'beta-comparison':
            continue
        shown = row.get('displayedInputs') or {}
        link = shown.get('attackLink') or {}
        counts['comparisonRecords'] += 1
        statuses[link.get('status') or 'LegacyOrLinkUnavailable'] += 1
        if link.get('exactRuleIdentity') is True:
            counts['exactRowIdentity'] += 1
            if row.get('status') == 'InputsChanged':
                counts['exactRowButInputsChanged'] += 1
        if isinstance(shown.get('inputAtAttackBegin'), dict):
            counts['frozenBeginInputs'] += 1
        if len(examples) < 20 and (not link.get('exactRuleIdentity') or row.get('status') != 'ComparedPartialModel'):
            examples.append({'sessionId': row.get('sessionId'), 'recordSequence': row.get('recordSequence'),
                'comparisonStatus': row.get('status'), 'attackLink': link,
                'inputChanges': row.get('inputChanges') or []})
    return {'counts': dict(counts), 'linkStatuses': dict(statuses), 'examples': examples,
            'warning': 'Exact rule identity certifies row association only. Changed conditions still invalidate numerical accuracy comparisons.'}


def independent_reference_review(rows):
    counts = Counter()
    for row in rows:
        reference = row.get('hcoReference') if row.get('kind') == 'beta-preview' else (row.get('spellMetadata') or {}).get('hcoReference') if row.get('kind') == 'spell-preview' else None
        if not isinstance(reference, dict):
            continue
        counts['referenceRecords'] += 1
        for difference in reference.get('differences') or []:
            counts['checkPairs'] += 1
            model = difference.get('localD20Model') or {}
            if difference.get('localPersistentSave') is True:
                counts['localPersistentSavePairs'] += 1
            if model.get('modifiers') or model.get('afterRollDirections') or model.get('replaceOneWithMax'):
                counts['localAdvancedRollPairs'] += 1
            if difference.get('probabilityEquivalenceVerified') is False:
                counts['probabilityEquivalenceNotVerified'] += 1
    return {'counts': dict(counts), 'authority': 'ReferenceOnly',
            'warning': 'HCO is an independent implementation, not ground truth. Persistent/favorable/reroll and multi-save probabilities must be verified against completed game inputs or independent enumeration, not forced to match HCO. Raw differences remain evidence only.'}


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
    spell_previews = [r for r in rows if r.get('kind') == 'spell-preview']
    spell_observations = [r for r in rows if r.get('kind') == 'spell-observation']
    hco = [ref for r in rows for ref in [r.get('hcoReference') if r.get('kind') == 'beta-preview'
        else (r.get('spellMetadata') or {}).get('hcoReference') if r.get('kind') == 'spell-preview' else None] if isinstance(ref, dict)]
    hco_deltas = [d for ref in hco if ref.get('status') == 'ComparablePartialModel' for d in ref.get('differences', [])]
    traces = [r['damageTrace'] for r in damage if isinstance(r.get('damageTrace'), dict)]
    trace_parts = [p for t in traces for p in t.get('components', [])]
    return {
        'records': len(rows), 'kinds': dict(Counter(r.get('kind') for r in rows)),
        'probabilityReview': probability_review(rows),
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
        'weaponNumericDeltasByStatus': dict(Counter(r.get('status') for r in beta_comparisons
            if (r.get('abDelta') is not None and r['abDelta'] != 0) or (r.get('acDelta') is not None and r['acDelta'] != 0))),
        'hcoReferenceStatuses': dict(Counter(ref.get('status') for ref in hco)),
        'hcoRawDifferences': dict(Counter(str(d.get('kind')) + ':' + field for d in hco_deltas
            for field in ('bonusDelta', 'thresholdDelta', 'rawRollProbabilityDelta')
            if d.get(field) is not None and abs(d[field]) > 1e-9)),
        'hcoReferenceWarning': 'HCO is a correlated reference model, not verified game truth; combined effect percent differs from basic roll odds.',
        'spellParameterStatuses': dict(Counter((r.get('spellMetadata') or {}).get('parameterStatus', 'legacy-unknown') for r in spell_previews)),
        'spellCheckStatuses': dict(Counter(str(c.get('kind')) + ':' + str(c.get('status'))
            for r in spell_previews for c in (r.get('checkDetails') or []))),
        'spellUnavailableReasons': dict(Counter(str(c.get('kind')) + ':' + str(c.get('reason'))
            for r in spell_previews for c in (r.get('checkDetails') or []) if c.get('probability') is None)),
        'spellDamageCandidates': spell_damage_candidates(spell_previews, spell_observations),
        'spellTargetScaling': spell_target_scaling(spell_previews),
        'spellAreaScheduling': area_scheduling(spell_previews),
        'conditionalSaveReview': conditional_save_review(spell_previews),
        'weaponRollDamageReview': weapon_roll_damage_review(rows),
        'rawEvidenceCoverage': raw_evidence_coverage(rows),
        'retainedInputReview': retained_input_review(rows),
        'attackAssociationReview': attack_association_review(rows),
        'independentReferenceReview': independent_reference_review(rows),
        'healthProjectionReview': health_projection_review(rows),
        'performanceReview': performance_review(rows),
        'parameterComparisonReview': parameter_comparison_review(spell_previews),
        'secondaryTargetPreviewSamples': sum(((row.get('spellMetadata') or {}).get('secondaryTargetEffects') or {}).get('status')
            == 'Unmodeled' for row in spell_previews),
        'approximateShieldWeaponRows': sum(row.get('afterDefenseApproximate') is True for preview in rows
                                          if preview.get('kind') == 'beta-preview' for row in preview.get('rows', [])),
        'approximateShieldSpellPreviews': sum(((preview.get('spellMetadata') or {}).get('healthProjection') or {}).get('approximate') is True
                                             for preview in spell_previews),
        'shieldApproximationWarning': 'Mean-state protection estimates are labeled approximations, not exact distribution or observed-damage validation.',
        'spellObservedChecks': dict(Counter((r.get('details') or {}).get('checkKind') for r in spell_observations)),
        'spellUnresolvedSources': sum((r.get('details') or {}).get('sourceResolved') is not True for r in spell_observations),
        'spellComparisonWarning': 'Spell observations and numeric deltas do not verify cast-to-preview conditions or validate probabilities from single rolls.',
        'damageTraceRecords': len(traces),
        'damageTraceComponents': len(trace_parts),
        'componentArithmeticDifferences': sum(p.get('reconstructionMatches') is False for p in trace_parts),
        'staticDefenseReplayDifferences': sum(p.get('previewStaticDefenseMatches') is False for p in trace_parts),
        'staticDefenseReplayComparedComponents': sum(type(p.get('previewStaticDefenseMatches')) is bool for p in trace_parts),
        'staticDefenseReplayUnavailable': dict(Counter(p.get('replayUnavailableReason') for p in trace_parts if p.get('replayUnavailableReason'))),
        'previewUnmodeledAdjustments': dict(Counter(a for t in traces for a in t.get('previewUnmodeledAdjustments', []))),
        'truncatedDamageTraces': sum(t.get('componentsTruncated') is True for t in traces),
        'damageTraceWarning': 'Static replay starts AFTER observed multipliers/immunity. Agreement does not validate preview halving, immunity or expected damage.',
        'damageTraceCases': [
            {'sessionId': r.get('sessionId'), 'recordSequence': r.get('recordSequence'),
             'eventId': r.get('eventId'), 'rollEventId': r.get('rollEventId'),
             'reportedDamage': r.get('reportedDamage'), 'beforeDifficulty': r.get('beforeDifficulty'),
             'previewId': r['damageTrace'].get('previewId'),
             'unmodeledAdjustments': r['damageTrace'].get('previewUnmodeledAdjustments', []),
             'missing': r['damageTrace'].get('missing', []),
             'components': [{k: p.get(k) for k in (
                 'index', 'kind', 'rollResult', 'rollAndBonusValue', 'valueWithoutReduction',
                 'reduction', 'finalValue', 'reconstructedFinal', 'previewStaticDefenseReplay',
                 'previewStaticDefenseMatches', 'replayUnavailableReason')}
                 for p in r['damageTrace'].get('components', [])]}
            for r in damage if isinstance(r.get('damageTrace'), dict)
            and (r['damageTrace'].get('previewUnmodeledAdjustments')
                 or any(p.get('previewStaticDefenseMatches') is False or p.get('reconstructionMatches') is False
                        for p in r['damageTrace'].get('components', [])))][:32],
        'warning': 'Legacy unassociated rolls are not proven fake; these counts do not validate preview accuracy or state/RNG preservation.'
    }


def version_assessment(latest_session, expected_version):
    import re
    def normalized(value):
        if not isinstance(value, str) or not re.fullmatch(r'\d+\.\d+\.\d+(?:\.\d+)?', value):
            return None
        return tuple(map(int, value.split('.'))) + ((0,) if value.count('.') == 2 else ())
    expected = normalized(expected_version)
    if expected is None:
        raise ValueError('Expected version must be numeric major.minor.patch[.revision]')
    runtimes = (latest_session or {}).get('runtimeMetadata') or []
    versions = [runtime.get('modVersion') for runtime in runtimes]
    consistent = (latest_session or {}).get('runtimeMetadataConsistent') is True
    matched = consistent and len(versions) == 1 and normalized(versions[0]) == expected
    return {'expectedVersion': expected_version, 'latestRuntimeVersions': versions,
            'status': 'RuntimeVersionMatched' if matched else 'RuntimeVersionUnverifiedOrDifferent',
            'canReviewRequestedRuntime': matched,
            'warning': 'Version match only establishes logged runtime identity. Feature absence or zero differences without comparable events does not establish correctness or FPS improvement.'}


def summarize_file(rows, expected_version=None):
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
    latest = next((s for s in sessions if s['sessionId'] == latest_id), None) if latest_id else None
    result = {**summarize(rows), 'sessions': sessions, 'latestIdentifiedSession': latest}
    if expected_version is not None:
        result['versionAssessment'] = version_assessment(latest, expected_version)
    return result


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('input', type=Path)
    parser.add_argument('--expected-version', help='Require this runtime identity when reviewing a release')
    args = parser.parse_args()
    print(json.dumps(summarize_file(read_rows(args.input), args.expected_version), ensure_ascii=False, indent=2))
