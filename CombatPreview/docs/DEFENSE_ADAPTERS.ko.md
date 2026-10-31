# 방어 어댑터와 정확도 계약 — 0.2.2

`DefenseContracts.cs`는 게임 객체 없는 순수 값 계약입니다. `DefenseObservation` 입력을 두 제공자
`VanillaDefenseSnapshotProvider` / `TabletopTweaksDefenseSnapshotProvider`가 투영합니다.
이는 실제 대상의 완전한 방어 판독기를 구현했다는 뜻이 아닙니다.

- `DefenseSnapshot`: 출처(Target/Fixture/Manual 구분), 게임 빌드, 대상, 적용 범위, 규칙, 누락 이유,
  관측 세부 정보. 컬렉션과 값은 복사되며 `ToProfile()`도 새 객체를 반환합니다.
- `DamageComponentSnapshot`: 피해 종류, 주사위/보너스, 치명타 적용 여부, 관통 특성의 복사본.
- 관통 표현식: AND/OR 및 명시적 특성(material/alignment/form). `enhancement:N`은 수치 임계값입니다.
  예를 들어 5는 3 요구를 충족합니다. 게임의 강화→재질/성향 관통 변환은 별도의 라이브 판독기가
  확정해야 합니다. 그 변환을 이 문자열 모델이 추정하지 않습니다.
- 감소량은 같은 StackGroup에서 최대값, 서로 다른 명시적 그룹에서 합계입니다. 그룹은 실제 규칙이
  확인된 판독기만 부여해야 합니다. 실제 기본 게임/TTT의 모든 중첩에 일반화하지 않습니다.
- 같은 유형 안에서 관통 특성이 다르면 계산 불가입니다. 소모형 풀, 억제 상태, 추가 수정자,
  미확정 그룹/최소피해 규칙, 타입/관통 미상, 불완전 판독도 숫자를 제공하지 않습니다.
- `Complete`, `TraitsComplete`, `GroupingVerified`는 기본 false입니다. 이 플래그들과 규칙 활성화 근거,
  게임/대상/범위가 모두 필요합니다. TTT 어셈블리 존재는 활성화 근거가 아닙니다. 제공자 간 fallback은 없습니다.
- `SupportedModel`은 지정한 정적 모델 범위의 계산 가능 상태입니다. Fixture 출처는 게임 지원 판정이 아닙니다.

`VanillaDefenseCapture`는 제공된 실제 게임 DLL에서 확인한 private 필드 구조를 사용하는 **초안**입니다.
이미 존재하고 초기화된 part의 캐시 감소량·적용량·잔여 풀 필드·등록 면역 종류를 읽습니다.
초기화되지 않은 경우 그대로 불가로 반환합니다. `AllSources` getter는 초기화를 유발하므로 사용하지 않습니다.
캐시 값의 최신성·감소 종류·관통·우선순위·억제·난이도/추가 핸들러가 확정되지 않아 항상 불완전합니다.
이 관측은 진단 증거에만 사용하며 DR=0이나 자동 적용 피해로 바꾸지 않습니다. 풀 잔량 0을
“풀 없음”으로 간주하지 않습니다. 모든 런타임 계측은 미실행입니다.

사용자는 TTT 미사용이라고 확인했습니다. TTT용은 순수 계약 fixture만 있으며 live 판독/설정 활성화
판정은 미구현입니다. TTT DLL이나 코드가 자동으로 섞이지 않습니다.

`PredictionReport`는 SupportedModel / PartialEstimate / Unavailable과 누락 이유를 전달합니다.
RAW, TARGET, MANUAL 출처를 섞지 않습니다. 행이 하나라도 없거나 계산 불가이거나 출처/범위가
다르면 전체 합계를 내지 않습니다. 부분 추정의 합계는 부분 추정으로 남습니다. 수동 시나리오의
다른 유형 저항 0 가정은 명시된 수동 모델에서만 허용됩니다. 확인 체크는 세션마다 초기화됩니다.
현재 패널은 차단되어 있으므로 보고서/UI 소스가 컴파일됐다는 사실과 게임 화면 검증을 구분해야 합니다.

0.2.2에서는 런타임의 SourceBlueprintComponent backing field에서 정적 UsePool 필드를 읽습니다.
기본 게임의 m_RemainPool은 UsePool=false인 경우도 초기화되므로 양수만으로 보호막으로 분류하지 않습니다.
플래그 미상은 Unknown, UsePool=true/잔량0은 고갈된 Present입니다. 어떤 값도 live SpendPool/가상 계산 함수를 호출해 얻지 않습니다.
