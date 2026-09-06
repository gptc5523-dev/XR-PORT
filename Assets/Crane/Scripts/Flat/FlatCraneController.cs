using UnityEngine;
using UnityEngine.InputSystem;
using Container.Crane.Sts;

namespace Container.Crane.Flat
{
    /// <summary>
    /// 평면 모드 크레인 조작 — 게임패드로 트롤리·호이스트·갠트리를 움직이고 집기/놓기를 한다.
    ///
    /// ★ 기존 StsCraneVRController 를 대체하지 않는다. 그쪽은 VR 경로 그대로 두고, 여기서는
    ///   같은 IAxisMover(트롤리/호이스트/갠트리)·SpreaderGrabber 를 '호출만' 한다.
    ///   속도 상수는 VR 쪽과 같은 실물 정격값을 쓴다(정격이 바뀌면 양쪽을 같이 고쳐야 한다).
    ///
    /// ★ VR과 달리 모드 전환이 없다. 게임패드는 스틱 2개 + D패드 + 트리거로 축이 충분해,
    ///   이동/조종을 나눌 이유가 없다 — 걷기와 크레인 조작이 항상 동시에 열려 있다.
    ///
    /// [매핑] D패드 좌우=트롤리, D패드 상하=갠트리, LT/RT=호이스트 하강/상승,
    ///        A=집기, B=놓기, X=운전실 시점, Y=HUD 토글.
    ///        키보드 폴백: J/L=트롤리, U/O=갠트리, K/I=호이스트, G=집기, H=놓기, C=운전실, F1=HUD.
    ///
    /// [물리 정합] 축 적분은 FixedUpdate 에서만 한다 — kinematic 화물을 끌고 가는 이동이라
    ///   SpreaderGrabber 의 통과방지 클램프(DefaultExecutionOrder 50)와 같은 박자여야 한다.
    ///   입력 샘플링은 Update, 운전실 시점 추종은 LateUpdate(한 프레임 어긋남 방지) — VR 쪽과 동일한 규약.
    /// </summary>
    [AddComponentMenu("Container/Flat Mode/Flat Crane Controller")]
    [DisallowMultipleComponent]
    public sealed class FlatCraneController : MonoBehaviour
    {
        [Header("속도 (실제 m/s — StsCraneVRController 와 같은 정격값)")]
        [Tooltip("트롤리 횡행 — 실제 STS 정격 ≈ 198 m/min = 3.3 m/s")]
        [SerializeField] float trolleySpeedMps = 3.3f;
        [Tooltip("호이스트 공하(빈 스프레더) ≈ 2.7 m/s")]
        [SerializeField] float hoistEmptySpeedMps = 2.7f;
        [Tooltip("호이스트 적재-경하중 ≈ 1.8 m/s")]
        [SerializeField] float hoistLoadedLightMps = 1.8f;
        [Tooltip("호이스트 적재-정격(무거운 컨테이너) ≈ 0.9 m/s")]
        [SerializeField] float hoistLoadedHeavyMps = 0.9f;
        [Tooltip("경하중 브레이크포인트(t) — 이하면 light 속도")]
        [SerializeField] float hoistLightLoadTons = 8f;
        [Tooltip("정격 브레이크포인트(t) — 이상이면 heavy 속도")]
        [SerializeField] float hoistRatedLoadTons = 32f;
        [Tooltip("갠트리 주행 — 실제 정격 ≈ 45 m/min = 0.75 m/s")]
        [SerializeField] float gantrySpeedMps = 0.75f;

        [Header("입력")]
        [Tooltip("트리거를 이만큼 눌러야 호이스트가 움직인다(0~1).")]
        [SerializeField, Range(0f, 0.5f)] float triggerDeadzone = 0.1f;
        [SerializeField] bool debugLog = true;

        [Header("운전실 시점 (X 버튼)")]
        [Tooltip("운전실 바닥 패널 '아래'로 눈을 내릴 거리(크레인 로컬 m, 스케일 무관) — 발밑 화물을 막힘없이 내려다보게.")]
        [SerializeField, Range(0f, 0.04f)] float cabFloorDropDown = 0.008f;

        StsCrane crane;
        SpreaderGrabber grabber;
        FlatPlayerRig rig;
        float nextFind;

        // Update 에서 샘플링한 축 입력(-1..1) — FixedUpdate 가 소비한다.
        float trolleyIn, gantryIn, hoistIn;

        // 운전실 시점 상태
        bool cabView;
        Transform cabAnchor;
        Vector3 savedRigPos, lastTrolleyPos;
        Quaternion savedRigRot;

        /// <summary>운전실 시점 활성 여부 — HUD 표시에 사용.</summary>
        public bool CabView => cabView;
        /// <summary>현재 조작 대상 크레인(없으면 null) — HUD가 축 상태를 읽을 때 사용.</summary>
        public StsCrane Crane => crane;

        void Update()
        {
            EnsureCrane();
            if (crane == null) { trolleyIn = gantryIn = hoistIn = 0f; return; }

            ReadAxes(out trolleyIn, out gantryIn, out hoistIn);
            ReadButtons();

            if (cabView && rig != null) rig.MovementLocked = true;
        }

        // 축 적분은 물리 고정틱에서 — 통과방지 클램프(order 50)와 같은 박자.
        void FixedUpdate()
        {
            if (crane == null) return;
            float dt = Time.fixedDeltaTime;
            float s = crane.ModelScale;   // 실척 m/s → 모델 단위 m/s

            IAxisMover trolley = crane.Trolley;
            if (trolley != null && Mathf.Abs(trolleyIn) > 1e-4f)
                trolley.MoveTo(trolley.Current + trolleyIn * trolleySpeedMps * s * dt);

            IAxisMover gantry = crane.Gantry;
            if (gantry != null && Mathf.Abs(gantryIn) > 1e-4f)
                gantry.MoveTo(gantry.Current + gantryIn * gantrySpeedMps * s * dt);

            IAxisMover hoist = crane.Spreader;
            if (hoist != null && Mathf.Abs(hoistIn) > 1e-4f)
                hoist.MoveTo(hoist.Current + hoistIn * HoistMps() * s * dt);
        }

        // 운전실 시점 추종 — 트롤리/갠트리 이동(FixedUpdate)이 끝난 뒤 정렬해 시점 저더를 막는다.
        void LateUpdate()
        {
            if (!cabView || crane == null || rig == null) return;
            var trolleyT = (crane.Trolley as Component)?.transform;
            if (trolleyT == null) return;
            rig.transform.position += trolleyT.position - lastTrolleyPos;
            lastTrolleyPos = trolleyT.position;
        }

        /// <summary>든 컨테이너 무게에 따른 권상 속도 — 빈 스프레더는 공하, 무거울수록 정격에 수렴(VR 쪽과 동일 산식).</summary>
        float HoistMps()
        {
            var attach = crane != null ? crane.Attach : null;
            if (attach == null || !attach.HasContainer) return hoistEmptySpeedMps;
            float t = Mathf.InverseLerp(hoistLightLoadTons, hoistRatedLoadTons, attach.AttachedLoadTons);
            return Mathf.Lerp(hoistLoadedLightMps, hoistLoadedHeavyMps, t);
        }

        // ── 입력 ────────────────────────────────────────────────────────────────────
        void ReadAxes(out float trolley, out float gantry, out float hoist)
        {
            trolley = gantry = hoist = 0f;

            var gp = Gamepad.current;
            if (gp != null)
            {
                if (gp.dpad.right.isPressed) trolley += 1f;
                if (gp.dpad.left.isPressed) trolley -= 1f;
                if (gp.dpad.up.isPressed) gantry += 1f;
                if (gp.dpad.down.isPressed) gantry -= 1f;

                float rt = gp.rightTrigger.ReadValue();
                float lt = gp.leftTrigger.ReadValue();
                if (rt > triggerDeadzone) hoist += Mathf.InverseLerp(triggerDeadzone, 1f, rt);   // 권상(올림)
                if (lt > triggerDeadzone) hoist -= Mathf.InverseLerp(triggerDeadzone, 1f, lt);   // 권하(내림)
            }

            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.lKey.isPressed) trolley += 1f;
            if (kb.jKey.isPressed) trolley -= 1f;
            if (kb.oKey.isPressed) gantry += 1f;
            if (kb.uKey.isPressed) gantry -= 1f;
            if (kb.iKey.isPressed) hoist += 1f;
            if (kb.kKey.isPressed) hoist -= 1f;

            trolley = Mathf.Clamp(trolley, -1f, 1f);
            gantry = Mathf.Clamp(gantry, -1f, 1f);
            hoist = Mathf.Clamp(hoist, -1f, 1f);
        }

        void ReadButtons()
        {
            var gp = Gamepad.current;
            var kb = Keyboard.current;

            bool grab = (gp != null && gp.buttonSouth.wasPressedThisFrame) || (kb != null && kb.gKey.wasPressedThisFrame);
            bool release = (gp != null && gp.buttonEast.wasPressedThisFrame) || (kb != null && kb.hKey.wasPressedThisFrame);
            bool cab = (gp != null && gp.buttonWest.wasPressedThisFrame) || (kb != null && kb.cKey.wasPressedThisFrame);
            bool hud = (gp != null && gp.buttonNorth.wasPressedThisFrame) || (kb != null && kb.f1Key.wasPressedThisFrame);

            if (grab) { if (debugLog) Debug.Log("[FlatCrane] 집기(Grab)"); grabber?.Grab(); }
            if (release) { if (debugLog) Debug.Log("[FlatCrane] 놓기(Release)"); grabber?.Release(); }
            if (cab) { if (cabView) ExitCabView(); else EnterCabView(); }
            if (hud) FindAnyObjectByType<FlatHud>()?.Toggle();
        }

        // ── 크레인/리그 탐색 ────────────────────────────────────────────────────────
        // 크레인은 에디터 메뉴로 나중에 생성될 수 있어, 못 찾으면 0.5s마다만 재시도한다(매 프레임 풀 씬 스캔 방지).
        void EnsureCrane()
        {
            if (rig == null) rig = FindAnyObjectByType<FlatPlayerRig>();
            if (crane != null) { if (grabber == null) grabber = crane.GetComponent<SpreaderGrabber>(); return; }
            if (Time.unscaledTime < nextFind) return;
            nextFind = Time.unscaledTime + 0.5f;
            crane = FindAnyObjectByType<StsCrane>();
            if (crane != null)
            {
                grabber = crane.GetComponent<SpreaderGrabber>();
                if (debugLog) Debug.Log($"[FlatCrane] 크레인 '{crane.name}' 연결 — 게임패드 조작 활성.");
            }
        }

        // ── 운전실 시점 ─────────────────────────────────────────────────────────────
        // VR 쪽 EnterCabView 와 같은 기준을 쓴다: 시선은 Cab_Viewpoint, 눈 위치는 운전실 후방 바닥 패널 '아래'.
        //   좌석 눈높이는 바닥/콘솔에 가려 발밑 화물이 안 보이므로, 바닥 밑에서 전면 경사창으로 내려다보게 한다.
        void EnterCabView()
        {
            var trolleyT = (crane?.Trolley as Component)?.transform;
            var cam = rig != null ? rig.Cam : null;
            if (trolleyT == null || cam == null)
            {
                if (debugLog) Debug.LogWarning("[FlatCrane] 운전실 시점 실패 — 트롤리 또는 평면 카메라를 못 찾음.");
                return;
            }

            cabAnchor = FindByBaseName(trolleyT, StsPartNames.CabViewpoint) ?? trolleyT;
            Transform cabFloor = FindByBaseName(trolleyT, StsPartNames.CabFloorRear);

            Transform rigT = rig.transform;
            savedRigPos = rigT.position;
            savedRigRot = rigT.rotation;

            // 시선(요)을 운전실 전방(스프레더/바다쪽)에 정렬 — 상하 피치는 사용자 스틱에 맡긴다.
            Vector3 fwd = cabAnchor.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude > 1e-4f) rigT.rotation = Quaternion.LookRotation(fwd.normalized, Vector3.up);

            Vector3 target = cabFloor != null
                ? cabFloor.position - trolleyT.up * cabFloorDropDown   // 바닥 패널 '아래'
                : cabAnchor.position;                                   // 폴백 — 좌석 눈높이
            rigT.position += target - cam.transform.position;           // (회전 후) 카메라를 시점으로 정렬

            lastTrolleyPos = trolleyT.position;
            cabView = true;
            rig.MovementLocked = true;

            if (debugLog)
                Debug.Log($"[FlatCrane] 운전실 시점 ON — 시선기준 '{cabAnchor.name}', " +
                          $"눈위치 '{(cabFloor != null ? cabFloor.name : cabAnchor.name)}'. 다시 X를 누르면 원위치.");
            QaLog.Info("FLAT", "cabview", $"on=true anchor={cabAnchor.name} floor={(cabFloor != null ? cabFloor.name : "none")}");
        }

        void ExitCabView()
        {
            if (rig != null)
            {
                rig.transform.SetPositionAndRotation(savedRigPos, savedRigRot);
                rig.MovementLocked = false;
            }
            cabView = false;
            if (debugLog) Debug.Log("[FlatCrane] 운전실 시점 OFF (원위치 복귀)");
            QaLog.Info("FLAT", "cabview", "on=false");
        }

        /// <summary>부품 이름(번호 접미사 제외)으로 자식에서 찾기 — 생성기가 붙이는 _1,_2 접미사를 CraneHud.BaseName 으로 벗긴다.</summary>
        static Transform FindByBaseName(Transform root, string baseName)
        {
            if (string.IsNullOrEmpty(baseName)) return null;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (CraneHud.BaseName(t.name) == baseName) return t;
            return null;
        }
    }
}
