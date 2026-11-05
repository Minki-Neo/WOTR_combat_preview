# WotR Combat Preview — 0.5.0 읽기 전용 베타

턴제 전투에서 공격 가능한 적을 가리키면 **기본 주무기 모델의 부분 추정**을 표시하고,
표시한 모델과 실제 AB·AC·무기 구성의 차이를 로컬 로그에 기록합니다. 화면 차이 알림은 선택 사항입니다.
패널은 좌측에 낮은 표로 배치하며 대상 체력과 공격별 명중률·줄바꿈 피해 주사위·명중 시 평균·기댓값·합계를 표시합니다. 직접 만든 짙은 배경과 청동색 테두리를 사용하고 상세 설명은 접습니다.
추가 게임 규칙 이벤트나 주사위를 실행해 결과를 얻지 않습니다.

## 다운로드와 사용

- [0.5.0 베타 UMM ZIP](releases/v0.5.0/CombatPreview-0.5.0-beta-UMM.zip)
- [개발 소스 ZIP](releases/v0.5.0/CombatPreview-0.5.0-development-source.zip)
- [설치·지원 범위·첫 시험 안내](CombatPreview/README.ko.md)
- [변경 설명](releases/v0.5.0/RELEASE_NOTES.md)
- [체크섬](releases/v0.5.0/SHA256SUMS.txt)

ZIP 파일 페이지에서 **Download raw file**을 선택합니다. 비공개 저장소라면 권한 있는 GitHub 계정으로 로그인해야 합니다.
게임 종료 후 ModFinder/UMM으로 설치하고 Ctrl+F10 → Combat Preview에서 **베타 무기 미리보기**를 켭니다.
일반 장검 등 제조 무기를 주무기로 선택해 현재 전투에서 패널 표시와 비교 기록부터 시험할 수 있습니다.

## 이번 변경

- 주문 선택 중 접촉 명중률과 인내·반사·의지 내성 실패 확률, 기본 주문 저항 관통률을 구분합니다.
- 범위 주문은 게임이 조준 중 판정한 적·아군을 대상별로 표시합니다. 범위 판정이 없으면 커서 대상만 표시합니다.
- 물리/전체 피해 면역 누락과 무기 통계 캐시 갱신 문제를 수정하고 비교 제외 사유를 기록합니다.
- 기존 무기·기수/탈것·질풍격 미리보기와 좌측 패널을 유지합니다.

내성 실패는 디버프 최종 적용률이 아닙니다. 조건부 DC/내성·면역·특수 재굴림 등의 제한은
[상세 안내](CombatPreview/README.ko.md)와 [변경 설명](releases/v0.5.0/RELEASE_NOTES.md)에 기록했습니다.
게임이 이미 계산한 결과를 읽고, 추가 규칙·공격·주사위를 실행하지 않습니다.
0.5.0의 실제 게임 UI/주문 적용은 아직 재확인이 필요합니다. 제공된2.7.0x MVID만 허용하며 TTT는 차단합니다.

## 기록과 배포

[검증 기록](CombatPreview/docs/VALIDATION.md), [안전성 감사](CombatPreview/docs/SAFETY_AUDIT.md),
[로그 분석](CombatPreview/docs/RUNTIME_LOG_REVIEW.ko.md)을 확인하세요. 원시 사용자 게임 로그와 캐릭터명은 저장소에 넣지 않습니다.
0.2.x 진단용 ZIP은 [이전 버전 폴더](releases)에 보존합니다.

GitHub Release 생성은 `bash tools/publish_release.sh`이며 실제 Release와 ModFinder 중앙 매니페스트 등록은 별도입니다.
확인한 ModFinder 1.5.3의 기본 다운로드 클라이언트에는 GitHub 인증 경로가 없어 비공개 저장소 자동 업데이트는 어렵습니다.
게임·Unity·Harmony·UMM 참조 DLL은 배포하지 않습니다. MIT 라이선스/원본 고지는 [LICENSE](CombatPreview/LICENSE)와
[THIRD_PARTY](CombatPreview/THIRD_PARTY.md)를 따릅니다.
