using UnityEngine;
using UnityEngine.InputSystem;

namespace Container.Crane.Flat
{
    /// <summary>
    /// 평면 모드 플레이어 리그 — VR의 XR Origin 자리를 대신한다(게임패드 이동·시선).
    ///
    /// ★ 스케일 규약(중요): 리그 루트는 CranePlayerRigScale 이 매 프레임 1/24(StsConfig.ModelScale)로
    ///   축소한다. 그래서 이 스크립트의 '미터' 값들은 전부 **실척 미터를 리그 로컬에 그대로** 넣는다:
    ///     · 눈높이 1.65 를 카메라 localPosition.y 에 넣으면 → 월드 1.65/24 ≈ 0.069 (미니어처 기준 정확)
    ///     · 이동은 월드 변위이므로 속도에 리그 스케일을 곱한다(lossyScale 실측 — 축소가 꺼져 있어도 자동 대응).
    ///   스케일을 코드에 상수로 박지 말 것. 리그 축소가 켜졌는지/꺼졌는지에 따라 24배가 어긋난다.
    ///
    /// [조작] 왼쪽 스틱 = 이동, 오른쪽 스틱 = 시선, LB/RB = 하강/상승, 왼쪽 스틱 누름 = 가속.
    ///   게임패드가 없을 때를 대비해 키보드 폴백(WASD·방향키·Q/E·Shift)도 둔다 — PC 검증용.
    /// </summary>
    [AddComponentMenu("Container/Flat Mode/Flat Player Rig")]
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

        Camera cam;
        float yaw, pitch;
        float appliedYaw = float.NaN;   // 내가 마지막으로 적용한 yaw — 외부(StartPlacer)가 돌렸는지 판별용

        /// <summary>평면 모드 카메라. 부트스트랩·HUD·크레인 컨트롤러가 참조.
        /// (프로퍼티명을 타입명 Camera 와 겹치지 않게 Cam 으로 둔다 — 제네릭/타입 문맥에서의 혼동 방지.)</summary>
        public Camera Cam => cam;

        /// <summary>true면 수평/수직 이동 입력을 무시한다(운전실 시점 등에서 위치를 남이 잡을 때).</summary>
        public bool MovementLocked { get; set; }

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

            ReadInput(out Vector2 move, out Vector2 look, out float vertical, out bool sprint);

            // ── 시선: yaw 는 리그 루트, pitch 는 카메라 로컬 ────────────────────────────
            // 외부(CranePlayerStartPlacer)가 리그를 회전시켰으면 내 yaw 를 그쪽에 맞춘다.
            //   안 맞추면 다음 프레임에 내가 옛 yaw 로 되돌려 '시작 방향이 튕기는' 증상이 난다.
            float curYaw = transform.eulerAngles.y;
            if (float.IsNaN(appliedYaw) || Mathf.Abs(Mathf.DeltaAngle(curYaw, appliedYaw)) > 0.01f) yaw = curYaw;

            yaw += look.x * lookDegPerSec * dt;
            pitch += (invertPitch ? look.y : -look.y) * lookDegPerSec * dt;
            pitch = Mathf.Clamp(pitch, -89f, 89f);

            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            appliedYaw = yaw;
            if (cam != null) cam.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);

            if (MovementLocked) return;

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
