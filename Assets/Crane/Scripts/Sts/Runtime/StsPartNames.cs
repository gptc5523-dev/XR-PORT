namespace Container.Crane.Sts
{
    /// <summary>
    /// STS 크레인/부두/HUD 절차 생성물의 '정규 GameObject 이름' SSOT(Single Source Of Truth).
    ///
    /// 외부 감사 H6: 부품 이름을 생산부(StsCraneCreator/StsQuayGroundCreator/HUD 생성/StartPoint 메뉴)와
    /// 소비부(이름 == 비교·StartsWith·GameObject.Find)에 각각 리터럴로 박아둔 탓에, 리네임 시 한쪽만 바뀌면
    /// 조용히 매칭 실패(라벨/콜라이더/시점 탐색이 빈 결과)했다. 양쪽이 이 상수를 공유하면 동시 변경이 강제된다.
    ///
    ///   ★ 값은 현재 코드 문자열 그대로 — SSOT화는 동작을 바꾸지 않는다(리팩터링 only).
    ///
    /// 런타임 어셈블리(Assembly-CSharp)에 두어 에디터 생성기(Assembly-CSharp-Editor)·Container 도메인 모두에서
    /// 참조 가능하다. Container.Crane.Sts 하위(.Net/.EditorTools)는 무수식 'StsPartNames'로,
    /// 다른 네임스페이스(ContainerProject.* 등)는 정규명 'Container.Crane.Sts.StsPartNames'로 참조한다.
    /// </summary>
    public static class StsPartNames
    {
        // ── 다리/주행(갠트리) ─────────────────────────────────────────────
        /// <summary>다리 기둥(포스트). 갠트리 라벨 앵커.</summary>
        public const string LegPost = "Leg_Post";
        /// <summary>다리 충돌체(Numbered 접미사 붙음 → BaseName 비교). GantryMover가 수집.</summary>
        public const string LegCollider = "Leg_Collider";

        // ── 트롤리/운전실 시점 ────────────────────────────────────────────
        /// <summary>트롤리 본체 헤드. VR 운전실 시점 앵커 기본값.</summary>
        public const string TrolleyHead = "Trolley_Head";
        /// <summary>전용 운전실 시점(좌석 눈높이). StsCraneVRController가 우선 탐색.</summary>
        public const string CabViewpoint = "Cab_Viewpoint";
        /// <summary>운전실 '바닥' 앵커(발밑 화물 내려다보기 눈높이) — 옛 기본값.
        /// 감사 확인: StsCraneCreator에 이 이름의 생산부가 없다 → 폴백으로만 빠져 좌석 눈높이(Cab_Viewpoint)에 갇혔음.
        /// 현재 cabFloorAnchorName 기본값은 실재 부품 <see cref="CabFloorRear"/>로 교체됨.</summary>
        public const string CabKick = "Cab_Kick";
        /// <summary>운전실 후방 바닥 패널(폴백 셸 생산, BaseName 비교 → 'Cab_Fb_FloorRear_1' 등 매칭).
        /// VR 운전실 시점의 눈 '아래' 기준 — 이 바닥 패널 밑에 카메라를 둬 발밑 화물을 막힘없이 내려다본다.</summary>
        public const string CabFloorRear = "Cab_Fb_FloorRear";

        // ── 스프레더 트위스트락 ───────────────────────────────────────────
        /// <summary>트위스트락 콘(Numbered 접미사 → BaseName 비교). 잠금 애니/잡기 기준점.</summary>
        public const string TwistlockCone = "Twistlock_Cone";
        /// <summary>트위스트락 헤드(Numbered 접미사 → BaseName 비교).</summary>
        public const string TwistlockHead = "Twistlock_Head";

        // ── 결박(타이다운) ────────────────────────────────────────────────
        /// <summary>결박 봉 이름 접두사. TiedownController가 StartsWith로 수집(여러 개).</summary>
        public const string TiedownRodPrefix = "Tiedown_Rod";

        // ── 붐/기계실/평형추(정적 라벨용) ─────────────────────────────────
        /// <summary>붐 거더(트롤리 레일). 정적 라벨 앵커(현재 라벨 호출은 주석 처리됨).</summary>
        public const string BoomGirder = "Boom_Girder";
        /// <summary>기계실. 정적 라벨 앵커(현재 주석 처리됨).</summary>
        public const string MachineryHouse = "Machinery_House";
        /// <summary>운전실. 정적 라벨 앵커(현재 주석 처리됨).
        /// 주의(가설): 'Operator_Cab' 이름의 생산부가 없다(아래 감사 메모 참조).</summary>
        public const string OperatorCab = "Operator_Cab";
        /// <summary>평형추. 정적 라벨 앵커(현재 주석 처리됨).
        /// 주의: 감사 #1·#2로 'Counterweight' 생산부가 삭제됨(아래 감사 메모 참조).</summary>
        public const string Counterweight = "Counterweight";

        // ── 부두(Quay) ────────────────────────────────────────────────────
        /// <summary>부두 바닥(걷는 면). StartPlacer/ViewHeightAdjuster/GantryRangeFit/VRTest가 탐색.</summary>
        public const string QuayGround = "Quay_Ground";
        /// <summary>레일 이름 접두사(Rail_Land/Rail_Water/Rail_Sweeper …). 소비부는 StartsWith로 레인 탐색.</summary>
        public const string RailPrefix = "Rail_";

        // ── 플레이어 시작 마커 ────────────────────────────────────────────
        /// <summary>플레이어 시작 지점 마커. 타입(CranePlayerStartPoint)으로 못 찾을 때 이름 폴백.</summary>
        public const string PlayerStartPoint = "PlayerStartPoint";

        // ── HUD 캔버스 ────────────────────────────────────────────────────
        /// <summary>크레인 상태 패널 캔버스. CraneStatusHUD가 생성, ModeSelectorHUD가 GameObject.Find로 추적.</summary>
        public const string CraneStatusCanvas = "CraneStatusCanvas";

        // ── 외부 XR 리그 명명 추정(우리 빌더가 만드는 게 아님) ────────────
        // CraneHud.FindController / CraneControllerArrowHUD가 XRI/Hands 리그의 컨트롤러 객체를 이름으로 휴리스틱
        // 탐색할 때 쓰는 소문자 부분문자열(대소문자 무시 비교). 리그 패키지가 바꾸면 휴리스틱이 깨질 수 있으므로
        // 장기적으로는 SerializeField로 노출(인스펙터 지정)하는 편이 안전하다. 지금은 동작 불변 유지.
        /// <summary>(외부 리그 추정) 컨트롤러 객체 이름에 들어가는 부분문자열.</summary>
        public const string ControllerNameHint = "controller";
        /// <summary>(외부 리그 추정) 손 추적 앵커 객체 이름에 들어가는 부분문자열.</summary>
        public const string HandNameHint = "hand";
    }
}
