# WotR Combat Preview

**0.2.1 진단용 개발 빌드입니다. 전투 hover 미리보기는 차단되어 있습니다.**
상태/RNG 안전성이 검증되지 않은 기존 이벤트 재실행 경로를 막고, 기본 OFF인 로컬 공격·피해 관측을 추가했습니다.
자동 유효 방어 판독과 실시간 예측 비교는 아직 완료되지 않았습니다.

## 다운로드

- [진단용 UMM 설치 ZIP](releases/v0.2.1/CombatPreview-0.2.1-diagnostic-only-UMM.zip)
- [개발 소스 ZIP](releases/v0.2.1/CombatPreview-0.2.1-development-source.zip)
- [SHA-256 체크섬](releases/v0.2.1/SHA256SUMS.txt)
- [v0.2.1 설명](releases/v0.2.1/RELEASE_NOTES.md)

ZIP 파일 페이지에서 **Download raw file**을 선택합니다. 저장소가 비공개이면 저장소 접근 권한이 있는
GitHub 계정으로 로그인해야 합니다. 게임/Unity/Harmony/UMM 참조 DLL은 포함하지 않습니다.

다운로드한 설치 ZIP을 ModFinder의 로컬 ZIP 설치 또는 UMM으로 설치합니다. 실제 게임에서 로드/진단 동작은 아직 검증하지 않았습니다.
설치 후 Ctrl+F10 → Combat Preview에서 로컬 진단을 선택적으로 켭니다. 이 빌드에서는 hover 패널이 나오지 않습니다.

## ModFinder 업데이트와 비공개 저장소

제공된 ModFinder 1.5.3을 정적으로 확인한 결과, 다운로드용 HTTP 클라이언트에 GitHub 인증을 설정하는 경로가 없습니다.
따라서 비공개 GitHub ZIP/Release를 일반 ModFinder 자동 업데이트 대상으로 사용하기는 어렵습니다.
GitHub 브라우저 로그인 상태가 ModFinder의 HTTP 요청으로 전달되지는 않습니다.

현재 비공개 저장소는 소스 보관과 수동 다운로드에 사용할 수 있습니다. 공개 배포를 원하면 별도 공개 배포 저장소와
ModFinder 매니페스트 등록을 검토할 수 있습니다. 기존 저장소의 공개 여부를 바꾸지 않습니다.
GitHub Release를 생성해도 ModFinder의 중앙 목록에 자동 등록되지는 않습니다.

## 개발과 검증

프로젝트는 [CombatPreview](CombatPreview)에 있습니다. [설치/빌드 안내](CombatPreview/README.ko.md),
[검증 기록](CombatPreview/docs/VALIDATION.md), [안전성 감사](CombatPreview/docs/SAFETY_AUDIT.md)를 확인하세요.
실제 제공된 게임 DLL을 사용한 전체 net472 빌드, C# 6,808 assertions와 독립 Python 검사 509개가 통과했습니다.
게임 실행·정확도·상태/RNG 비간섭 검증은 별도입니다.

GitHub Release API 접근이 가능한 환경에서는 루트에서 `bash tools/publish_release.sh`로
태그가 이미 게시된 v0.2.1의 진단용 prerelease와 파일을 생성할 수 있습니다.
이 스크립트는 기존 릴리스를 덮어쓰거나 저장소 공개 설정을 바꾸지 않습니다.
