# WotR Combat Preview — 0.3.1 읽기 전용 베타

턴제 전투에서 공격 가능한 적을 가리키면 **기본 주무기 모델의 부분 추정**을 표시하고,
표시한 모델과 실제 AB·AC·무기 구성의 차이를 로컬 로그에 기록합니다. 화면 차이 알림은 선택 사항입니다.
패널은 행동 막대 위 중앙에 배치하며 대상 체력과 공격별 명중률·줄바꿈 피해 주사위·명중 시 평균·기댓값·합계를 표시합니다. 직접 만든 짙은 배경과 청동색 테두리를 사용하고 상세 설명은 접습니다.
추가 게임 규칙 이벤트나 주사위를 실행해 결과를 얻지 않습니다.

## 다운로드와 사용

- [0.3.1 베타 UMM ZIP](releases/v0.3.1/CombatPreview-0.3.1-beta-UMM.zip)
- [개발 소스 ZIP](releases/v0.3.1/CombatPreview-0.3.1-development-source.zip)
- [설치·지원 범위·첫 시험 안내](CombatPreview/README.ko.md)
- [변경 설명](releases/v0.3.1/RELEASE_NOTES.md)
- [체크섬](releases/v0.3.1/SHA256SUMS.txt)

ZIP 파일 페이지에서 **Download raw file**을 선택합니다. 비공개 저장소라면 권한 있는 GitHub 계정으로 로그인해야 합니다.
게임 종료 후 ModFinder/UMM으로 설치하고 Ctrl+F10 → Combat Preview에서 **베타 무기 미리보기**를 켭니다.
일반 장검 등 제조 무기를 주무기로 선택해 현재 전투에서 패널 표시와 비교 기록부터 시험할 수 있습니다.

## 베타 범위

기본 BAB 반복 모델과 저장된 일반 능력치 기반입니다. 가속/연타/보조손/자연/탈것 추가 타격, 무기 강화/특기/
능력치 치환/조건부 보정 등은 미반영입니다. 실제 전체 풀어택 수·순서가 아닙니다.
DR·저항·면역·보호막 적용 후 피해는 미지원이며, 알려지지 않은 방어를 0으로 계산하지 않습니다.
차이 기록은 미반영 효과가 원인일 수 있으며 확정 버그가 아닙니다. 실제 피해 한 번과 기대값을 비교해 경고하지 않습니다.

제공된 게임 2.7.0x MVID에 맞춘 필드 판독만 허용하며 TTT는 차단합니다. 0.3.0 패널 표시와 비교 연결은 사용자 증거로 확인했지만 AB 차이가 남습니다. **0.3.1 새 UI·게임 추정 사본 경로·전체 상태/RNG 비간섭은 아직 실제 게임 검증이 필요합니다.**
게임이 이미 만든 최근 추정값이 동일한 알려진 상태에서 관측되면 AB/AC/복수 피해 구성을 복사하고, 없으면 기본 모델을 사용합니다. 게임 추정을 생성/실행하지 않습니다. 기존 DryRun 이벤트 재실행은 계속 차단합니다.

## 기록과 배포

[검증 기록](CombatPreview/docs/VALIDATION.md), [안전성 감사](CombatPreview/docs/SAFETY_AUDIT.md),
[로그 분석](CombatPreview/docs/RUNTIME_LOG_REVIEW.ko.md)을 확인하세요. 원시 사용자 게임 로그와 캐릭터명은 저장소에 넣지 않습니다.
0.2.x 진단용 ZIP은 [이전 버전 폴더](releases)에 보존합니다.

GitHub Release 생성은 `bash tools/publish_release.sh`이며 실제 Release와 ModFinder 중앙 매니페스트 등록은 별도입니다.
확인한 ModFinder 1.5.3의 기본 다운로드 클라이언트에는 GitHub 인증 경로가 없어 비공개 저장소 자동 업데이트는 어렵습니다.
게임·Unity·Harmony·UMM 참조 DLL은 배포하지 않습니다. MIT 라이선스/원본 고지는 [LICENSE](CombatPreview/LICENSE)와
[THIRD_PARTY](CombatPreview/THIRD_PARTY.md)를 따릅니다.
