# WotR Combat Preview — 0.5.68 개발 베타

턴제 전투에서 무기 공격과 주문의 명중·내성·SR 판정, 피해식과 기대 피해를 표시합니다. 복합 주문은 분기별 확률과 효과를 표로 보여주며, 모드 패널의 체력바는 본체·임시 HP와 예상 소모를 구분합니다. 추가 공격이나 주사위를 실행하지 않고 저장된 게임 상태와 독립 계산 모델을 사용합니다.

현재 저장소에는 0.5.68 소스와 버전별 개발·검증 기록이 반영되어 있습니다. GitHub Release와 ModFinder 중앙 등록은 별도이며, 이 소스 갱신만으로 설치 파일이 자동 업데이트되지는 않습니다.

## 최신 개발 및 검증 기록

- [설치·사용 및 버전별 변경](CombatPreview/README.ko.md)
- [구현 목록과 남은 항목](CombatPreview/docs/IMPLEMENTATION_CHECKLIST.ko.md)
- [로그로 확인한 개발 항목](CombatPreview/docs/OPEN_ITEMS.ko.md)
- [검증 기록](CombatPreview/docs/VALIDATION.md)
- [한 번의 플레이로 검증하기](CombatPreview/docs/SINGLE_SESSION_REVIEW.ko.md)
- [진단 로그 구조](CombatPreview/docs/DIAGNOSTICS.ko.md)

0.5.68은 공격 명령과 실제 규칙 객체를 연결해 무기별 타격 행을 식별하고, 공격 시작 입력을 불변 로그로 보존합니다. 미확인 사유와 원인 효과를 표시하며, 자동 메타매직 공통 조건을 주문서·물약까지 독립 판독합니다. 로드 레벨 제한에는 선택 메타매직의 슬롯 비용을 포함합니다. HCO는 비교 참고값이며 계산 입력이나 정답으로 취급하지 않습니다.

빌드·수학/분기 회귀·게임 메타데이터 검사는 통과했으며, 새 버전의 실제 게임 판정·UI·FPS 검증은 적용 후 로그가 필요합니다. 특수 파생 조건과 읽을 수 없는 입력은 명시적 미확인으로 남깁니다.

## 이전 배포 파일

아래 링크는 이전 **0.5.0** 배포물이며 최신 개발 버전이 아닙니다.

- [0.5.0 베타 UMM ZIP](releases/v0.5.0/CombatPreview-0.5.0-beta-UMM.zip)
- [0.5.0 개발 소스 ZIP](releases/v0.5.0/CombatPreview-0.5.0-development-source.zip)
- [0.5.0 변경 설명](releases/v0.5.0/RELEASE_NOTES.md)
- [0.5.0 체크섬](releases/v0.5.0/SHA256SUMS.txt)

## 안전성과 라이선스

[안전성 감사](CombatPreview/docs/SAFETY_AUDIT.md)를 참고하세요. 게임·Unity·Harmony·UMM 참조 DLL, 원시 게임 로그, 생성된 캐시 파일은 새 커밋에 포함하지 않습니다. 기본 게임 체력바 연동은 제거되었고 모드 패널에서만 체력 미리보기를 제공합니다.

MIT 라이선스와 원본 고지는 [LICENSE](CombatPreview/LICENSE), [THIRD_PARTY](CombatPreview/THIRD_PARTY.md)에 있습니다.
