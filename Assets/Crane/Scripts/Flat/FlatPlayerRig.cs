using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace AIXRCrane.Crane.Flat
{
    /// <summary>평면 모드 플레이어 리그 — VR XR Origin 대신(게임패드 이동·시선, 키보드 폴백). 리그가 1/24로 축소되므로
    /// 미터값은 실척 그대로 로컬에 넣고 속도엔 lossyScale(리그 스케일)을 곱한다 — 상수로 박으면 축소 온/오프에 따라 어긋난다.</summary>
    [AddComponentMenu("AI-XR Crane/Flat Mode/Flat Player Rig")]
    [DisallowMultipleComponent]
    public sealed class FlatPlayerRig : MonoBehaviour
    {
        [Header("시점 (실척 m — 리그 로컬에 그대로 넣는다)")]
        [Tooltip("눈높이(실척 m). 리그가 1/24로 축소되므로 월드에선 1.65/24 ≈ 0.069 가 된다.")]
        [SerializeField] float eyeHeightMeters = 1.65f;
        [Tooltip("카메라 near clip(월드). 미니어처라 아주 작아야 손앞 부재가 안 잘린다.")]
        [SerializeField] float nearClip = 0.01f;
        [Tooltip("카메라 수직 시야각(도). XREAL One Pro는 대각 57°라 화면이 좁으므로 과하게 넓히지 않는다.")]
        [SerializeField] float fieldOfView = 60f;

        [Header("속도 (실척 m/s · deg/s)")]
        [Tooltip("걷기 속도(실척 m/s). VR 쪽 walkSpeed(8)와 같은 체감으로 맞춤.")]
        [SerializeField] float walkSpeedMps = 8f;
        [Tooltip("가속(왼쪽 스틱 누름 / Shift) 배율.")]
        [SerializeField] float sprintMultiplier = 3f;
        [Tooltip("상하 이동(LB/RB · Q/E) 속도(실척 m/s). 크레인 상부를 올려다보러 띄울 때 쓴다.")]
        [SerializeField] float verticalSpeedMps = 8f;
        [Tooltip("시선 회전 속도(도/초, 스틱 최대 시).")]
        [SerializeField] float lookDegPerSec = 140f;

        [Header("입력")]
        [SerializeField, Range(0f, 0.5f)] float deadzone = 0.15f;
        [Tooltip("오른쪽 스틱 상하 반전(비행 시뮬 방식).")]
        [SerializeField] bool invertPitch = false;
        [SerializeField] bool debugLog = true;

        [Header("공간 마우스(모바일 관전) — 커서 하나 + 누르기")]
        [Tooltip("누르고 있을 때 커서가 가리키는 쪽으로 나는 속도(실척 m/s). 선석 344 m 를 17초쯤에 건넌다.")]
        [SerializeField] float flySpeedMps = 20f;
        [Tooltip("가장자리 회전 띠(화면 비율). 커서가 이 띠 안에 있으면 그쪽으로 고개를 돌린다.")]
        [SerializeField, Range(0.02f, 0.25f)] float edgeBand = 0.08f;
        [Tooltip("가장자리 회전 속도(도/초) — 띠 맨 끝에서 최대.")]
        [SerializeField] float edgeTurnDegPerSec = 60f;

        Camera cam;
        float yaw, pitch;
        float appliedYaw = float.NaN;   // 내가 마지막으로 적용한 yaw — 외부(StartPlacer)가 돌렸는지 판별용

        /// <summary>평면 모드 카메라. 부트스트랩·HUD·크레인 컨트롤러가 참조.
        /// (프로퍼티명을 타입명 Camera 와 겹치지 않게 Cam 으로 둔다 — 제네릭/타입 문맥에서의 혼동 방지.)</summary>
        public Camera Cam => cam;

        /// <summary>true면 수평/수직 이동 입력을 무시한다(운전실 시점 등에서 위치를 남이 잡을 때).</summary>
        public bool MovementLocked { get; set; }

        /// <summary>모바일 관전 — Beam Pro '공간 마우스'(커서 하나 + 누르기)로 돌아다닌다. 부트스트랩이 모바일일 때만 켠다.
        ///   PC 평면 모드에서 켜면 IMGUI·HUD 를 누르려던 클릭이 비행이 되므로 모바일 전용이다.</summary>
        public bool PointerNav { get; set; }

        bool wasPressed, pressOnUi;                 // 누름이 버튼 위에서 시작됐으면 뗄 때까지 날지 않는다
        bool glide; float glideT, glideDur;         // 시점 버튼 비행
        Vector3 glideFrom, glideTo;
        float yawFrom, yawTo, pitchFrom, pitchTo;

        void Awake()
        {
            // 카메라를 자식으로 — 루트(this)는 CranePlayerStartPlacer·CranePlayerRigScale 이 잡는 '리그'가 된다.
            //   (그쪽은 Camera.main.transform.root 로 리그를 찾는다 → 카메라가 반드시 이 오브젝트의 자식이어야 함)
            var camGo = new GameObject("FlatCamera") { tag = "MainCamera" };
            camGo.transform.SetParent(transform, worldPositionStays: false);
            camGo.transform.localPosition = new Vector3(0f, eyeHeightMeters, 0f);   // 실척 m — 리그 축소로 월드에선 1/24
            camGo.transform.localRotation = Quaternion.identity;

            cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = nearClip;
            cam.fieldOfView = fieldOfView;
            camGo.AddComponent<AudioListener>();

            yaw = transform.eulerAngles.y;

            if (debugLog)
                Debug.Log($"[FlatRig] 생성 — 눈높이 {eyeHeightMeters}m(리그 로컬), near {nearClip}, FOV {fieldOfView}°.");
        }

        void Update()
        {
            float dt = Time.deltaTime;

            // 리그 스케일 실측 — CranePlayerRigScale이 1/24를 먹였으면 그 값, 축소가 꺼져 있으면 1.
            //   실척 속도에 이 값을 곱해야 '모델 안에서 실제로 그 속도로 걷는' 체감이 된다.
            float s = Mathf.Max(transform.lossyScale.x, 1e-6f);

            // ── 시선: yaw 는 리그 루트, pitch 는 카메라 로컬 ──
            // 외부(CranePlayerStartPlacer)가 리그를 돌렸으면 내 yaw 를 맞춘다 — 안 맞추면 시작 방향이 튕긴다.
            float curYaw = transform.eulerAngles.y;
            if (float.IsNaN(appliedYaw) || Mathf.Abs(Mathf.DeltaAngle(curYaw, appliedYaw)) > 0.01f) yaw = curYaw;

            if (glide) { StepGlide(dt); ApplyLook(); return; }   // 시점 버튼 비행 중엔 다른 입력을 안 받는다

            ReadInput(out Vector2 move, out Vector2 look, out float vertical, out bool sprint);
            Vector3 flyDir = PointerNav ? PointerStep(dt) : Vector3.zero;

            yaw += look.x * lookDegPerSec * dt;
            pitch += (invertPitch ? look.y : -look.y) * lookDegPerSec * dt;
            pitch = Mathf.Clamp(pitch, -89f, 89f);
            ApplyLook();

            if (MovementLocked) return;

            // 공간 마우스 비행 — 바닥 아래·부두 밖으로 나가는 건 CranePlayerStartPlacer 가 이미 잡아 준다(여기서 또 막지 않는다).
            if (flyDir != Vector3.zero) transform.position += flyDir * (flySpeedMps * s * dt);

            // ── 이동: 카메라 yaw 기준 전후좌우 + 수직 ───────────────────────────────────
            if (move.sqrMagnitude > 1e-6f || Mathf.Abs(vertical) > 1e-6f)
            {
                float speed = walkSpeedMps * (sprint ? sprintMultiplier : 1f);
                Vector3 fwd = transform.forward, right = transform.right;
                Vector3 delta = (fwd * move.y + right * move.x) * (speed * s * dt)
                              + Vector3.up * (vertical * verticalSpeedMps * s * dt);
                transform.position += delta;
            }
        }

        void ApplyLook()
        {
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            appliedYaw = yaw;
            if (cam != null) cam.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        /// <summary>시점 버튼 — 카메라 눈이 eye 에 와서 lookAt 을 보도록 부드럽게 옮긴다.
        ///   루트는 발밑이고 눈은 그 위 eyeHeight(실척)×리그 축척이라, 루트 목표 = eye − 위×그 높이.</summary>
        public void FlyTo(Vector3 eye, Vector3 lookAt, float seconds)
        {
            float s = Mathf.Max(transform.lossyScale.x, 1e-6f);
            glideFrom = transform.position;
            glideTo = eye - Vector3.up * (eyeHeightMeters * s);
            Vector3 d = lookAt - eye;
            yawFrom = yaw;     yawTo = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            pitchFrom = pitch; pitchTo = Mathf.Clamp(-Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg, -89f, 89f);
            glideT = 0f; glideDur = Mathf.Max(0.01f, seconds); glide = true;
        }

        void StepGlide(float dt)
        {
            glideT = Mathf.Min(1f, glideT + dt / glideDur);
            float k = Mathf.SmoothStep(0f, 1f, glideT);
            transform.position = Vector3.Lerp(glideFrom, glideTo, k);
            yaw = Mathf.LerpAngle(yawFrom, yawTo, k);
            pitch = Mathf.Lerp(pitchFrom, pitchTo, k);
            if (glideT >= 1f) glide = false;
        }

        // 공간 마우스 한 프레임 — 가장자리 회전은 여기서 yaw/pitch 에 바로 더하고, 비행 방향(없으면 0)을 돌려준다.
        Vector3 PointerStep(float dt)
        {
            var p = Pointer.current;
            if (p == null || cam == null || !Application.isFocused) return Vector3.zero;

            Vector2 pos = p.position.ReadValue();
            bool pressed = p.press.isPressed;
            bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            if (pressed && !wasPressed) pressOnUi = overUi;
            wasPressed = pressed;

            // 화면 크기는 UnityEngine.Device 로 — 에디터 휴대폰 시뮬레이터의 포인터 좌표는 가상 기기 화면 기준이라
            //   UnityEngine.Screen(에디터 창 크기)으로 나누면 가장자리 판정이 어긋난다. 빌드에선 둘이 같다.
            var uv = new Vector2(pos.x / Mathf.Max(1, UnityEngine.Device.Screen.width), pos.y / Mathf.Max(1, UnityEngine.Device.Screen.height));
            // 마우스만 호버가 있다(Moonlight→서버는 마우스로 들어온다). 터치는 손을 떼도 마지막 자리가 남아
            //   가장자리에서 뗀 순간부터 영원히 돌게 되므로, 터치는 누르고 있을 때만 가장자리 회전을 받는다.
            var (turnYaw, turnPitch, fly) = PointerIntent(uv, pressed, overUi || (pressed && pressOnUi), p is Mouse, edgeBand);
            yaw += turnYaw * edgeTurnDegPerSec * dt;
            pitch -= turnPitch * edgeTurnDegPerSec * dt;   // pitch 양수 = 아래를 본다
            return fly ? cam.ScreenPointToRay(pos).direction : Vector3.zero;
        }

        /// <summary>공간 마우스 의도 — 순수 함수. 커서(화면 0~1)·누름·UI 위·호버 가능 → 회전(−1~1, 위·오른쪽 양수)과 비행 여부.
        ///   가장자리 띠에서는 돌기만 하고 날지 않는다 — 돌리려고 가장자리를 누르다 옆으로 날아가지 않게.</summary>
        public static (float yaw, float pitch, bool fly) PointerIntent(Vector2 uv, bool pressed, bool blocked, bool canHover, float band)
        {
            if (blocked || uv.x < 0f || uv.x > 1f || uv.y < 0f || uv.y > 1f) return (0f, 0f, false);
            if (!pressed && !canHover) return (0f, 0f, false);
            float yaw = Edge(uv.x, band), pitch = Edge(uv.y, band);
            return (yaw, pitch, pressed && yaw == 0f && pitch == 0f);
        }

        static float Edge(float t, float band)
            => t < band ? -Mathf.InverseLerp(band, 0f, t) : t > 1f - band ? Mathf.InverseLerp(1f - band, 1f, t) : 0f;

        /// <summary><see cref="PointerIntent"/> 자체 검사 — 모바일 부팅 QA 가 부른다. 실패하면 어느 경우인지 돌려준다.</summary>
        public static bool PointerIntentSelfCheck(out string failed)
        {
            const float b = 0.08f;
            var cases = new (string name, Vector2 uv, bool pressed, bool blocked, bool hover, float yaw, float pitch, bool fly)[]
            {
                ("가운데 누름=비행",        new Vector2(0.5f, 0.5f),  true,  false, true,   0f,  0f, true),
                ("가운데 호버=정지",        new Vector2(0.5f, 0.5f),  false, false, true,   0f,  0f, false),
                ("왼끝 호버=왼쪽 회전",     new Vector2(0f, 0.5f),    false, false, true,  -1f,  0f, false),
                ("오른끝 누름=회전만",      new Vector2(1f, 0.5f),    true,  false, true,   1f,  0f, false),
                ("위끝 호버=위로",          new Vector2(0.5f, 1f),    false, false, true,   0f,  1f, false),
                ("버튼 위 누름=무시",       new Vector2(0.5f, 0.5f),  true,  true,  true,   0f,  0f, false),
                ("터치 뗀 뒤 가장자리=정지", new Vector2(0f, 0.5f),    false, false, false,  0f,  0f, false),
                ("화면 밖=정지",            new Vector2(1.2f, 0.5f),  false, false, true,   0f,  0f, false),
            };
            foreach (var c in cases)
            {
                var r = PointerIntent(c.uv, c.pressed, c.blocked, c.hover, b);
                if (Mathf.Abs(r.yaw - c.yaw) > 1e-4f || Mathf.Abs(r.pitch - c.pitch) > 1e-4f || r.fly != c.fly)
                { failed = $"{c.name} → yaw {r.yaw} pitch {r.pitch} fly {r.fly}"; return false; }
            }
            failed = $"{cases.Length}건 통과";
            return true;
        }

        // 게임패드 우선, 없으면 키보드 폴백(PC 검증용). 새 Input System 전용 프로젝트라 UnityEngine.Input 은 못 쓴다.
        void ReadInput(out Vector2 move, out Vector2 look, out float vertical, out bool sprint)
        {
            move = Vector2.zero; look = Vector2.zero; vertical = 0f; sprint = false;

            var gp = Gamepad.current;
            if (gp != null)
            {
                move = Deadzone(gp.leftStick.ReadValue());
                look = Deadzone(gp.rightStick.ReadValue());
                if (gp.rightShoulder.isPressed) vertical += 1f;
                if (gp.leftShoulder.isPressed) vertical -= 1f;
                sprint = gp.leftStickButton.isPressed;
            }

            var kb = Keyboard.current;
            if (kb == null) return;

            Vector2 kMove = Vector2.zero;
            if (kb.wKey.isPressed) kMove.y += 1f;
            if (kb.sKey.isPressed) kMove.y -= 1f;
            if (kb.dKey.isPressed) kMove.x += 1f;
            if (kb.aKey.isPressed) kMove.x -= 1f;
            if (kMove.sqrMagnitude > 1e-6f) move = Vector2.ClampMagnitude(kMove, 1f);

            Vector2 kLook = Vector2.zero;
            if (kb.rightArrowKey.isPressed) kLook.x += 1f;
            if (kb.leftArrowKey.isPressed) kLook.x -= 1f;
            if (kb.upArrowKey.isPressed) kLook.y += 1f;
            if (kb.downArrowKey.isPressed) kLook.y -= 1f;
            if (kLook.sqrMagnitude > 1e-6f) look = Vector2.ClampMagnitude(kLook, 1f);

            if (kb.eKey.isPressed) vertical += 1f;
            if (kb.qKey.isPressed) vertical -= 1f;
            if (kb.leftShiftKey.isPressed) sprint = true;
        }

        Vector2 Deadzone(Vector2 v)
        {
            // 축별 컷이 아니라 '크기' 기준 — 대각 입력에서 축 하나가 잘려 방향이 꺾이는 것을 막는다.
            float m = v.magnitude;
            if (m < deadzone) return Vector2.zero;
            return v.normalized * Mathf.InverseLerp(deadzone, 1f, m);
        }
    }
}
