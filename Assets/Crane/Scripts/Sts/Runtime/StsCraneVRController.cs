using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;            // InputDevices — Quest 컨트롤러 직접 읽기(OpenXR 액션 에셋 비의존)

namespace Container.Crane.Sts
{
    /// <summary>
    /// VR 컨트롤러(Quest)로 STS 크레인을 수동 조종. 입력은 UnityEngine.XR.InputDevices로 직접 읽음
    /// (코드 InputAction이 OpenXR에서 입력을 못 받는 문제 회피). 키보드 미사용 — 컨트롤러 전용.
    ///
    /// [모드] 이동 / 운전 / 갠트리 3가지 (CraneModeSelectorHUD가 오른쪽 컨트롤러에 목록 표시)
    ///   - 오른쪽 스틱 위/아래(상하) 플릭 → 모드 후보 이동(하이라이트만, 좌우=트롤리와 안 겹치게 가드)
    ///   - B 버튼(오른손 보조) → 현재 후보를 실제 모드로 확정 (스틱만으론 안 바뀜 → 조종 중 충돌 방지)
    /// [조종]
    ///   - 조종모드: 오른손 스틱 X → 트롤리,  왼손 스틱 Y → 호이스트(위=올림)
    ///   - 갠트리모드: 왼손 스틱 X → 갠트리(크레인 전체 좌우 주행)
    ///   - 이동모드: 스틱 무시, XR 로코모션(걷기) 활성
    /// [공통] Y 버튼(왼손) → 집기,  X 버튼(왼손) → 놓기
    ///   (집기/놓기를 X·Y에 둬서 트리거/그립은 손 직접 집기[XRGrabInteractable]와 겹치지 않음)
    /// A 버튼(오른손 주): 운전/갠트리 모드일 때 운전실 시점(운전실 좌석 눈높이로 이동·스프레더 향, 고개 숙여 내려다봄) 토글.
    /// 운전/갠트리 모드일 때만 씬의 XR 로코모션을 끄고, 이동모드면 복구한다.
    /// </summary>
    [AddComponentMenu("Container/STS Crane/STS Crane VR Controller")]
    [RequireComponent(typeof(StsCrane))]
    [DisallowMultipleComponent]
    public sealed class StsCraneVRController : MonoBehaviour
    {
        public enum Mode { Move = 0, Crane = 1, Gantry = 2 }
        /// <summary>모드 목록 표시명(인덱스 = Mode 값). HUD가 공유.</summary>
        public static readonly string[] ModeNames = { "이동모드", "조종모드", "갠트리모드" };

        [Header("모드")]
        [Tooltip("시작 시 바로 조종모드 (테스트 편의). 게임 흐름에선 false(이동모드 시작) 권장.")]
        [SerializeField] bool startInCraneMode = false;
        [Tooltip("운전/갠트리 모드일 때 끌 커스텀 이동 스크립트(있으면). XR 표준 로코모션은 자동 탐색됨.")]
        [SerializeField] Behaviour[] suppressWhileControlling;

        [Header("속도 (실제 m/s, 스틱 최대 시 — 모델 1/24 축척 자동 반영)")]
        [Tooltip("트롤리 횡행 — 실제 STS 정격 ≈ 198 m/min = 3.3 m/s (240/4.0에서 묵직하게 하향)")]
        [SerializeField] float trolleySpeedMps = 3.3f;
        [Tooltip("호이스트 공하(빈 스프레더) 권상/권하 ≈ 2.7 m/s — 컨테이너 안 잡았을 때")]
        [SerializeField] float hoistEmptySpeedMps = 2.7f;
        [Tooltip("호이스트 적재-경하중(가벼운 컨테이너) 권상/권하 ≈ 1.8 m/s")]
        [SerializeField] float hoistLoadedLightMps = 1.8f;
        [Tooltip("호이스트 적재-정격(무거운 컨테이너) 권상/권하 ≈ 0.9 m/s — 무게 클수록 이 값에 수렴")]
        [SerializeField] float hoistLoadedHeavyMps = 0.9f;
        [Tooltip("권상 속도 보간의 경하중 톤수 브레이크포인트(이하면 light 속도). 정격 용량 바꾸면 여기를 조정.")]
        [SerializeField] float hoistLightLoadTons = 8f;
        [Tooltip("권상 속도 보간의 정격 톤수 브레이크포인트(이상이면 heavy 속도). 정격 용량 바꾸면 여기를 조정.")]
        [SerializeField] float hoistRatedLoadTons = 32f;
        [Tooltip("갠트리 주행 — 실제 정격 ≈ 45 m/min = 0.75 m/s")]
        [SerializeField] float gantrySpeedMps = 0.75f;

        [Header("이동(걷기) 속도")]
        [Tooltip("이동모드 걷기 속도(m/s, 체감). 시작 시 XR 로코모션 Move Speed를 이 값으로 설정한다. 0 이하면 안 건드림. 수직이동(ViewHeightSpeed)과 동일한 8로 맞춤. 너무 빠르면/느리면 이 값만 조정.")]
        [SerializeField] float walkSpeed = 8f;

        // 실제 m/s에 crane.ModelScale(=1/24)을 곱하면 모델(씬) 단위 m/s. 모델은 작아도 '실제 크레인이
        // 그 거리를 지나는 데 걸리는 시간(초)'은 현실과 동일. 축척은 StsCrane.ModelScale 단일 소스 참조.

        [Header("입력")]
        [SerializeField, Range(0f, 0.5f)] float deadzone = 0.12f;
        [Tooltip("모드 선택 스틱 위/아래 플릭 임계값(이 이상 밀어야 모드 변경)")]
        [SerializeField, Range(0.5f, 0.95f)] float modeFlickThreshold = 0.7f;
        [Tooltip("Console에 입력/모드 로그 출력")]
        [SerializeField] bool debugLog = true;

        [Header("운전실 시점 (조종모드에서 A 버튼 토글)")]
        [Tooltip("시점을 붙일 트롤리 하위 부품 이름(예: Trolley_Head, Operator_Cab). 못 찾으면 트롤리 본체 기준.")]
        [SerializeField] string cabAnchorName = StsPartNames.TrolleyHead;
        [Tooltip("위 부품 기준 카메라 오프셋(크레인 로컬 m, 스케일 무관). x=앞뒤(-=기계실/육지, +=바다), y=상하, z=좌우.")]
        [SerializeField] Vector3 cabLocalOffset = new Vector3(-0.035f, -0.03f, -0.05f);
        [Tooltip("운전실 '바닥' 부품 이름 — 전용 시점일 때 눈 위치를 이 바닥 '아래'에 둔다(발밑 화물 내려다보기). 못 찾으면 좌석 눈높이(Cab_Viewpoint) 유지.")]
        [SerializeField] string cabFloorAnchorName = StsPartNames.CabFloorRear;   // 옛 Cab_Kick은 생산부 없어 좌석 안에 갇혔음
        [Tooltip("바닥 부품 '아래'로 카메라를 내릴 거리(크레인 로컬 m, 스케일 무관). 바닥 패널 밑에서 발밑 화물을 막힘없이 내려다본다. VR에서 미세조정.")]
        [SerializeField, Range(0f, 0.04f)] float cabFloorDropDown = 0.008f;   // 바닥 반두께(~0.003) + 여유(~0.005). 실척 ≈ 0.008×24 ≈ 0.19m

        StsCrane crane;
        SpreaderGrabber grabber;

        Mode mode = Mode.Crane;
        /// <summary>현재 적용된 모드(B로 확정된 것). HUD가 '현재' 표시에 사용.</summary>
        public Mode CurrentMode => mode;
        /// <summary>스틱으로 가리키는 선택 후보 인덱스(아직 미적용). HUD가 커서(▸) 표시에 사용.</summary>
        public int SelectedIndex => selectedIndex;
        /// <summary>조종 중(운전 또는 갠트리)인지 — 이동모드면 false. 상태 HUD 표시 여부 판단에 사용.</summary>
        public bool CraneMode => mode != Mode.Move;
        /// <summary>조종 활성 여부 — 관찰(기본)이면 false. 오른쪽 스틱클릭으로 토글. 모드선택·조종 HUD·축 조종은 이게 true일 때만.</summary>
        public bool ControlActive => controlActive;

        int selectedIndex;   // 스틱이 가리키는 후보(0..2). B를 눌러야 mode로 확정됨.

        // Update에서 샘플링한 조종 스틱값 — 실제 축 적분은 FixedUpdate에서 소비(물리 틱 정합).
        Vector2 driveRS, driveLS;

        // 운전실 시점 상태 (A 토글)
        bool cabView, prevCabBtn, rigSaved;
        Transform rig;                       // XR Origin 루트(카메라 최상위 부모) — 이걸 옮겨 시점 이동
        Transform cabAnchor;                 // 시점 기준 부품(Trolley_Head 등)
        Vector3 savedRigPos, lastTrolleyPos; // 진입 전 위치 / 트롤리 추적용 직전 위치
        Quaternion savedRigRot;
        readonly List<Collider> rigColliders = new List<Collider>();   // 시점 중 잠시 끈 리그 콜라이더(복구용)
        /// <summary>운전실 시점(내려다보기) 활성 여부.</summary>
        public bool CabView => cabView;

        bool prevGrab, prevRelease, prevCycleBtn, prevModeToggleBtn;
        bool controlActive;   // 관찰(false, 기본) ⇄ 조종(true) — 오른쪽 스틱클릭 토글
        bool stickCentered = true;   // 모드 스틱 플릭 엣지 검출(중앙 복귀 후에만 다음 플릭 인정)
        readonly List<Behaviour> locoProviders = new List<Behaviour>();   // 캐시된 XR 로코모션 프로바이더
        bool locoCached;

        // 시점 높이 조절은 별도 컴포넌트(CraneViewHeightAdjuster)가 담당 — 여기선 '조절 중' 상태만 참조.
        CraneViewHeightAdjuster viewHeight;

        // XRI LocomotionProvider 타입을 리플렉션으로(컴파일 의존성 제거)
        static System.Type _locoType;
        static bool _locoSearched;
        static System.Type LocomotionProviderType
        {
            get
            {
                if (_locoSearched) return _locoType;
                _locoSearched = true;
                _locoType =
                    System.Type.GetType("UnityEngine.XR.Interaction.Toolkit.Locomotion.LocomotionProvider, Unity.XR.Interaction.Toolkit")
                    ?? System.Type.GetType("UnityEngine.XR.Interaction.Toolkit.LocomotionProvider, Unity.XR.Interaction.Toolkit");
                return _locoType;
            }
        }

        // SnapTurnProvider 타입(45° 스냅 회전) — 로코모션 토글에서 제외/항상 끄기 위해 별도로 식별.
        static System.Type _snapTurnType;
        static bool _snapTurnSearched;
        static System.Type SnapTurnProviderType
        {
            get
            {
                if (_snapTurnSearched) return _snapTurnType;
                _snapTurnSearched = true;
                _snapTurnType = System.Type.GetType(
                    "UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning.SnapTurnProvider, Unity.XR.Interaction.Toolkit");
                return _snapTurnType;
            }
        }

        void Awake()
        {
            crane = GetComponent<StsCrane>();
            grabber = GetComponent<SpreaderGrabber>();
        }

        /// <summary>지금 조종기를 받는 컨트롤러. 크레인이 여러 대면 PortDemoDirector 가 가까운 한 대만 켠다 —
        ///   HUD·시점 보조는 이걸 따라간다(FindAnyObjectByType 은 꺼져 있는 다른 크레인 컨트롤러를 집을 수 있다).</summary>
        public static StsCraneVRController Active { get; private set; }

        void OnEnable()
        {
            Active = this;
            mode = startInCraneMode ? Mode.Crane : Mode.Move;
            selectedIndex = (int)mode;
            ApplyMode();   // 로코모션 + 자동/수동(이동모드=자동, 운전/갠트리=수동) 연동
            ApplyWalkSpeed();
            if (debugLog) Debug.Log($"[Crane] VRController 활성 — 시작 모드 {ModeNames[(int)mode]}");
        }

        void OnDisable()
        {
            if (cabView) ExitCabView();   // 운전실 시점이면 시점 원위치 복귀
            mode = Mode.Move;   // 컨트롤러 끄면 로코모션 복구
            ApplyMode();
            if (Active == this) Active = null;
        }

        void Update()
        {
            if (crane == null) return;

            // 매 프레임 로코모션 상태를 목표(이동모드 && 높이조절 아님)로 강제 — 모드 전환·높이조절이
            // 어떻게 섞여도 '이동모드면 항상 걷기 가능'을 보장(아래 조기 return에 안 막히게 맨 앞에서).
            EnforceLocomotion();

            var right = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            var left = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);

            Vector2 rs = Vector2.zero, ls = Vector2.zero;
            if (right.isValid) right.TryGetFeatureValue(CommonUsages.primary2DAxis, out rs);
            if (left.isValid) left.TryGetFeatureValue(CommonUsages.primary2DAxis, out ls);

            // QA 자동 시나리오 합성 입력 — 기기 대신 주입값으로 스틱을 대체(나머지 처리는 동일 → FixedUpdate 축적분 경로 그대로).
            if (qaDrive) { rs = qaRS; ls = qaLS; }

            // 시점 높이 조절은 CraneViewHeightAdjuster(별도 컴포넌트, 호스트/관전자 공통)가 담당
            //   조종 컨트롤러는 '조절 중'이면 왼손 스틱을 높이 전용으로 양보(호이스트/갠트리 입력 무효화)한다.
            //   걷기 정지는 EnforceLocomotion의 locoOn 조건이 ViewHeightActive()로 매 프레임 반영한다.
            if (ViewHeightActive()) ls = Vector2.zero;

            // 관찰 ⇄ 조종 토글: 오른쪽 스틱 클릭(primary2DAxisClick). 기본은 관찰(controlActive=false)
            //   관찰: 크레인은 PLC/시뮬이 움직이고 사용자 조종 입력은 전면 차단 → 호스트·관전자 동일 화면(모드선택 HUD도 숨김).
            //   조종: 스틱 클릭 한 번으로 진입, 이때만 모드선택 HUD가 뜨고 축 조종이 열린다.
            bool modeToggleNow = Btn(right, CommonUsages.primary2DAxisClick);
            if (modeToggleNow && !prevModeToggleBtn)
            {
                controlActive = !controlActive;
                Haptic(right, 0.4f, 0.05f);
                if (!controlActive)
                {
                    if (cabView) ExitCabView();   // 관찰로 빠지면 운전실 시점 해제
                    SetMode(Mode.Move);            // 이동모드로 되돌려 걷기(둘러보기) 보장
                }
            }
            prevModeToggleBtn = modeToggleNow;

            // 관찰 모드: 조종 입력(모드선택/확정/시점/집기/축이동) 전면 차단. 걷기·시점높이는 위에서 이미 처리됨.
            if (!controlActive) { driveRS = driveLS = Vector2.zero; return; }

            // 모드 선택: 스틱 위/아래로 '후보'만 이동 → B로 '확정'
            //   스틱만으론 모드가 안 바뀜(후보 하이라이트만 이동). B를 눌러야 실제 전환.
            //   → 조종모드에서 오른쪽 스틱 좌우(트롤리) 조작 중 모드가 빠지는 충돌 해소.
            //   추가 가드: 좌우로 밀 땐(|x|≥0.5) 후보도 안 움직임. 중앙 복귀 후에만 다음 이동 인정(폭주 방지).
            if (Mathf.Abs(rs.y) < 0.3f) stickCentered = true;
            if (stickCentered && Mathf.Abs(rs.y) > modeFlickThreshold && Mathf.Abs(rs.x) < 0.5f)
            {
                selectedIndex = Mathf.Clamp(selectedIndex + (rs.y > 0f ? -1 : 1), 0, 2);
                Haptic(right, 0.2f, 0.02f);   // 후보 이동 — 가벼운 진동
                stickCentered = false;
            }

            // B 버튼: 현재 후보를 실제 모드로 확정(엣지 검출)
            bool applyNow = Btn(right, CommonUsages.secondaryButton);
            if (applyNow && !prevCycleBtn) SetMode((Mode)selectedIndex);
            prevCycleBtn = applyNow;

            // A 버튼: 운전/갠트리 모드에서 운전실 시점(내려다보기) 토글
            bool cabBtnNow = Btn(right, CommonUsages.primaryButton);
            if (cabBtnNow && !prevCabBtn && mode != Mode.Move) { if (cabView) ExitCabView(); else EnterCabView(); }
            prevCabBtn = cabBtnNow;
            // 이동모드로 바뀌면 시점 자동 복귀(여기서 즉시). 시점 추종(FollowTrolley)은 모든 이동이
            //   끝난 뒤(LateUpdate)에 카메라를 정렬해 트롤리 이동(FixedUpdate)과의 한 프레임 어긋남/저더를 막는다.
            if (cabView && mode == Mode.Move) ExitCabView();

            // 공통: 집기 / 놓기 (모드 무관)
            bool grabNow = Btn(left, CommonUsages.secondaryButton);     // Y
            bool releaseNow = Btn(left, CommonUsages.primaryButton);    // X
            if (grabNow && !prevGrab) { if (debugLog) Debug.Log("[Crane] Y 입력 → 집기(Grab)"); grabber?.Grab(); }
            prevGrab = grabNow;
            if (releaseNow && !prevRelease) { if (debugLog) Debug.Log("[Crane] X 입력 → 놓기(Release)"); grabber?.Release(); }
            prevRelease = releaseNow;

            // 모드별 조종 입력 저장 → 실제 축 이동은 FixedUpdate에서(물리 정합)
            //   입력 샘플링은 Update(프레임률)에서, kinematic 화물을 끌고 가는 축 적분은 FixedUpdate(고정틱)에서
            //   처리해 PhysX 접촉/스윕과 박자를 맞춘다 — 프레임률 의존·터널링 완화. (이동모드는 FixedUpdate가 무시)
            driveRS = rs;
            driveLS = ls;
        }

        // 축 이동(트롤리/호이스트/갠트리)은 물리 고정틱에서 — Update가 샘플링한 스틱값(driveRS/driveLS)을 소비.
        //   kinematic 컨테이너를 끌고 가는 이동이므로 PhysX와 같은 박자(FixedUpdate)에 둬야 충돌/안착이 결정적.
        void FixedUpdate()
        {
            if (crane == null || !controlActive || mode == Mode.Move) return;   // 관찰이거나 이동모드면 축 조종 없음
            float dt = Time.fixedDeltaTime;

            if (mode == Mode.Gantry)
            {
                // 갠트리 주행: 크레인 전체만 움직이고 트롤리/호이스트는 잠금(섞이지 않게)
                IAxisMover gantry = crane.Gantry;
                bool gActive = gantry != null && Mathf.Abs(driveLS.x) > deadzone;
                if (gActive)
                {
                    float deltaModel = driveLS.x * gantrySpeedMps * crane.ModelScale / gantry.WorldPerUnit * dt;
                    gantry.MoveTo(gantry.Current + deltaModel);
                    // QA S-PHYS-1: 축 적분이 FixedUpdate에서만 일어남 — 이동 시작 엣지에서 1줄(폭주 방지).
                    if (QaLog.Enabled && !qaGantryActive)
                        QaLog.Info("GANTRY", "move", $"phase=FixedUpdate dt={QaLog.F(dt)} input={QaLog.F(driveLS.x)} " +
                            $"mps={QaLog.F(gantrySpeedMps)} scale={QaLog.F(crane.ModelScale)} deltaModel={QaLog.F(deltaModel)}");
                }
                qaGantryActive = gActive;
                return;
            }

            // mode == Mode.Crane
            IAxisMover trolley = crane.Trolley;
            bool tActive = trolley != null && Mathf.Abs(driveRS.x) > deadzone;
            if (tActive)
            {
                float deltaModel = driveRS.x * trolleySpeedMps * crane.ModelScale / trolley.WorldPerUnit * dt;   // 축 단위(FBX RTG 트롤리는 루트 스케일 4.17 을 탄다)
                trolley.MoveTo(trolley.Current + deltaModel);
                if (QaLog.Enabled && !qaTrolleyActive)
                    QaLog.Info("TROLLEY", "move", $"phase=FixedUpdate dt={QaLog.F(dt)} input={QaLog.F(driveRS.x)} " +
                        $"mps={QaLog.F(trolleySpeedMps)} scale={QaLog.F(crane.ModelScale)} deltaModel={QaLog.F(deltaModel)}");
            }
            qaTrolleyActive = tActive;

            IAxisMover hoist = crane.Spreader;
            bool hActive = hoist != null && Mathf.Abs(driveLS.y) > deadzone;
            if (hActive)
            {
                // 빈 스프레더는 공하 속도(빠름). 적재 시 컨테이너 무게에 비례해 권상 속도 감소
                // (가벼우면 light, 정격 근처면 heavy) — 실제 STS 모터 정격 거동 모사.
                var attach = crane.Attach;
                float hoistMps, tons;
                string band;
                if (attach != null && attach.HasContainer)
                {
                    tons = attach.AttachedLoadTons;
                    float loadT = Mathf.InverseLerp(hoistLightLoadTons, hoistRatedLoadTons, tons);  // light톤→경하중, rated톤→정격
                    hoistMps = Mathf.Lerp(hoistLoadedLightMps, hoistLoadedHeavyMps, loadT);
                    band = tons <= hoistLightLoadTons ? "light" : (tons >= hoistRatedLoadTons ? "heavy" : "interp");
                }
                else { hoistMps = hoistEmptySpeedMps; tons = 0f; band = "empty"; }
                float deltaModel = driveLS.y * hoistMps * crane.ModelScale / hoist.WorldPerUnit * dt;
                hoist.MoveTo(hoist.Current + deltaModel);
                // QA S-PHYS-5: 하중별 권상속도 보간 — 권상 시작 엣지에서 1줄.
                if (QaLog.Enabled && !qaHoistActive)
                    QaLog.Info("HOIST", "speed", $"phase=FixedUpdate tons={QaLog.F(tons)} mps={QaLog.F(hoistMps)} " +
                        $"band={band} scale={QaLog.F(crane.ModelScale)} deltaModel={QaLog.F(deltaModel)}");
            }
            qaHoistActive = hActive;
        }

        // QA 축 이동 로그 엣지 추적 — 이동 시작 시점에만 1줄 찍어 매 물리틱 폭주 방지.
        bool qaTrolleyActive, qaHoistActive, qaGantryActive;

        // QA 자동 시나리오 합성 입력(VR 기기 없이 production 경로를 그대로 구동)
        bool qaDrive; Vector2 qaRS, qaLS;
        /// <summary>QA: 합성 스틱 구동 시작 — controlActive를 켜고 모드 확정. 이후 Update가 기기 대신 주입값을 쓴다.</summary>
        public void QaBeginDrive(Mode m) { qaDrive = true; controlActive = true; SetMode(m); }
        /// <summary>QA: 이번/다음 프레임에 적용할 합성 스틱값(rs=오른손, ls=왼손).</summary>
        public void QaSticks(Vector2 rs, Vector2 ls) { qaRS = rs; qaLS = ls; }
        /// <summary>QA: 합성 구동 종료 — 입력 0, 이동모드 복귀, 관찰로 전환.</summary>
        public void QaEndDrive() { qaDrive = false; qaRS = qaLS = Vector2.zero; SetMode(Mode.Move); controlActive = false; driveRS = driveLS = Vector2.zero; }

        // 운전실 시점 추종 — 트롤리/갠트리 이동(FixedUpdate)과 모든 Update가 끝난 뒤 카메라를 정렬해
        //   한 프레임 어긋남으로 인한 시점 저더(멀미 가중)를 방지.
        void LateUpdate()
        {
            if (cabView && mode != Mode.Move) FollowTrolley();   // 트롤리/갠트리 이동 따라 시점도 함께
        }

        // 모드 변경(같은 모드면 무시) — 로코모션 적용 + 진동 + 로그
        void SetMode(Mode m)
        {
            if (m == mode) return;   // m=(Mode)selectedIndex 이므로 같으면 커서도 이미 mode와 동기화 상태
            mode = m;
            selectedIndex = (int)mode;
            ApplyMode();
            Haptic(InputDevices.GetDeviceAtXRNode(XRNode.RightHand), 0.4f, 0.05f);
            if (debugLog) Debug.Log($"[Crane] 모드 → {ModeNames[(int)mode]}");
        }

        // 운전실 시점 (카메라를 운전실 좌석 눈높이 앵커로 이동, 크기 변경 없음)
        void EnterCabView()
        {
            var cam = Camera.main;
            var trolleyT = (crane.Trolley as Component)?.transform;
            if (cam == null || trolleyT == null)
            {
                if (debugLog) Debug.LogWarning("[Crane] 운전실 시점 실패 — Main 카메라 또는 트롤리를 못 찾음");
                return;
            }
            cabAnchor = FindCabAnchor(trolleyT, out bool dedicated);
            rig = cam.transform.root;          // XR Origin 루트 이동(카메라+컨트롤러 함께)
            savedRigPos = rig.position;
            savedRigRot = rig.rotation;
            rigSaved = true;

            // 리그가 크레인/컨테이너 속으로 들어가 물리적으로 밀어버리지 않도록, 리그 콜라이더(손·몸 충돌체)를 잠시 끔.
            //   (CharacterController도 Collider 하위라 함께 꺼짐. 시점 나갈 때 복구.)
            rigColliders.Clear();
            foreach (var c in rig.GetComponentsInChildren<Collider>(true))
                if (c.enabled) { c.enabled = false; rigColliders.Add(c); }

            // 카메라가 운전실 시점에 오도록 리그를 평행 이동.
            //   전용 앵커면: 시선(전방/요)은 Cab_Viewpoint 기준, 눈 '위치'는 운전실 후방 바닥 패널(Cab_Fb_FloorRear)
            //     '아래'로 내린다 — 좌석 눈높이는 바닥/콘솔/벽에 가려 발밑 화물이 안 보이므로, 바닥 패널 밑에서
            //     전면 경사창으로 바로 아래(스프레더/선박 셀)를 막힘없이 내려다보게 한다. (옛 Cab_Kick은 생산부가 없어
            //     항상 폴백→좌석 눈높이에 갇혀 '조종실 안' 시점이 됐었음.)
            //   레거시 앵커면: 기준부품 + 오프셋(트롤리 회전만 반영·스케일 안 곱함).
            Transform cabFloor = dedicated ? FindCabFloor(trolleyT) : null;
            Transform eyeAnchor = cabFloor != null ? cabFloor : cabAnchor;
            Vector3 target = cabFloor != null ? cabFloor.position - trolleyT.up * cabFloorDropDown   // 바닥 패널 '아래'
                           : dedicated         ? cabAnchor.position                                    // 바닥 폴백 → 좌석 눈높이
                                               : cabAnchor.position + trolleyT.rotation * cabLocalOffset;
            // 전용 앵커면 시선(요)을 운전실 전방(스프레더/바다쪽)에 정렬 — 상하 피치는 머리에 맡김(고개 숙여 내려다봄).
            if (dedicated)
            {
                Vector3 fwd = cabAnchor.forward; fwd.y = 0f;
                Vector3 camFwd = cam.transform.forward; camFwd.y = 0f;
                if (fwd.sqrMagnitude > 1e-4f && camFwd.sqrMagnitude > 1e-4f)
                    rig.rotation = Quaternion.FromToRotation(camFwd.normalized, fwd.normalized) * rig.rotation;
            }
            rig.position += target - cam.transform.position;   // (회전 후) 카메라를 시점으로 정렬
            lastTrolleyPos = trolleyT.position;

            cabView = true;
            Haptic(InputDevices.GetDeviceAtXRNode(XRNode.RightHand), 0.4f, 0.06f);
            if (debugLog) Debug.Log($"[Crane] A → 운전실 시점 ON — 시선기준 '{cabAnchor.name}', 눈위치 '{eyeAnchor.name}'" +
                (cabFloor != null ? $" (바닥 아래 {cabFloorDropDown:0.###} 드롭·시선 스프레더 정렬)"
                 : dedicated       ? " (좌석 눈높이 폴백·시선 스프레더 정렬)"
                                   : $", 오프셋 {cabLocalOffset}") + " (고개 숙여 아래를 보세요)");
        }

        // 시점 기준 부품 찾기 — 트롤리 하위에서 이름으로(재귀).
        //   우선순위: 전용 'Cab_Viewpoint'(운전실 좌석 눈높이 앵커, 생성기가 심음) → 직렬화된 cabAnchorName → 트롤리 본체.
        //   전용 앵커면 dedicated=true → EnterCabView가 오프셋 0 + 전방(스프레더) 시선정렬을 적용.
        Transform FindCabAnchor(Transform trolleyT, out bool dedicated)
        {
            dedicated = false;
            foreach (var t in trolleyT.GetComponentsInChildren<Transform>(true))
                if (CraneHud.BaseName(t.name) == StsPartNames.CabViewpoint) { dedicated = true; return t; }
            if (!string.IsNullOrEmpty(cabAnchorName))
                foreach (var t in trolleyT.GetComponentsInChildren<Transform>(true))
                    if (CraneHud.BaseName(t.name) == cabAnchorName) return t;
            return trolleyT;
        }

        // 운전실 '바닥' 부품(Cab_Fb_FloorRear 등) 찾기 — 전용 시점에서 눈 위치를 이 바닥 '아래'에 둬 발밑 화물을 내려다보게.
        //   1순위: 직렬화된 cabFloorAnchorName  2순위: 실재 후방 바닥 패널(Cab_Fb_FloorRear).
        //   기존 씬 인스턴스가 옛 'Cab_Kick'(생산부 없음)으로 직렬화돼 있어도 인스펙터 수정 없이 동작하도록 2순위 폴백을 둔다.
        //   둘 다 못 찾으면 null → EnterCabView가 좌석 눈높이(Cab_Viewpoint)로 폴백.
        Transform FindCabFloor(Transform trolleyT)
        {
            Transform byName = null, byRear = null;
            foreach (var t in trolleyT.GetComponentsInChildren<Transform>(true))
            {
                string bn = CraneHud.BaseName(t.name);
                if (byName == null && !string.IsNullOrEmpty(cabFloorAnchorName) && bn == cabFloorAnchorName) byName = t;
                if (byRear == null && bn == StsPartNames.CabFloorRear) byRear = t;
            }
            return byName != null ? byName : byRear;
        }

        // 트롤리가 움직인 만큼 시점도 같이 이동 — 운전실이 트롤리에 붙어 따라가게
        void FollowTrolley()
        {
            var trolleyT = (crane.Trolley as Component)?.transform;
            if (rig == null || trolleyT == null) return;
            rig.position += trolleyT.position - lastTrolleyPos;
            lastTrolleyPos = trolleyT.position;
        }

        void ExitCabView()
        {
            if (rigSaved && rig != null) { rig.position = savedRigPos; rig.rotation = savedRigRot; }
            foreach (var c in rigColliders) if (c != null) c.enabled = true;   // 콜라이더 복구
            rigColliders.Clear();
            cabView = false;
            rigSaved = false;
            if (debugLog) Debug.Log("[Crane] A → 운전실 시점 OFF (원위치 복귀)");
        }


        // 컨트롤러 짧은 진동 — 모드 전환 피드백
        static void Haptic(UnityEngine.XR.InputDevice d, float amplitude, float seconds)
        {
            if (!d.isValid) return;
            if (d.TryGetHapticCapabilities(out var caps) && caps.supportsImpulse)
                d.SendHapticImpulse(0, Mathf.Clamp01(amplitude), Mathf.Max(0f, seconds));
        }

        static bool Btn(UnityEngine.XR.InputDevice d, InputFeatureUsage<bool> usage)
            => d.isValid && d.TryGetFeatureValue(usage, out bool v) && v;

        // 로코모션(걷기·회전 등 + 수동 지정분)은 '이동모드 + 높이조절 중 아님'일 때만 켠다.
        //   목록 추적(disable/enable 큐) 방식은 모드 사이클·높이조절이 섞이면 상태가 어긋나
        //   '두 번째 이동모드에서 안 걸어지던' 버그가 났다. → 캐시한 프로바이더에 매 프레임 목표 상태를
        //   '직접' 강제하는 선언적 방식으로 교체(자가 치유, 누적/엇갈림 원천 차단). Update와 전환 시 호출.
        void ApplyMode()
        {
            EnforceLocomotion();
        }

        void EnforceLocomotion()
        {
            EnsureLocoProviders();
            bool locoOn = (mode == Mode.Move) && !ViewHeightActive();
            foreach (var b in locoProviders)
                if (b != null && b.enabled != locoOn) b.enabled = locoOn;
            if (suppressWhileControlling != null)
                foreach (var b in suppressWhileControlling)
                    if (b != null && b.enabled != locoOn) b.enabled = locoOn;
        }

        // XR 로코모션 프로바이더를 한 번 찾아 캐시(리그가 늦게 뜰 수 있어 찾을 때까지 재시도).
        void EnsureLocoProviders()
        {
            if (locoCached) return;
            var locoType = LocomotionProviderType;
            if (locoType == null) { locoCached = true; return; }   // XRI 없음 — 더 안 찾음
            var snapType = SnapTurnProviderType;
            foreach (var o in FindObjectsByType(locoType, FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (o is not Behaviour b) continue;
                // SnapTurn(45° 점프 + 0.5s debounce)은 '딱딱 끊기는' 회전이라 항상 끄고 토글 목록에서도 제외.
                //   (제외 안 하면 이동모드 진입마다 EnforceLocomotion이 도로 켜서 점프가 부활한다.)
                //   같은 오른손 스틱의 ContinuousTurn('Turn' 액션)이 부드러운 회전을 이어받는다.
                if (snapType != null && snapType.IsInstanceOfType(b)) { b.enabled = false; continue; }
                if (!locoProviders.Contains(b)) locoProviders.Add(b);
            }
            if (locoProviders.Count > 0) locoCached = true;
        }

        // 별도 컴포넌트(CraneViewHeightAdjuster)의 '높이 조절 중' 상태 — 조절 중엔 걷기 정지 + 왼손 스틱 양보.
        //   실제 높이 조절은 그 컴포넌트가 수행(호스트/관전자 공통). 여기선 입력 협조만 한다.
        bool ViewHeightActive()
        {
            if (viewHeight == null) viewHeight = FindAnyObjectByType<CraneViewHeightAdjuster>();
            return viewHeight != null && viewHeight.HeightHold;
        }

        // 걷기 속도 설정 — 로코모션 프로바이더 중 'moveSpeed' 속성을 가진 것(=ContinuousMove/DynamicMove)에 적용.
        //   리플렉션이라 XRI 버전·프리팹 직렬화와 무관하게 시작 시 한 번 박는다. (다른 프로바이더는 moveSpeed 없어 무시)
        /// <summary>리그 스케일이 바뀐 뒤(CranePlayerRigScale 등) 걷기 속도를 다시 적용한다(호환용 — 스케일 무관, walkSpeed 그대로).</summary>
        public void ReapplyWalkSpeed() => ApplyWalkSpeed();

        void ApplyWalkSpeed()
        {
            if (walkSpeed <= 0f) return;
            var locoType = LocomotionProviderType;
            if (locoType == null) return;
            // rigScale을 곱하지 않는다(중요). XRI ContinuousMoveProvider는 이동량 계산 시 이미
            //   `m_MoveSpeed * deltaTime * originTransform.localScale.x`로 리그 스케일(1/24)을 곱한다
            //   ("Adjust speed with user scale"). 여기서 또 walkSpeed×rigScale을 넣으면 1/24 × 1/24 = 1/576이라
            //   걷기가 사실상 0이 된다(턴은 회전이라 스케일 무관 → 정상 → '오른쪽만 되고 왼쪽 걷기 안 됨').
            //   → moveSpeed에는 원하는 '체감' 속도(walkSpeed)를 그대로 넣고, 월드 축소 반영은 XRI에 맡긴다.
            float effective = walkSpeed;
            int n = 0;
            foreach (var o in FindObjectsByType(locoType, FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                var prop = o.GetType().GetProperty("moveSpeed");
                if (prop != null && prop.CanWrite && prop.PropertyType == typeof(float))
                {
                    float old = (float)prop.GetValue(o);   // 설정 전 기존 속도(빠른지/느린지 판단용)
                    prop.SetValue(o, effective);
                    n++;
                    if (debugLog)
                        Debug.Log($"[Crane] 걷기 속도: '{o.name}' 기존 {old} → {effective} m/s (walkSpeed 그대로, 리그 스케일은 XRI가 반영)");
                }
            }
            if (debugLog && n == 0)
                Debug.LogWarning("[Crane] 걷기 속도 적용 실패 — moveSpeed 가진 프로바이더 없음(리그에 ContinuousMove/DynamicMoveProvider 확인).");
        }

    }
}
