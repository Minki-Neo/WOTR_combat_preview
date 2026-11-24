# 0.5.68 한 번의 플레이 로그 검증

설치 ZIP의 DLL/Info.json은 0.5.68이며 실행 중 로그 runtime.modVersion은 0.5.68.0이어야 한다. 모드 상세 설정에서 CompareBeta(베타 비교 기록)가 켜져 있으면 자동 수집된다. LocalDiagnostics만 켜고 CompareBeta를 끄면 실제 판정/성능 위주이며 표시 비교 자료가 부족할 수 있다.

추가 확인: 보조 무기/추가 타격의 attackLink, frozen inputAtAttackBegin, 자동 메타매직 조건별 확률과 미확인 사유를 확인한다. 행 연결이 정확해도 InputsChanged를 계산 오류로 확정하지 않는다. HCO와의 차이는 실제 최종 입력과 독립 주사위 모델로 판단한다.

1. 게임 종료 후 ZIP으로 교체하고 실행한다. UMM의 DLL 버전과 로그의 runtime.modVersion/modAssemblyMvid로 새 빌드임을 확인한다.
2. 임시 HP가 있는 대상에게 첫 호버 후 공격한다. 가능하면 DR/저항이 있는 대상 및 다중 공격도 포함한다. 파란 임시 HP와 노란 소모 구간을 확인한다.
3. 피해 주문 하나와 환영살인마처럼 분기가 있는 주문 하나를 사용한다. 최초 호버와 재호버, 범위기에서 아군 제외/계산 지연도 관찰한다. 전체 공격열의 기대 감소와 한 타격 결과를 같은 값으로 판단하지 않는다.
4. 15초 이상 플레이하거나 UMM에서 모드를 꺼 짧은 성능 구간을 저장한다. 강제 종료 대신 정상적으로 종료한다.
5. `CombatPreview.diagnostics.jsonl`과 `.prev`를 보존한다. JSONL에 성능이 포함되므로 보통 이 두 파일로 분석 가능하다. 오류가 있으면 CombatPreview.log와 UMM 로그도 함께 보존한다.

분석 명령: `python tools/analyze_diagnostics.py <CombatPreview.diagnostics.jsonl> --expected-version 0.5.68.0` (CombatPreview 폴더 기준). 현재 파일의 .prev를 자동으로 읽고 세션을 분리한다. 이전 버전 로그는 새 기능 검증 자료로 취급하지 않는다.

보고서의 versionAssessment, rawEvidenceCoverage, healthProjectionReview, performanceReview, weaponRollDamageReview 및 주문 분석 항목을 함께 확인한다. 기록이 없으면 미측정이며, 값이 범위 안에 있다는 것만으로 정확성을 인증하지 않는다. 여러 로그 파일의 보존 구간에 같은 사건이 중복될 수 있다. FPS 전체 수치는 다른 모드/엔진도 포함하며 중첩 단계 시간은 합산하지 않는다.

이번 검증에는 음수 레벨을 가진 시전자, 자연무기의 마법 강화/DR 관통, 아루샬레의 주적 대상, 지속 메타매직 로드 환영 살인마를 우선 포함한다. HCO와의 차이 자체는 오류 판정이 아니다.
