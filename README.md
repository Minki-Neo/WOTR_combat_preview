# WotR Combat Preview — 0.3.2 읽기 전용 베타

턴제 전투에서 공격 가능한 적을 가리키면 **기본 주무기 모델의 부분 추정**을 표시하고,
표시한 모델과 실제 AB·AC·무기 구성의 차이를 로컬 로그에 기록합니다. 화면 차이 알림은 선택 사항입니다.
패널은 행동 막대 위 중앙에 배치하며 대상 체력과 공격별 명중률·줄바꿈 피해 주사위·명중 시 평균·기댓값·합계를 표시합니다. 직접 만든 짙은 배경과 청동색 테두리를 사용하고 상세 설명은 접습니다.
추가 게임 규칙 이벤트나 주사위를 실행해 결과를 얻지 않습니다.

## 다운로드와 사용

- [0.3.2 베타 UMM ZIP](releases/v0.3.2/CombatPreview-0.3.2-beta-UMM.zip)
- [개발 소스 ZIP](releases/v0.3.2/CombatPreview-0.3.2-development-source.zip)
- [설치·지원 범위·첫 시험 안내](CombatPreview/README.ko.md)
- [변경 설명](releases/v0.3.2/RELEASE_NOTES.md)
- [체크섬](releases/v0.3.2/SHA256SUMS.txt)

ZIP 파일 페이지에서 **Download raw file**을 선택합니다. 비공개 저장소라면 권한 있는 GitHub 계정으로 로그인해야 합니다.
게임 종료 후 ModFinder/UMM으로 설치하고 Ctrl+F10 → Combat Preview에서 **베타 무기 미리보기**를 켭니다.
일반 장검 등 제조 무기를 주무기로 선택해 현재 전투에서 패널 표시와 비교 기록부터 시험할 수 있습니다.

## 이번 변경

- 연결된 기수와 탈것을 구분하고 이미 있는 자연 무기 슬롯을 각각 표시합니다. 전체 실행 횟수/순서는 미확정입니다.
- **F6 상세 / F7 대상 고정**. 패널로 마우스를 옮겨도 조작할 수 있으며 보관 화면은 최신 예측으로 기록하지 않습니다.
- 상세에 관측 AC·명중 보정 합계·필요한 d20 눈금을 표시합니다. 관측 결과가 없으면 기본 추정으로 구분합니다.
- 기존 계산의 완료된 AB/AC Result를 제한된 postfix로 읽습니다. 추가 규칙/공격/주사위를 실행하지 않습니다.

기본 BAB 반복/슬롯별 자연 공격 모델입니다. 추가 공격·제조 보조손·치명/조건부 효과·모든 명중 확률 수정은
완전하게 모델링하지 않습니다. DR·저항·면역·보호막 적용 후 피해는 미지원이며 모르는 값을 0으로 가정하지 않습니다.
최근 관측 키도 완전한 상태 revision이 아니며 모든 비교는 fullConditionsVerified=false입니다.

0.3.1 사용자 로그에서는 모든 표시가 기본 추정이었고 비교 가능 5건 모두 AB 차이가 남았습니다.
**0.3.2의 기승/완료 결과 관측/입력 유지 경로는 실제 게임 검증 전입니다.** 제공된 게임2.7.0x MVID만
허용하고 TTT는 차단합니다. 기존 DryRun/event replay 및 억제 패치는 계속 차단합니다.

## 기록과 배포

[검증 기록](CombatPreview/docs/VALIDATION.md), [안전성 감사](CombatPreview/docs/SAFETY_AUDIT.md),
[로그 분석](CombatPreview/docs/RUNTIME_LOG_REVIEW.ko.md)을 확인하세요. 원시 사용자 게임 로그와 캐릭터명은 저장소에 넣지 않습니다.
0.2.x 진단용 ZIP은 [이전 버전 폴더](releases)에 보존합니다.

GitHub Release 생성은 `bash tools/publish_release.sh`이며 실제 Release와 ModFinder 중앙 매니페스트 등록은 별도입니다.
확인한 ModFinder 1.5.3의 기본 다운로드 클라이언트에는 GitHub 인증 경로가 없어 비공개 저장소 자동 업데이트는 어렵습니다.
게임·Unity·Harmony·UMM 참조 DLL은 배포하지 않습니다. MIT 라이선스/원본 고지는 [LICENSE](CombatPreview/LICENSE)와
[THIRD_PARTY](CombatPreview/THIRD_PARTY.md)를 따릅니다.
