using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace Container.Crane.Sts
{
    /// <summary>
    /// STS 크레인 상태를 VR HMD 시야 우상단에 고정 표시하는 world-space HUD.
    ///   - Canvas/Background/TMP 텍스트를 코드로 자동 생성
    ///   - HMD 카메라(Camera.main)에 자식으로 붙여 머리를 돌려도 같은 위치에 따라옴(head-locked)
    ///   - 매 프레임 StsCrane(트롤리/호이스트/갠트리, 적재 컨테이너) 상태를 텍스트로 업데이트
    /// 씬 어디든 한 곳에 컴포넌트 붙이면 됨. crane을 비워두면 씬에서 자동 탐색.
    /// </summary>
    [AddComponentMenu("Container/STS Crane/Crane Status HUD")]
    [DisallowMultipleComponent]
    public sealed class CraneStatusHUD : MonoBehaviour
    {
        [Header("참조")]
        [SerializeField] StsCrane crane;
        [Tooltip("이 카메라에 자식으로 붙음. 비우면 Camera.main 자동 사용(HMD가 MainCamera로 태그된 경우 OK).")]
        [SerializeField] Camera targetCamera;

        [Header("HMD 우상단 위치 (카메라 로컬 좌표, m)")]
        [SerializeField] Vector3 hmdOffset = new Vector3(0.32f, 0.20f, 0.8f);   // x=오른쪽, y=위, z=앞
        [Tooltip("카메라 정면을 기준으로 약간 안쪽으로 기울이기(편안한 시야각). 0이면 정면.")]
        [SerializeField, Range(-30f, 30f)] float tiltYawDeg = -15f;
        [SerializeField, Range(-30f, 30f)] float tiltPitchDeg = 8f;

        [Header("패널/텍스트")]
        [SerializeField] Vector2 panelPixels = new Vector2(500f, 240f);   // fitToText 사용 시 무시(글자에 맞춰 자동)
        [SerializeField] float worldScale = 0.00075f;   // 줌 축소(1 px ≈ 0.75 mm)
        [SerializeField] Color bgColor = new Color(0f, 0f, 0f, 0.85f);
        [SerializeField] Color textColor = Color.white;
        [SerializeField] int fontSize = 18;

        Canvas canvas;
        Text text;                          // 한글 지원 위해 legacy UI.Text + 시스템 폰트 동적 로드
        StsCraneVRController controller;    // 운전모드 여부 판단(이 모드일 때만 HUD 표시)
        SpreaderLockAnimator lockAnim;      // 트위스트락 잠금 상태(적재 표시 보강용)
        readonly StringBuilder sb = new StringBuilder(512);
        string lastText;          // 직전 표시 문자열 — 바뀔 때만 Text.text 대입(캔버스 리빌드 절감)
        float nextTextRefresh;    // 다음 텍스트 갱신 시각(CraneHud.TextHz 스로틀 — 매 프레임 문자열 생성/GC 방지)

        // ─── 속도 측정 ───
        // 크레인이 실척의 1/24로 생성됨(StsCraneCreator.Scale=1/24). 모델 속도(units/s)를 ÷Scale 하면 실척 m/s.
        // 축척은 StsCrane.ModelScale 단일 소스 참조(모델 units/s ÷ ModelScale = 실척 m/s)
        float prevTrolley, prevHoist, prevGantry;   // 직전 프레임 위치(모델 units)
        float spdTrolley, spdHoist, spdGantry;       // 평활된 현재 속도(실척 m/min)
        bool speedPrimed;                            // 첫 프레임 위치 초기화 여부(초기 튐 방지)

        // 씬 로드 시 자동 스폰 — 컴포넌트 수동 부착 안 해도 동작. 이미 인스턴스가 있으면(수동 설정) 스킵.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoSpawn() => CraneHud.EnsureSpawned<CraneStatusHUD>("HUD");

        void Start()
        {
            if (crane == null) crane = FindAnyObjectByType<StsCrane>();
            if (crane != null)
            {
                var spreaderT = (crane.Spreader as Component)?.transform;
                if (spreaderT != null) lockAnim = spreaderT.GetComponent<SpreaderLockAnimator>();
            }
            if (lockAnim == null) lockAnim = FindAnyObjectByType<SpreaderLockAnimator>();
            BuildCanvas();
            TryAttachToCamera();
        }

        void LateUpdate()
        {
            if (canvas == null || text == null) return;

            // 운전모드일 때만 표시 — 컨트롤러의 CraneMode를 따른다(걷기/시점변경 모드면 숨김).
            //   컨트롤러를 못 찾으면(비VR/테스트 씬) 항상 표시(기존 동작 유지).
            if (controller == null) controller = FindController();
            // 관전자(순수 클라이언트)에겐 숨김 — 조종을 못 하니 운전 상태 출력이 무의미(ModeHUD/ArrowHUD와 동일 기준).
            var nm = Unity.Netcode.NetworkManager.Singleton;
            bool show = (nm == null || nm.IsServer) && (controller == null || controller.CraneMode);
            canvas.enabled = show;
            if (!show) { speedPrimed = false; return; }   // 숨길 땐 갱신 스킵 + 재표시 시 속도 재초기화

            // 카메라 부착 안 됐으면 재시도(XR Rig 초기화가 늦는 경우). 빌보드 회전은 부착 시 1회만 설정한다
            //   — 캔버스가 카메라 자식이라 카메라를 향하는 로컬 회전은 매 프레임 동일(상수)이므로 재계산 불필요.
            if (canvas.transform.parent == null || canvas.transform.parent == transform)
                TryAttachToCamera();

            UpdateSpeeds();   // 속도 평활은 매 프레임(정확도) — 텍스트 생성/대입만 스로틀.
            if (CraneHud.Due(ref nextTextRefresh, CraneHud.TextHz))
                CraneHud.SetTextIfChanged(text, ref lastText, BuildText());
        }

        // ───────── 축별 현재 속도 측정(실척 m/min) ─────────
        void UpdateSpeeds()
        {
            if (crane == null) { speedPrimed = false; return; }
            float dt = Time.deltaTime;
            if (dt < 1e-5f) return;

            // 첫 프레임은 위치만 잡아두고 속도 계산 스킵(0→현재 위치 큰 점프 방지)
            if (!speedPrimed)
            {
                prevTrolley = Cur(crane.Trolley);
                prevHoist = Cur(crane.Spreader);
                prevGantry = Cur(crane.Gantry);
                speedPrimed = true;
                return;
            }

            UpdateAxisSpeed(crane.Trolley, ref prevTrolley, ref spdTrolley, dt);
            UpdateAxisSpeed(crane.Spreader, ref prevHoist, ref spdHoist, dt);
            UpdateAxisSpeed(crane.Gantry, ref prevGantry, ref spdGantry, dt);
        }

        static float Cur(IAxisMover m) => m != null ? m.Current : 0f;

        // 운전모드를 알려줄 VR 컨트롤러 탐색 — 크레인에 붙어 있음(RequireComponent). 없으면 씬 전체 탐색.
        StsCraneVRController FindController()
        {
            if (crane != null)
            {
                var c = crane.GetComponent<StsCraneVRController>();
                if (c != null) return c;
            }
            return FindAnyObjectByType<StsCraneVRController>();
        }

        void UpdateAxisSpeed(IAxisMover m, ref float prev, ref float spd, float dt)
        {
            if (m == null) { spd = 0f; return; }
            float cur = m.Current;
            float vModel = Mathf.Abs(cur - prev) / dt;     // 모델 units/s
            float vRealMpm = vModel / crane.ModelScale * 60f;   // 실척 m/min (÷Scale=×24, ×60=분당)
            // 지수 평활(약 0.15s 시상수) — 프레임 노이즈로 숫자가 튀지 않게
            spd = Mathf.Lerp(spd, vRealMpm, 1f - Mathf.Exp(-dt / 0.15f));
            prev = cur;
        }

        // ───────── HMD 카메라 부착(head-locked) ─────────
        void TryAttachToCamera()
        {
            if (canvas == null) return;
            var cam = targetCamera != null ? targetCamera : Camera.main;
            if (cam == null) cam = FindHMDCamera();
            if (cam == null)
            {
                if (Time.frameCount % 60 == 0)   // 매 60프레임 한 번씩만 경고(스팸 방지)
                    Debug.LogWarning("[HUD] HMD/Main 카메라를 못 찾음 — XR Origin 활성/카메라 MainCamera 태그 확인.");
                return;
            }
            canvas.transform.SetParent(cam.transform, worldPositionStays: false);
            canvas.transform.localPosition = hmdOffset;
            CraneHud.FaceCameraChild(canvas.transform, hmdOffset, tiltPitchDeg, tiltYawDeg);   // 카메라 향함 — 부착 시 1회

            Debug.Log($"[HUD] '{cam.name}'(stereoEnabled={cam.stereoEnabled}, MainCamera tag={cam.CompareTag("MainCamera")}) 부착 완료. " +
                      $"localPos={hmdOffset}, 월드={canvas.transform.position:F2}, 카메라 월드={cam.transform.position:F2}");
        }

        // Camera.main 실패 시 XR(헤드셋) 카메라 후보 탐색
        static Camera FindHMDCamera()
        {
            // stereoEnabled(스테레오 렌더링 중)인 카메라 우선 = HMD
            foreach (var c in Camera.allCameras)
                if (c != null && c.stereoEnabled) return c;
            // TrackedPoseDriver(XR 위치 트래킹) 붙은 카메라
            foreach (var c in Camera.allCameras)
                if (c != null && (c.GetComponent("TrackedPoseDriver") != null)) return c;
            // 마지막 폴백: 씬의 첫 카메라
            return Camera.allCameras.Length > 0 ? Camera.allCameras[0] : null;
        }

        // ───────── Canvas/배경/텍스트 자동 생성 ─────────
        void BuildCanvas()
        {
            // fitToText: 배경이 글자 분량에 맞춰 자동 축소(빈 여백 제거). inset이 글자~배경 여백(padding)이 됨.
            canvas = CraneHud.BuildPanel(transform, "CraneStatusCanvas", panelPixels, worldScale,
                bgColor, fontSize, textColor, TextAnchor.UpperLeft, new Vector2(12, 10), out text, fitToText: true);
            text.text = "...";
        }

        // ───────── 상태 텍스트 ─────────
        string BuildText()
        {
            sb.Clear();
            sb.AppendLine("<b><size=22>STS 크레인 상태</size></b>");
            sb.AppendLine();
            if (crane == null)
            {
                sb.AppendLine("<color=#FF6666>StsCrane 없음</color>");
                return sb.ToString();
            }
            AppendAxis("트롤리   ", crane.Trolley, spdTrolley);
            AppendAxis("호이스트 ", crane.Spreader, spdHoist);
            AppendAxis("갠트리   ", crane.Gantry, spdGantry);
            sb.AppendLine();

            var attach = crane.Attach;
            bool has = attach != null && attach.HasContainer;
            sb.Append("적재     ");
            if (has)
            {
                sb.Append($"<color=#7FFF7F>{attach.AttachedContainer.name}</color>");
                float t = attach.AttachedMassKg / 1000f;
                if (t > 0.05f) sb.Append($"  <color=#FFD25F>{t:0.#} t</color>");   // 하중(t) — 질량 있을 때만
            }
            else sb.Append("<color=#888888>없음</color>");
            sb.AppendLine();

            // 잠금(트위스트락) — 애니메이터가 있으면 지령 상태, 없으면 적재 여부로 추정
            sb.Append("잠금     ");
            bool locked = lockAnim != null ? lockAnim.Locked : has;
            sb.AppendLine(locked ? "<color=#7FFF7F>OK (체결)</color>" : "<color=#FF6666>해제</color>");

            // 운전실 시점(A 토글) 활성 시에만 한 줄 표시
            if (controller != null && controller.CabView)
                sb.AppendLine("<color=#5FE0FF>● 운전실 시점</color>");
            return sb.ToString();
        }

        void AppendAxis(string label, IAxisMover m, float speedMpm)
        {
            sb.Append(label);
            if (m == null) { sb.AppendLine("<color=#888888>(none)</color>"); return; }
            float r = m.Max - m.Min;
            float t = r > 1e-6f ? (m.Current - m.Min) / r : 0f;
            int pct = Mathf.Clamp(Mathf.RoundToInt(t * 100f), 0, 100);
            int bars = Mathf.Clamp(Mathf.RoundToInt(t * 10f), 0, 10);
            sb.Append(pct.ToString("D3"));
            sb.Append("% [");
            for (int i = 0; i < bars; i++) sb.Append('█');
            for (int i = bars; i < 10; i++) sb.Append('░');
            sb.Append("] ");
            // 현재 속도(실척 m/min) — 멈춰 있으면 회색, 움직이면 청록 강조
            int mpm = Mathf.RoundToInt(speedMpm);
            if (mpm > 0) sb.Append($"<color=#5FE0FF>{mpm,3} m/min</color>");
            else sb.Append("<color=#888888>  0 m/min</color>");
            sb.AppendLine();
        }
    }
}
