# WotR Combat Preview — 0.3.3 읽기 전용 베타

턴제 전투에서 공격 가능한 적을 가리키면 **기본 주무기 모델의 부분 추정**을 표시하고,
표시한 모델과 실제 AB·AC·무기 구성의 차이를 로컬 로그에 기록합니다. 화면 차이 알림은 선택 사항입니다.
패널은 좌측에 낮은 표로 배치하며 대상 체력과 공격별 명중률·줄바꿈 피해 주사위·명중 시 평균·기댓값·합계를 표시합니다. 직접 만든 짙은 배경과 청동색 테두리를 사용하고 상세 설명은 접습니다.
추가 게임 규칙 이벤트나 주사위를 실행해 결과를 얻지 않습니다.

## 다운로드와 사용

- [0.3.3 베타 UMM ZIP](releases/v0.3.3/CombatPreview-0.3.3-beta-UMM.zip)
- [개발 소스 ZIP](releases/v0.3.3/CombatPreview-0.3.3-development-source.zip)
- [설치·지원 범위·첫 시험 안내](CombatPreview/README.ko.md)
- [변경 설명](releases/v0.3.3/RELEASE_NOTES.md)
- [체크섬](releases/v0.3.3/SHA256SUMS.txt)

ZIP 파일 페이지에서 **Download raw file**을 선택합니다. 비공개 저장소라면 권한 있는 GitHub 계정으로 로그인해야 합니다.
게임 종료 후 ModFinder/UMM으로 설치하고 Ctrl+F10 → Combat Preview에서 **베타 무기 미리보기**를 켭니다.
일반 장검 등 제조 무기를 주무기로 선택해 현재 전투에서 패널 표시와 비교 기록부터 시험할 수 있습니다.

## 이번 변경

- 현재 양손 잡기를 반영합니다. 힘+4의 양손 장검은1D8+6으로 계산합니다.
- 선궁의 질풍격 등 확인된 추가 공격 효과와 이미 계산된 공격 수를 읽어 최고 보정의 두 공격을 표시합니다.
- 좌측 압축 표에서 공격자/무기를 각 공격 행에 합칩니다. 최대 높이420이며 넘는 내용은 스크롤합니다.
- **F6 상세 / F7 대상 고정**과 기수/탈것별 추적을 유지합니다. 보관 화면을 새 예측으로 기록하지 않습니다.

게임/다른 모드가 이미 계산한 AB/AC/공격 수를 세 개의 제한된 postfix로 관측합니다. 추가 규칙·공격·주사위를
실행하지 않습니다. 지원한 활성 효과가 없고 관측 결과도 없으면 기본 추정입니다. 모든 조건부 추가 타격,
치명/은폐/재굴림/난이도 효과와 DR·저항·보호막 적용 후 피해는 아직 완전하게 계산하지 않습니다.
전체 상태/RNG 동일 및 정확도 통과를 주장하지 않습니다. fullConditionsVerified는 계속 false입니다.

사용자0.3.2 증거에서 관측 설치/AB·AC 콜백과10개 모델의 관측 결과 사용을 확인했습니다.
새 양손/추가 공격 판독과 압축 UI는 게임에서 재확인이 필요합니다. 제공된2.7.0x MVID만 허용하고 TTT는 차단합니다.
기존 DryRun/event replay/억제 패치는 계속 차단합니다. 현재 JSONL과.prev를 함께 보내면 회전 전후를 확인할 수 있습니다.

## 기록과 배포

[검증 기록](CombatPreview/docs/VALIDATION.md), [안전성 감사](CombatPreview/docs/SAFETY_AUDIT.md),
[로그 분석](CombatPreview/docs/RUNTIME_LOG_REVIEW.ko.md)을 확인하세요. 원시 사용자 게임 로그와 캐릭터명은 저장소에 넣지 않습니다.
0.2.x 진단용 ZIP은 [이전 버전 폴더](releases)에 보존합니다.

GitHub Release 생성은 `bash tools/publish_release.sh`이며 실제 Release와 ModFinder 중앙 매니페스트 등록은 별도입니다.
확인한 ModFinder 1.5.3의 기본 다운로드 클라이언트에는 GitHub 인증 경로가 없어 비공개 저장소 자동 업데이트는 어렵습니다.
게임·Unity·Harmony·UMM 참조 DLL은 배포하지 않습니다. MIT 라이선스/원본 고지는 [LICENSE](CombatPreview/LICENSE)와
[THIRD_PARTY](CombatPreview/THIRD_PARTY.md)를 따릅니다.
