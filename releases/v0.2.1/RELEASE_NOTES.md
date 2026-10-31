# v0.2.1 — 진단용 개발 빌드

이 릴리스는 전투 미리보기 완성 제품이 아닙니다. 기존 DryRun의 게임/모드 상태 및 RNG 비간섭이 입증되지 않아 hover 예측은 차단되어 있습니다.

- 기본 OFF인 로컬 공격 굴림/피해 이벤트 관측과 제한된 JSONL 진단을 추가했습니다.
- 방어 스냅샷 계약, 정확도/누락 상태, 관통/DR fixture 테스트를 추가했습니다. 자동 유효 방어 판독과 실시간 예측 비교는 미완료입니다.
- 실제 사용자 제공 게임/Unity/Harmony/UMM 참조로 net472 전체 빌드 성공. C# 6,808 assertions 및 독립 Python 검사 509개 통과.
- 실제 게임의 설치/이벤트 관측/상태/RNG 보존 검증은 아직 하지 않았습니다.

설치 파일: CombatPreview-0.2.1-diagnostic-only-UMM.zip
개발 소스: CombatPreview-0.2.1-development-source.zip
체크섬: SHA256SUMS.txt

ZIP에는 게임·Unity·Harmony·UMM DLL이 포함되지 않습니다. 진단용 UMM ZIP에는 CombatPreview.dll과 설정/라이선스/안내서만 포함됩니다. ModFinder 목록의 자동 업데이트 대상 등록은 별도 매니페스트 등록이 필요합니다.
