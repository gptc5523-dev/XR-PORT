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
    ///   - 운전모드: 오른손 스틱 X → 트롤리,  왼손 스틱 Y → 호이스트(위=올림)
    ///   - 갠트리모드: 왼손 스틱 X → 갠트리(크레인 전체 좌우 주행)
    ///   - 이동모드: 스틱 무시, XR 로코모션(걷기) 활성
    /// [공통] Y 버튼(왼손) → 집기,  X 버튼(왼손) → 놓기
    ///   (집기/놓기를 X·Y에 둬서 트리거/그립은 손 직접 집기[XRGrabInteractable]와 겹치지 않음)
    /// A 버튼(오른손 주): 운전/갠트리 모드일 때 운전실 시점(트롤리 위로 시점 이동, 고개 숙여 내려다봄) 토글.
    /// 운전/갠트리 모드일 때만 씬의 XR 로코모션을 끄고, 이동모드면 복구한다.
    /// </summary>
    [AddComponentMenu("Container/STS Crane/STS Crane VR Controller")]
    [RequireComponent(typeof(StsCrane))]
    [DisallowMultipleComponent]
    public sealed class StsCraneVRController : MonoBehaviour
    {
        public enum Mode { Move = 0, Crane = 1, Gantry = 2 }
        /// <summary>모드 목록 표시명(인덱스 = Mode 값). HUD가 공유.</summary>
        public static readonly string[] ModeNames = { "이동모드", "운전모드", "갠트리모드" };

        [Header("모드")]
        [Tooltip("시작 시 바로 운전모드 (테스트 편의). 게임 흐름에선 false(이동모드 시작) 권장.")]
        [SerializeField] bool startInCraneMode = false;
        [Tooltip("운전/갠트리 모드일 때 끌 커스텀 이동 스크립트(있으면). XR 표준 로코모션은 자동 탐색됨.")]
        [SerializeField] Behaviour[] suppressWhileControlling;

        [Header("속도 (실제 m/s, 스틱 최대 시 — 모델 1/24 축척 자동 반영)")]
        [Tooltip("트롤리 횡행 — 실제 STS 정격 ≈ 240 m/min = 4 m/s")]
        [SerializeField] float trolleySpeedMps = 4f;
        [Tooltip("호이스트 공하(빈 스프레더) 권상/권하 ≈ 2.7 m/s — 컨테이너 안 잡았을 때")]
        [SerializeField] float hoistEmptySpeedMps = 2.7f;
        [Tooltip("호이스트 만재(컨테이너 적재) 권상/권하 ≈ 1.3 m/s — 컨테이너 잡으면 자동 적용")]
        [SerializeField] float hoistLoadedSpeedMps = 1.3f;
        [Tooltip("갠트리 주행 — 실제 정격 ≈ 45 m/min = 0.75 m/s")]
        [SerializeField] float gantrySpeedMps = 0.75f;

        [Header("이동(걷기) 속도")]
        [Tooltip("이동모드 걷기 속도(m/s). 시작 시 XR 로코모션 Move Speed를 이 값으로 설정한다(인스펙터 기본 ~2.5보다 낮춤). 0 이하면 안 건드림.")]
        [SerializeField] float walkSpeed = 1.2f;

        // 실제 m/s에 crane.ModelScale(=1/24)을 곱하면 모델(씬) 단위 m/s. 모델은 작아도 '실제 크레인이
        // 그 거리를 지나는 데 걸리는 시간(초)'은 현실과 동일. 축척은 StsCrane.ModelScale 단일 소스 참조.

        [Header("입력")]
        [SerializeField, Range(0f, 0.5f)] float deadzone = 0.12f;
        [Tooltip("모드 선택 스틱 위/아래 플릭 임계값(이 이상 밀어야 모드 변경)")]
        [SerializeField, Range(0.5f, 0.95f)] float modeFlickThreshold = 0.7f;
        [Tooltip("Console에 입력/모드 로그 출력")]
        [SerializeField] bool debugLog = true;

        [Header("운전실 시점 (운전모드에서 A 버튼 토글)")]
        [Tooltip("시점을 붙일 트롤리 하위 부품 이름(예: Trolley_Head, Operator_Cab). 못 찾으면 트롤리 본체 기준.")]
        [SerializeField] string cabAnchorName = "Trolley_Head";
        [Tooltip("위 부품 기준 카메라 오프셋(크레인 로컬 m, 스케일 무관). x=앞뒤(-=기계실/육지, +=바다), y=상하, z=좌우.")]
        [SerializeField] Vector3 cabLocalOffset = new Vector3(-0.035f, -0.03f, -0.05f);

        StsCrane crane;
        SpreaderGrabber grabber;

        Mode mode = Mode.Crane;
        /// <summary>현재 적용된 모드(B로 확정된 것). HUD가 '현재' 표시에 사용.</summary>
        public Mode CurrentMode => mode;
        /// <summary>스틱으로 가리키는 선택 후보 인덱스(아직 미적용). HUD가 커서(▸) 표시에 사용.</summary>
        public int SelectedIndex => selectedIndex;
        /// <summary>조종 중(운전 또는 갠트리)인지 — 이동모드면 false. 상태 HUD 표시 여부 판단에 사용.</summary>
        public bool CraneMode => mode != Mode.Move;

        int selectedIndex;   // 스틱이 가리키는 후보(0..2). B를 눌러야 mode로 확정됨.

        // 운전실 시점 상태 (A 토글)
        bool cabView, prevCabBtn, rigSaved;
        Transform rig;                       // XR Origin 루트(카메라 최상위 부모) — 이걸 옮겨 시점 이동
        Transform cabAnchor;                 // 시점 기준 부품(Trolley_Head 등)
        Vector3 savedRigPos, lastTrolleyPos; // 진입 전 위치 / 트롤리 추적용 직전 위치
        Quaternion savedRigRot;
        readonly List<Collider> rigColliders = new List<Collider>();   // 시점 중 잠시 끈 리그 콜라이더(복구용)
        /// <summary>운전실 시점(내려다보기) 활성 여부.</summary>
        public bool CabView => cabView;

        bool prevGrab, prevRelease, prevCycleBtn;
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

        void OnEnable()
        {
            var op = GetComponent<StsCraneOperator>();
            if (op != null) op.enabled = false;   // 수동 조종 중엔 자동 사이클 정지
            mode = startInCraneMode ? Mode.Crane : Mode.Move;
            selectedIndex = (int)mode;
            ApplyMode();
            ApplyWalkSpeed();
            if (debugLog) Debug.Log($"[Crane] VRController 활성 — 시작 모드 {ModeNames[(int)mode]}");
        }

        void OnDisable()
        {
            if (cabView) ExitCabView();   // 운전실 시점이면 시점 원위치 복귀
            mode = Mode.Move;   // 컨트롤러 끄면 로코모션 복구
            ApplyMode();
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

            // ───── 시점 높이 조절은 CraneViewHeightAdjuster(별도 컴포넌트, 호스트/관전자 공통)가 담당 ─────
            //   조종 컨트롤러는 '조절 중'이면 왼손 스틱을 높이 전용으로 양보(호이스트/갠트리 입력 무효화)한다.
            //   걷기 정지는 EnforceLocomotion의 locoOn 조건이 ViewHeightActive()로 매 프레임 반영한다.
            if (ViewHeightActive()) ls = Vector2.zero;

            // ───── 모드 선택: 스틱 위/아래로 '후보'만 이동 → B로 '확정' ─────
            //   스틱만으론 모드가 안 바뀜(후보 하이라이트만 이동). B를 눌러야 실제 전환.
            //   → 운전모드에서 오른쪽 스틱 좌우(트롤리) 조작 중 모드가 빠지는 충돌 해소.
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
            if (cabView)
            {
                if (mode == Mode.Move) ExitCabView();   // 이동모드로 바뀌면 자동 복귀
                else FollowTrolley();                    // 트롤리/갠트리 이동 따라 시점도 함께
            }

            // ───── 공통: 집기 / 놓기 (모드 무관) ─────
            bool grabNow = Btn(left, CommonUsages.secondaryButton);     // Y
            bool releaseNow = Btn(left, CommonUsages.primaryButton);    // X
            if (grabNow && !prevGrab) { if (debugLog) Debug.Log("[Crane] Y 입력 → 집기(Grab)"); grabber?.Grab(); }
            prevGrab = grabNow;
            if (releaseNow && !prevRelease) { if (debugLog) Debug.Log("[Crane] X 입력 → 놓기(Release)"); grabber?.Release(); }
            prevRelease = releaseNow;

            // ───── 모드별 조종 ─────
            if (mode == Mode.Move) return;   // 이동모드: 스틱(이동/승강) 무시 — 로코모션이 처리

            if (mode == Mode.Gantry)
            {
                // 갠트리 주행: 크레인 전체만 움직이고 트롤리/호이스트는 잠금(섞이지 않게)
                IAxisMover gantry = crane.Gantry;
                if (gantry != null && Mathf.Abs(ls.x) > deadzone)
                    gantry.MoveTo(gantry.Current + ls.x * gantrySpeedMps * crane.ModelScale * Time.deltaTime);
                return;
            }

            // mode == Mode.Crane
            IAxisMover trolley = crane.Trolley;
            if (trolley != null && Mathf.Abs(rs.x) > deadzone)
                trolley.MoveTo(trolley.Current + rs.x * trolleySpeedMps * crane.ModelScale * Time.deltaTime);

            IAxisMover hoist = crane.Spreader;
            if (hoist != null && Mathf.Abs(ls.y) > deadzone)
            {
                // 컨테이너 적재 시 만재 속도(느림), 빈 스프레더면 공하 속도(빠름) — 실제 STS와 동일
                bool loaded = crane.Attach != null && crane.Attach.HasContainer;
                float hoistMps = loaded ? hoistLoadedSpeedMps : hoistEmptySpeedMps;
                hoist.MoveTo(hoist.Current + ls.y * hoistMps * crane.ModelScale * Time.deltaTime);
            }
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

        // ───────── 운전실 시점 (시점만 트롤리 위로 이동, 크기 변경 없음) ─────────
        void EnterCabView()
        {
            var cam = Camera.main;
            var trolleyT = (crane.Trolley as Component)?.transform;
            if (cam == null || trolleyT == null)
            {
                if (debugLog) Debug.LogWarning("[Crane] 운전실 시점 실패 — Main 카메라 또는 트롤리를 못 찾음");
                return;
            }
            cabAnchor = FindCabAnchor(trolleyT);
            rig = cam.transform.root;          // XR Origin 루트 이동(카메라+컨트롤러 함께)
            savedRigPos = rig.position;
            savedRigRot = rig.rotation;
            rigSaved = true;

            // 리그가 크레인/컨테이너 속으로 들어가 물리적으로 밀어버리지 않도록, 리그 콜라이더(손·몸 충돌체)를 잠시 끔.
            //   (CharacterController도 Collider 하위라 함께 꺼짐. 시점 나갈 때 복구.)
            rigColliders.Clear();
            foreach (var c in rig.GetComponentsInChildren<Collider>(true))
                if (c.enabled) { c.enabled = false; rigColliders.Add(c); }

            // 카메라가 운전실(기준 부품 + 오프셋)에 오도록 리그를 평행 이동. 시선 방향은 사용자 머리에 맡김(VR).
            //   오프셋은 크레인 방향(트롤리 회전)으로만 회전시키고 스케일은 안 곱함 → 1/24 모델에서도 m 단위 그대로.
            Vector3 target = cabAnchor.position + trolleyT.rotation * cabLocalOffset;
            rig.position += target - cam.transform.position;
            lastTrolleyPos = trolleyT.position;

            cabView = true;
            Haptic(InputDevices.GetDeviceAtXRNode(XRNode.RightHand), 0.4f, 0.06f);
            if (debugLog) Debug.Log($"[Crane] A → 운전실 시점 ON — 기준 '{cabAnchor.name}', 오프셋 {cabLocalOffset} (고개 숙여 아래를 보세요)");
        }

        // 시점 기준 부품 찾기 — 트롤리 하위에서 이름으로(재귀). 못 찾으면 트롤리 본체.
        Transform FindCabAnchor(Transform trolleyT)
        {
            if (!string.IsNullOrEmpty(cabAnchorName))
                foreach (var t in trolleyT.GetComponentsInChildren<Transform>(true))
                    if (t.name == cabAnchorName) return t;
            return trolleyT;
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
        void ApplyMode() => EnforceLocomotion();

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
        void ApplyWalkSpeed()
        {
            if (walkSpeed <= 0f) return;
            var locoType = LocomotionProviderType;
            if (locoType == null) return;
            int n = 0;
            foreach (var o in FindObjectsByType(locoType, FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                var prop = o.GetType().GetProperty("moveSpeed");
                if (prop != null && prop.CanWrite && prop.PropertyType == typeof(float))
                {
                    float old = (float)prop.GetValue(o);   // 설정 전 기존 속도(빠른지/느린지 판단용)
                    prop.SetValue(o, walkSpeed);
                    n++;
                    if (debugLog)
                        Debug.Log($"[Crane] 걷기 속도: '{o.name}' 기존 {old} m/s → {walkSpeed} m/s " +
                                  $"({(walkSpeed < old ? "느려짐" : walkSpeed > old ? "빨라짐" : "동일")})");
                }
            }
            if (debugLog && n == 0)
                Debug.LogWarning("[Crane] 걷기 속도 적용 실패 — moveSpeed 가진 프로바이더 없음(리그에 ContinuousMove/DynamicMoveProvider 확인).");
        }

    }
}
