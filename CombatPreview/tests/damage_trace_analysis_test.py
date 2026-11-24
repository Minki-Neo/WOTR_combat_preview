"""Regression tests: deterministic reduction differences are not unlucky rolls."""
import importlib.util
from pathlib import Path
import unittest
import tempfile
import json
import zipfile

spec = importlib.util.spec_from_file_location('analyzer', Path(__file__).parents[1] / 'tools/analyze_diagnostics.py')
analyzer = importlib.util.module_from_spec(spec)
spec.loader.exec_module(analyzer)


class DamageAnalysisTests(unittest.TestCase):
    def test_typed_save_and_inherited_parameter_provenance_are_not_validation(self):
        rows = [{'kind': 'spell-preview', 'checkDetails': [{'kind': 'save', 'd20Model': {
            'complete': True, 'modifiers': [{'saveBonusDescriptor': 15}]}}, {'kind': 'save', 'd20Model': {
            'complete': False, 'modifiers': [{'saveBonusDescriptor': 15}]}}, {'kind': 'attack', 'd20Model': {
            'complete': True, 'modifiers': [{'saveBonusDescriptor': 15}]}}],
            'spellMetadata': {'parameterSource': 'inherited-source-parameters'}}, {'kind': 'spell-preview'}]
        report = analyzer.conditional_save_review(rows)
        self.assertEqual(report['typedSaveChecks'], 2)
        self.assertEqual(report['completeTypedSaveChecks'], 1)
        self.assertEqual(report['incompleteTypedSaveChecks'], 1)
        self.assertEqual(report['inheritedParameterPreviews'], 1)
        self.assertEqual(report['inheritedIndependentComparisons'], 0)

    def test_parameter_comparison_coverage_excludes_missing_invalid_and_unavailable(self):
        def row(available, dc=0, cl=0, rank=0):
            return {'spellMetadata': {'readonlyParameterComparison': {'available': available,
                'dcDelta': dc, 'casterLevelDelta': cl, 'rankBonusDelta': rank}}}
        report = analyzer.parameter_comparison_review([row(True), row(True, cl=2), row(False),
            row(True, dc=None), row(True, rank=float('nan')), row(True, dc=True), {}])
        self.assertEqual(report['validComparisons'], 2)
        self.assertEqual(report['matchingNumericSamples'], 1)
        self.assertEqual(report['differentNumericSamples'], 1)
        self.assertEqual(report['invalidDeltaSamples'], 3)
        self.assertEqual(report['unavailableComparisons'], 1)
        self.assertEqual(report['noComparisonSamples'], 1)

    def test_weapon_outlier_uses_conditional_mean_and_groups_damage_events(self):
        preview = {'kind': 'beta-preview', 'sessionId': 's', 'previewId': 'p', 'betaActorId': 'a', 'betaTargetId': 't', 'betaWeaponId': 'w',
            'rows': [{'meanOnHitAfterDefense': 10, 'expectedAfterDefense': 1, 'hit': .1}]}
        roll = {'kind': 'observation', 'sessionId': 's', 'eventId': 'r', 'attacker': 'a:name', 'target': 't:name', 'weapon': 'w:name',
            'rolled': True, 'hit': True, 'criticalConfirmed': False, 'attackIndex': 0, 'ab': 20, 'ac': 20,
            'visibility': {'d20': {'result': 10, 'history': [10]}}}
        def damage(value, sequence):
            return {'kind': 'damage-observation', 'sessionId': 's', 'hasWeaponAttack': True, 'rollEventId': 'r', 'target': 't:name',
                'reportedDamage': value, 'recordSequence': sequence, 'damageTrace': {'previewId': 'p', 'previewComparisonStatus': 'InputsChanged'}}
        report = analyzer.weapon_roll_damage_review([preview, roll, damage(12, 1)])
        self.assertEqual(report['damageOutlierCandidates'], [])
        report = analyzer.weapon_roll_damage_review([preview, roll, damage(17, 1), damage(13, 2)])
        case = report['damageOutlierCandidates'][0]
        self.assertEqual(case['actualDamageSum'], 30)
        self.assertEqual(case['damageEventCount'], 2)
        self.assertEqual(case['ratioToMeanOnHit'], 3)
        self.assertEqual(case['comparisonStatuses'], ['InputsChanged'])
        self.assertEqual(case['rowAssociation'], 'AttackIndexCandidate')
        roll['criticalConfirmed'] = True
        self.assertEqual(analyzer.weapon_roll_damage_review([preview, roll, damage(100, 1)])['damageGroupsWithConditionalMean'], 0)
        preview['rows'][0]['meanOnCriticalHitAfterDefense'] = 20
        case = analyzer.weapon_roll_damage_review([preview, roll, damage(100, 1)])['damageOutlierCandidates'][0]
        self.assertEqual(case['meanBasis'], 'Critical')
        self.assertEqual(case['ratioToMeanOnHit'], 5)


    def test_actual_attack_dice_review_retains_reroll_history_and_explains_miss_gates(self):
        roll = {'kind': 'observation', 'sessionId': 's', 'eventId': 'r', 'rolled': True, 'ab': 10, 'ac': 15, 'hit': False,
                'visibility': {'d20': {'result': 7, 'history': [19, 7], 'signedRerollCount': -1}, 'concealmentPassed': False}}
        report = analyzer.weapon_roll_damage_review([roll])
        self.assertEqual(report['explainedConcealmentOrImageMisses'], 1)
        self.assertEqual(report['rollReviewCases'], [])
        roll['visibility']['concealmentPassed'] = True
        case = analyzer.weapon_roll_damage_review([roll])['rollReviewCases'][0]
        self.assertEqual(case['rollHistory'], [19, 7])
        self.assertEqual(case['status'], 'ReviewSpecialRuleOrArithmetic')

    def test_raw_coverage_reports_missing_and_partial_capture_without_validation(self):
        rows = [{'kind': 'observation', 'rawRateLimitedRecords': 2, 'rawInputs': {'version': 1,
            'armorClassRule': {'statModifiersAtTheMoment': {'entries': [], 'truncated': False}},
            'displayedInputs': {'comparisonStatus': 'InputsChanged'}}},
            {'kind': 'damage-observation', 'damageTrace': {'defenseAtCompletion': {'captureFailure': 'MissingField'}}},
            {'kind': 'spell-observation', 'details': {'frozenDisplayedInputs': {'dc': 25}}}, {'kind': 'observation'}]
        report = analyzer.raw_evidence_coverage(rows)
        self.assertEqual(report['attackRowsWithFinalRawInputs'], 1)
        self.assertEqual(report['attackRowsWithCapturedACModifiers'], 1)
        self.assertEqual(report['weaponRowsWithRetainedDisplay'], 1)
        self.assertEqual(report['spellRowsWithFrozenDisplay'], 1)
        self.assertEqual(report['maxRawDroppedCounter'], 2)
        self.assertIsNone(analyzer.raw_evidence_coverage([])['maxRawDroppedCounter'])

    def test_runtime_version_gate_is_not_accuracy_validation(self):
        old = {'runtimeMetadataConsistent': True, 'runtimeMetadata': [{'modVersion': '0.5.48.0'}]}
        self.assertFalse(analyzer.version_assessment(old, '0.5.53')['canReviewRequestedRuntime'])
        current = {'runtimeMetadataConsistent': True, 'runtimeMetadata': [{'modVersion': '0.5.53.0'}]}
        self.assertTrue(analyzer.version_assessment(current, '0.5.53')['canReviewRequestedRuntime'])
        current['runtimeMetadataConsistent'] = False
        self.assertFalse(analyzer.version_assessment(current, '0.5.53')['canReviewRequestedRuntime'])
        self.assertFalse(analyzer.version_assessment(None, '0.5.53')['canReviewRequestedRuntime'])
        with self.assertRaises(ValueError):
            analyzer.version_assessment(old, 'unknown')

    def test_shield_approximations_are_counted_separately_from_exact_and_legacy_rows(self):
        report = analyzer.summarize([
            {'kind': 'beta-preview', 'rows': [{'afterDefenseApproximate': True}, {'afterDefenseApproximate': False}, {}]},
            {'kind': 'spell-preview', 'spellMetadata': {'healthProjection': {'approximate': True}}},
            {'kind': 'spell-preview', 'spellMetadata': {'healthProjection': {'approximate': None}}}])
        self.assertEqual(report['approximateShieldWeaponRows'], 1)
        self.assertEqual(report['approximateShieldSpellPreviews'], 1)

    def test_aoe_work_timings_remain_distinct_from_frame_fps(self):
        rows = [{'kind': 'spell-preview', 'spellMetadata': {'areaScheduling': {
            'targetCount': 32, 'computeMsBeforeLogging': value, 'pendingAfterTarget': pending, 'processedThisFrame': processed}}}
            for value, pending, processed in [(1.2, 31, 1), (5.0, 30, 2), (20.0, 29, 1)]]
        rows.append({'kind': 'spell-preview', 'spellMetadata': {}})
        report = analyzer.summarize(rows)['spellAreaScheduling']
        self.assertEqual(report['samples'], 3)
        self.assertEqual(report['maxTargetComputeMs'], 20.0)
        self.assertEqual(report['targetOver4ms'], 2)
        self.assertEqual(report['targetOver16ms'], 1)
        self.assertEqual(report['maxPending'], 31)
        self.assertEqual(report['maxRecordedProcessedThisFrame'], 2)

    def test_modified_dice_are_not_mistaken_for_blueprint_base_damage(self):
        preview = {'kind': 'spell-preview', 'sessionId': 's', 'attacker': 'a', 'target': 't', 'weapon': 'spell',
                   'elapsedMs': 10, 'spellMetadata': {'blueprintId': 'bp', 'spellDamage': {'terms': [
                       {'path': 'cast', 'hpDamage': True, 'damageType': 'Fire', 'dice': 1, 'sides': 8}]}}}
        damage = {'kind': 'spell-observation', 'sessionId': 's', 'attacker': 'a', 'target': 't', 'elapsedMs': 20,
                  'details': {'checkKind': 'damage', 'castContextId': 'c', 'blueprintId': 'bp', 'abilityInstanceId': 'spell',
                              'damageTrace': {'components': [{'kind': 'Fire', 'diceBase': '1d8', 'diceModified': '3d6'}]}}}
        report = analyzer.spell_damage_candidates([preview], [damage])
        self.assertEqual(report['cases'][0]['actualDice'], (3, 6))

    def test_energy_scaling_unknowns_do_not_become_zero_percent_or_verified_matches(self):
        rows = [{'kind': 'spell-preview', 'spellMetadata': {'spellDamage': {'terms': [
            {'hpDamage': True, 'energyBonusPercent': 100, 'energyVulnerability': 1,
             'energyScalingSources': [{'component': 'EnergyDamageBonus'}, {'component': 'EnergyDamageBonus'}]},
            {'hpDamage': True, 'energyBonusPercent': None, 'energyVulnerability': 1.5,
             'targetDamageMultiplier': None, 'targetScalingUnavailableReason': 'VulnerabilityHandlerOrderUnavailable',
             'energyScalingSources': [{'component': 'AddEnergyVulnerability'}]},
            {'hpDamage': True}]}}}]
        report = analyzer.summarize(rows)['spellTargetScaling']
        self.assertEqual(report['energyBonusTerms'], 1)
        self.assertEqual(report['energyVulnerableTerms'], 1)
        self.assertEqual(report['energyHandlerOccurrences'], {'EnergyDamageBonus': 2, 'AddEnergyVulnerability': 1})
        self.assertEqual(report['unavailableReasons'], {'VulnerabilityHandlerOrderUnavailable': 1})

    def test_area_half_sources_are_distinct_from_vulnerability_and_legacy_terms(self):
        rows = [{'kind': 'spell-preview', 'spellMetadata': {'spellDamage': {'terms': [
            {'hpDamage': True, 'isAreaDamage': True, 'targetVulnerability': 1.5,
             'targetDurability': .25, 'targetDamageMultiplier': .375, 'areaHalfSources': [
                 {'component': 'HalveIncomingAreaDamage'}, {'component': 'BlinkAoEDamageResistance'}]},
            {'hpDamage': True, 'isAreaDamage': True, 'targetDurability': 1},
            {'hpDamage': True}, {'hpDamage': False, 'targetDurability': .5}]}}},
            {'kind': 'spell-preview', 'spellMetadata': {'spellDamage': None}}]
        report = analyzer.summarize(rows)['spellTargetScaling']
        self.assertEqual(report['areaDamageTerms'], 2)
        self.assertEqual(report['areaHalfTerms'], 1)
        self.assertEqual(report['handlerOccurrences'], {'HalveIncomingAreaDamage': 1, 'BlinkAoEDamageResistance': 1})

    def test_probability_review_excludes_no_damage_and_keeps_identity_boundaries(self):
        def preview(sequence, status, hp, turn='turn', damaging=True):
            return {'kind': 'spell-preview', 'sessionId': 's', 'attacker': 'a', 'target': 't', 'weapon': 'spell', 'turn': turn,
                    'recordSequence': sequence, 'elapsedMs': sequence * 100, 'spellMetadata': {
                        'parameterStatus': status, 'inputStamp': str(sequence), 'healthProjection': {'expectedHpLoss': hp},
                        'spellDamage': {'terms': [{'hpDamage': damaging}]}}}
        report = analyzer.probability_review([preview(1, 'Current', 10), preview(2, 'StateChanged', None),
            preview(3, 'Current', 11, turn='other'), preview(4, 'NotObserved', None, damaging=False)])
        self.assertEqual(report['directDamagePreviews'], 3)
        self.assertEqual(report['directDamageHpProjectionMissing'], 1)
        self.assertEqual(report['sameIdentityMissingToAvailable'], 0)
        self.assertEqual(report['sameIdentityParameterRegressions'][0], {'beforeSequence': 1, 'afterSequence': 2,
            'gapMs': 100, 'inputStampChanged': True, 'beforeHpLoss': 10, 'afterHpLoss': None})

    def test_multiple_rolls_do_not_imply_advantage_or_persistent_spell(self):
        report = analyzer.probability_review([{'kind': 'spell-observation', 'recordSequence': 8, 'details': {
            'checkKind': 'save', 'persistent': False, 'rollHistory': [20, 7], 'die': 7}},
            {'kind': 'spell-observation', 'details': {'checkKind': 'sr', 'rollHistory': [1, 20]}}])
        self.assertEqual(report['observedSaveMultiRolls'], 1)
        self.assertEqual(report['multiRollPersistentFlags'], {'False': 1})
        self.assertEqual(report['multiRollCases'][0]['keptDie'], 7)
        self.assertFalse(report['multiRollCases'][0]['rerollSourcesLogged'])

    def test_new_d20_sources_and_readiness_are_reported_without_guessing_direction(self):
        report = analyzer.probability_review([
            {'kind': 'spell-observation', 'recordSequence': 1, 'details': {'checkKind': 'save', 'rollHistory': [20, 7],
                'die': 7, 'statModifiers': {'entries': [{'source': 'Context buff', 'value': -2}]},
                'd20': {'history': [20, 7], 'result': 7, 'signedRerollCount': -1,
                    'sources': [{'source': 'Favorable Magic', 'amount': -1}]}}},
            {'kind': 'spell-preview', 'spellMetadata': {'parameterStatus': 'ReadOnlyCalculated',
                'hoverReadiness': {'firstHpProjectionReadyMs': 150}}},
            {'kind': 'spell-preview', 'spellMetadata': {'parameterFallbackReason': 'DynamicDC',
                'hoverReadiness': {'firstHpProjectionReadyMs': None}}}])
        self.assertTrue(report['multiRollCases'][0]['rerollSourcesLogged'])
        self.assertEqual(report['d20SourceKinds'], {'save': 1})
        self.assertEqual(report['d20SourceCases'][0]['signedRerollCount'], -1)
        self.assertEqual(report['maximumObservedHpReadinessMs'], 150)
        self.assertEqual(report['parameterFallbackReasons'], {'DynamicDC': 1})
        self.assertEqual(report['saveStatModifierTraces'], 1)
        self.assertEqual(report['saveStatModifierSources'], {'Context buff': 1})

    def test_spell_damage_pairs_only_recent_same_session_ability_target(self):
        preview = {'kind': 'spell-preview', 'sessionId': 's', 'attacker': 'a', 'target': 't',
                   'weapon': 'spell', 'elapsedMs': 10, 'recordSequence': 1,
                   'spellMetadata': {'blueprintId': 'bp', 'spellDamage': {'terms': [
                       {'path': 'cast', 'hpDamage': True, 'damageType': 'Fire', 'dice': 5, 'sides': 6}]}}}
        damage = {'kind': 'spell-observation', 'sessionId': 's', 'attacker': 'a', 'target': 't',
                  'elapsedMs': 20, 'recordSequence': 2,
                  'details': {'checkKind': 'damage', 'castContextId': 'c', 'blueprintId': 'bp',
                              'abilityInstanceId': 'spell', 'damageTrace': {
                                  'components': [{'kind': 'Fire', 'diceBase': '6d6'}]}}}
        result = analyzer.summarize([preview, damage, {**damage, 'sessionId': 'other'},
                                     {**damage, 'elapsedMs': 2000}])['spellDamageCandidates']
        self.assertEqual(result['statuses'], {'CandidateDiceDifference': 1, 'NoRecentPreview': 2})
        self.assertEqual(result['cases'][0]['previewDice'], [5, 6])
        timed_preview = {**preview, 'turn': 'turn'}
        timed_damage = {**damage, 'elapsedMs': 2000, 'turn': 'turn'}
        extended = analyzer.summarize([timed_preview, timed_damage, {**timed_damage, 'turn': 'other'},
                                      {**timed_damage, 'elapsedMs': 4011}])['spellDamageCandidates']
        self.assertEqual(extended['statuses'], {'CandidateDiceDifference': 1, 'NoRecentPreview': 2})
        self.assertTrue(extended['cases'][0]['sameTurn'])


    def test_reduction_difference_is_counted_without_expected_damage_comparison(self):
        base = {'schema': 2, 'sessionId': 's', 'recordSequence': 1}
        rows = [{**base, 'kind': 'damage-observation', 'eventId': 'd', 'rollEventId': 'r', 'reportedDamage': 8,
                 'damageTrace': {'previewUnmodeledAdjustments': ['componentHalf'], 'components': [
                     {'rollResult': 1, 'reconstructionMatches': True, 'previewStaticDefenseMatches': False},
                     {'rollResult': 20, 'reconstructionMatches': True, 'previewStaticDefenseMatches': True},
                     {'previewStaticDefenseMatches': None, 'replayUnavailableReason': 'unverified grouping'}]}}]
        report = analyzer.summarize(rows)
        self.assertEqual(report['staticDefenseReplayDifferences'], 1)
        self.assertEqual(report['staticDefenseReplayComparedComponents'], 2)
        self.assertEqual(report['componentArithmeticDifferences'], 0)
        self.assertEqual(report['previewUnmodeledAdjustments'], {'componentHalf': 1})
        self.assertEqual(report['staticDefenseReplayUnavailable'], {'unverified grouping': 1})

    def test_spell_checks_and_invalid_state_deltas_stay_separate(self):
        rows = [
            {'kind': 'spell-preview', 'spellMetadata': {'parameterStatus': 'Expired'}, 'checkDetails': [
                {'kind': 'attack', 'status': 'Estimated', 'probability': .7},
                {'kind': 'save', 'status': 'Unavailable', 'probability': None, 'reason': 'DC expired'}]},
            {'kind': 'spell-observation', 'details': {'checkKind': 'save', 'sourceResolved': False}},
            {'kind': 'beta-comparison', 'status': 'InputsChanged', 'abDelta': 2, 'acDelta': 0}]
        report = analyzer.summarize(rows)
        self.assertEqual(report['spellCheckStatuses'], {'attack:Estimated': 1, 'save:Unavailable': 1})
        self.assertEqual(report['spellUnavailableReasons'], {'save:DC expired': 1})
        self.assertEqual(report['spellParameterStatuses'], {'Expired': 1})
        self.assertEqual(report['spellObservedChecks'], {'save': 1})
        self.assertEqual(report['spellUnresolvedSources'], 1)
        self.assertEqual(report['weaponNumericDeltasByStatus'], {'InputsChanged': 1})
        self.assertEqual(report['betaModelDifferences'], 0)

    def test_reference_differences_require_comparable_status(self):
        differences = [{'kind': 'attack', 'bonusDelta': 2, 'thresholdDelta': 0, 'rawRollProbabilityDelta': .1}]
        report = analyzer.summarize([
            {'kind': 'beta-preview', 'hcoReference': {'status': 'StateChanged', 'differences': differences}},
            {'kind': 'spell-preview', 'spellMetadata': {'hcoReference': {'status': 'ComparablePartialModel', 'differences': differences}}}])
        self.assertEqual(report['hcoReferenceStatuses'], {'StateChanged': 1, 'ComparablePartialModel': 1})
        self.assertEqual(report['hcoRawDifferences'], {'attack:bonusDelta': 1, 'attack:rawRollProbabilityDelta': 1})

    def test_legacy_damage_records_remain_readable(self):
        report = analyzer.summarize([{'schema': 1, 'kind': 'damage-observation', 'eventId': 'd'}])
        self.assertEqual(report['damageTraceRecords'], 0)
        self.assertEqual(report['staticDefenseReplayComparedComponents'], 0)

    def test_rotation_is_read_before_current_and_sessions_stay_separate(self):
        with tempfile.TemporaryDirectory() as folder:
            current = Path(folder) / 'CombatPreview.diagnostics.jsonl'
            old = {'schema': 2, 'sessionId': 'old', 'recordSequence': 1, 'kind': 'observation', 'eventId': '1'}
            new = {'schema': 2, 'sessionId': 'new', 'recordSequence': 1, 'kind': 'damage-observation', 'eventId': '2', 'rollEventId': '1'}
            current.write_text(json.dumps(new), encoding='utf-8')
            Path(str(current) + '.prev').write_text(json.dumps(old), encoding='utf-8')
            rows = analyzer.read_rows(current)
            self.assertEqual([r['sessionId'] for r in rows], ['old', 'new'])
            self.assertEqual(analyzer.summarize(rows)['damageWithoutRoll'], 1)
            archive_path = Path(folder) / 'logs.zip'
            with zipfile.ZipFile(archive_path, 'w') as archive:
                archive.write(current, current.name)
                archive.write(Path(str(current) + '.prev'), current.name + '.prev')
            self.assertEqual(analyzer.read_rows(archive_path), rows)

    def test_health_projection_bounds_and_unknowns_are_not_success(self):
        rows = [
            {'kind': 'beta-preview', 'healthProjection': {'currentHp': 10, 'maximumHp': 20, 'temporaryHp': 5,
                'expectedHpLoss': 2, 'expectedTemporaryHpLoss': 5, 'approximate': True, 'status': 'Computed'}},
            {'kind': 'spell-preview', 'spellMetadata': {'healthProjection': {'currentHp': 10, 'maximumHp': 20, 'temporaryHp': 5,
                'expectedHpLoss': None, 'expectedTemporaryHpLoss': None}}},
            {'kind': 'beta-preview', 'healthProjection': {'currentHp': 10, 'maximumHp': 20, 'temporaryHp': 5,
                'expectedHpLoss': 11, 'expectedTemporaryHpLoss': 6, 'status': 'Computed'}},
        ]
        review = analyzer.health_projection_review(rows)
        self.assertEqual(review['counts']['invalidProjectionRecords'], 1)
        self.assertEqual(len(review['issues'][0]['issues']), 2)
        self.assertEqual(review['counts']['mainLossUnavailable'], 1)
        self.assertEqual(review['counts']['approximate'], 1)
        self.assertEqual(review['counts']['withTemporaryHp'], 3)

    def test_retained_health_and_final_state_are_not_absorption_measurement(self):
        projection = {'currentHp': 20, 'maximumHp': 20, 'temporaryHp': 10, 'expectedHpLoss': 0, 'expectedTemporaryHpLoss': 4}
        rows = [{'kind': 'observation', 'rawInputs': {'displayedInputs': {'healthProjection': projection}}},
                {'kind': 'spell-observation', 'details': {'frozenDisplayedInputs': {'healthProjection': projection},
                 'damageTrace': {'healthAtCompletion': {'currentHp': 20, 'maximumHp': 20, 'temporaryHp': 6}}}}]
        counts = analyzer.health_projection_review(rows)['counts']
        self.assertEqual(counts['retainedWeaponDisplay'], 1)
        self.assertEqual(counts['retainedSpellDisplay'], 1)
        self.assertEqual(counts['completedWithTemporaryHp'], 1)
        self.assertNotIn('actualAbsorbedTemporaryHp', counts)

    def test_performance_windows_preserve_slow_scope_and_do_not_sum_nested_ms(self):
        rows = [{'kind': 'performance-window', 'performance': {'frames': 100, 'over33ms': 2, 'over100ms': 1,
            'stages': [{'stage': 'parent', 'calls': 2, 'maximumMs': 12, 'maximumFrame': 77, 'gc0AtMaximum': 1},
                       {'stage': 'child', 'calls': 2, 'maximumMs': 10}]}},
            {'kind': 'performance-window', 'performance': {'frames': 50,
            'stages': [{'stage': 'parent', 'calls': 3, 'maximumMs': 4}], 'settings': {'manualScenario': True}}}]
        report = analyzer.performance_review(rows)
        self.assertEqual(report['windows'], 2)
        self.assertEqual(report['frames'], 150)
        self.assertEqual(report['stagesByMaximumMs'][0]['maximumMs'], 12)
        self.assertEqual(report['stagesByMaximumMs'][0]['calls'], 5)
        self.assertEqual(report['stagesByMaximumMs'][0]['maximumFrame'], 77)
        self.assertEqual(report['manualScenarioWindows'], 1)
        self.assertEqual(analyzer.performance_review([])['windows'], 0)

    def test_persistent_hco_difference_is_not_game_truth(self):
        rows = [{'kind': 'spell-preview', 'spellMetadata': {'hcoReference': {'differences': [
            {'kind': 'save', 'localPersistentSave': True, 'probabilityEquivalenceVerified': False,
             'localD20Model': {'afterRollDirections': [-1]}, 'rawRollProbabilityDelta': .1875}]}}}]
        review = analyzer.independent_reference_review(rows)
        self.assertEqual(review['authority'], 'ReferenceOnly')
        self.assertEqual(review['counts']['localPersistentSavePairs'], 1)
        self.assertEqual(review['counts']['localAdvancedRollPairs'], 1)
        self.assertEqual(review['counts']['probabilityEquivalenceNotVerified'], 1)
        self.assertNotIn('confirmedModelErrors', review)

    def test_exact_row_association_still_rejects_changed_conditions(self):
        rows = [{'kind': 'beta-comparison', 'status': 'InputsChanged', 'displayedInputs': {
            'attackLink': {'exactRuleIdentity': True, 'status': 'ExactCommandRuleAndWeapon', 'weaponOrdinal': 0},
            'inputAtAttackBegin': {'ac': 24}}}, {'kind': 'beta-comparison', 'status': 'UnmodeledAttack'}]
        review = analyzer.attack_association_review(rows)
        self.assertEqual(review['counts']['exactRowIdentity'], 1)
        self.assertEqual(review['counts']['exactRowButInputsChanged'], 1)
        self.assertEqual(review['counts']['frozenBeginInputs'], 1)
        self.assertEqual(review['linkStatuses']['LegacyOrLinkUnavailable'], 1)
        self.assertNotIn('confirmedModelErrors', review)

    def test_retained_value_difference_does_not_override_changed_conditions(self):
        rows = [{'kind': 'observation', 'recordSequence': 1, 'rawInputs': {'finalAB': 32, 'finalAC': 24,
            'displayedInputs': {'ab': 25, 'ac': 24, 'comparisonStatus': 'InputsChanged', 'rowAssociation': 'PreviousDisplayedRowOnly'}}},
            {'kind': 'observation', 'rawInputs': {'finalAB': 25, 'finalAC': 24, 'displayedInputs': {'ab': 25, 'ac': 24}}}]
        review = analyzer.retained_input_review(rows)
        self.assertEqual(review['counts']['abAcNumericallyDifferent'], 1)
        self.assertEqual(review['counts']['abAcNumericallyEqual'], 1)
        self.assertEqual(review['examples'][0]['actualAb'], 32)
        self.assertEqual(review['examples'][0]['verdict'], 'InputDifferenceOnlyNotConfirmedModelError')


if __name__ == '__main__':
    unittest.main()
