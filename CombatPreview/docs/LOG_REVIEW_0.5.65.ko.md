# 0.5.65 사용자 보고 재현 근거와 수정

0.5.64.0의 d0e3492847624fbfa16206a0b6eac4ad 세션을 작업 폴더 work/log-review-065에 보존했다. 이전 버전 행은 새 기능 검증에서 분리했다.

## 합계 제외 · 한도 초과

문구의 발생 경로는 AttackActionPolicy의 Overused 검사다. 연산량/DP/로그 제한과 관계없다. 엔진 ActionsState.Use는 이동/표준/신속 중 선택한 행동을 쓸 수 없으면 공용 Overused를 켠다. TurnController는 접근 불가능/다른 활성 행동 계획에서도 같은 플래그를 사용한다.

로그에는 StandardCurrent=4(Lost), MoveCurrent=0인 상태와 둘 다5(Used)인 상태가 있다. 이들은 공격 합계 제외가 맞으며, 이전 UI가 무기 계산 자체를 생략해서 정보가 사라지는 문제가 있었다. StandardCurrent=0, MoveCurrent=0이면서 접근 이동이 필요한 Overused도 확인된다. 현재 사거리에서 이동 없이 표준 공격 가능한 경우에만 별도 단일 공격 추정을 허용하며, 실패한 접근이나 소모된 행동을 무조건 무시하지 않는다.

수정 후 합계에서 제외되어도 참고 행의 명중률/AB/AC/피해는 보인다. 해당 행은 실행 가능 합계·HP 예상 감소·Beta 실제 판정 연결에서 제외한다.

## 주문서 확률 미표시

환영 살인마의 1,625,647~1,630,382ms 부근 미리보기에서 parameterStatus=NotObserved, DC=null, casterLevel=null, parameterFallbackReason=SpecialOrUnresolvedParameterSource를 확인했다. 공격 판정이 없는 주문이고 의지/인내/SR 모두 입력 부족으로 비었다.

기존 SpellParameterReader는 주문책 기반을 전제로 하며 아이템 출처를 거절했다. 반면 게임 CalculateParams는 아이템 출처일 때 GetParamsFromItem으로 즉시 반환한다. 일반 RuleCalculateAbilityParams를 통과하지 않으므로 일반 주문책 경로와 별개 구현이 필요하다.

엔진 IL을 확인해 아이템 전용 우선순위를 독립 구현했다. 일반 주문서는 전문화 책 CL로 대체하고 제작 주문서는 저장 CL과 책 CL 중 최대를 사용한다. 제작 DC는 숙련 능력치 DC로 덮지 않는다. 아이템 DC/CL은 일반 SpellFocus/AddCasterLevel 같은 주문책 보정과 섞지 않는다. 실제 게임 규칙을 재실행하거나 HCO 출력을 읽지 않는다.

특수 AutoMetamagic의 미판독 조건은 원인을 로그에 남기며 미확인으로 유지한다. 이번 빌드의 수치 정확성은 실제 사용 후 최종 판정 대조가 남아 있다.
