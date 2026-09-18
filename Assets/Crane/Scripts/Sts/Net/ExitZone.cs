using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.XR;

namespace AIXRCrane.Crane.Sts.Net
{
    /// <summary>
    /// 부두 모서리 바닥의 **나가는 존** — 밟고 잠깐 서 있으면 세션을 끊고 시작 메뉴로 돌아간다.
    ///
    /// <para><b>왜 필요한가</b> — 오너 2026-09-16 "호스트로 접속했다가 그냥 앱을 끄면 호스트 세션이 그대로 남아
    /// 호스트가 접속이 안 된다". 서버에서는 크레인 5대가 <b>한 머신에서 같은 포트(7777)를 공유</b>하고,
    /// 먼저 호스트를 누른 인스턴스가 그 포트를 쥔다(<see cref="NetLanUI"/>.StartHost 주석 참조).
    /// 헤드셋을 벗어도 서버의 AI-XR-Crane.exe 는 24시간 계속 살아 있어 포트를 놓지 않는다 → 다음 호스트는 반드시 실패한다.
    /// VR 에는 나갈 길이 아예 없었다: <see cref="NetLanUI"/> 의 '연결 끊기'는 IMGUI 라 헤드셋에 안 그려지고,
    /// 시작 메뉴(<see cref="CraneNetMenuHUD"/>)는 접속 6초 뒤 사라지며 나가기 항목이 없다.</para>
    ///
    /// <para><b>두 갈래로 막는다</b>
    /// ① <b>존을 밟고 나가기</b> — 의도적으로 끝낼 때. 지나가다 스치는 것으로는 안 끊기게 dwell(기본 10초)을 둔다.
    /// ② <b>헤드셋 이탈 자동 종료</b> — 실제로는 대부분 존을 안 밟고 그냥 앱을 끈다. 그때가 근본 원인이므로
    ///    XR 디스플레이가 멈추면 유예 뒤 스스로 Shutdown 한다. ①만으로는 같은 사고가 계속 난다.</para>
    ///
    /// 씬에 안 붙여도 <c>[RuntimeInitializeOnLoadMethod]</c> 로 자동 스폰 — Port.unity 를 건드리지 않는다
    /// (다른 세션도 같은 씬을 편집 중이라 씬 변경은 충돌 위험이 크다). 접속 중일 때만 보인다.
    /// </summary>
    [AddComponentMenu("AI-XR Crane/Net/Exit Zone")]
    [DisallowMultipleComponent]
    public sealed class ExitZone : MonoBehaviour
    {
        [Header("존")]
        [Tooltip("존 반경(실척 m). 걷다가 실수로 들어오지 않게 부두 '모서리'에 둔다.")]
        [SerializeField] float radiusMeters = DefaultRadiusMeters;
        [Tooltip("존 안에 이만큼 서 있어야 나간다(초). 지나가다 스치는 것으로 안 끊기게.")]
        [SerializeField] float dwellSeconds = 10f;   // 오너 지시 2026-09-17 — 트리거를 없앤 대신 체류시간으로 실수 방지(시연 뒤 20→10초)
        [Tooltip("걷는 땅 모서리에서 안쪽으로 띄울 거리(실척 m) — 띠가 경계 밖으로 새지 않게.")]
        [SerializeField] float insetMeters = DefaultInsetMeters;

        /// <summary>기본 인셋(실척 m) — 에디터 표시용 체스말도 같은 값을 써야 같은 자리에 선다.</summary>
        public const float DefaultInsetMeters = 6f;

        /// <summary>기본 반경(실척 m) — 에디터가 씬에 심는 띠도 같은 값을 써야 런타임 띠와 겹친다.</summary>
        public const float DefaultRadiusMeters = 3f;

        [Header("헤드셋 이탈 자동 종료 (근본 대책)")]
        [Tooltip("헤드셋이 빠진 뒤 이만큼 지나면 세션을 자동 종료(초). 0 이하면 끔. " +
                 "★ 호스트는 '관전자가 없을 때만' 적용된다 — 남을 끊는 자동 종료는 하지 않는다(WatchHeadset 주석).")]
        [SerializeField] float headsetLostGraceSeconds = 15f;

        // 나가기 = 빨강(HudColor.Danger 계열). 접근 범위 띠(청록)와 색으로 구분돼 헷갈리지 않는다.
        static readonly Color BandIdle = new Color(0.92f, 0.20f, 0.18f, 0.35f);
        static readonly Color BandInside = new Color(0.92f, 0.20f, 0.18f, 0.95f);

        // ★ 월드공간 Canvas(HUD)는 2026-09-17 오너 지시로 없앴다 — "나가는 존에 HUD 삭제하고
        //   blender 에서 표시판 하나 이쁘게 만들어서 나가는 존 가운데 놔줘".
        //   대신 실물 표지판 ExitSign.fbx(문서/스크립트/표시판_빌드.py)가 존 중심에 선다
        //   (BuildSign 이 존과 같은 center 를 쓰므로 자리가 어긋날 수 없다 — 심는 메뉴는 없앴다).
        //   바닥 띠(LineRenderer)는 그대로 둔다 — 존 '경계'는 표지판으로 못 보여준다.
        /// <summary>표지판 리소스 경로(Assets/Crane/Resources/ 기준, 확장자 없음).</summary>
        public const string SignResourcePath = "Models/ExitSign";
        /// <summary>표지판 배율 — 실척 모델을 이만큼 부풀려 세운다. 오너 지시 2026-09-17 "10배" → "더 많이 키우고".
        ///   ★ 이건 표지판의 <b>실제 치수가 아니라 연출</b>이다(실척 2.2m 짜리를 66m 로 보이게 한다).
        ///     모델은 실척 그대로 두고 '얼마나 크게 세우나'만 여기서 정한다 — 치수와 연출을 한 숫자에 섞으면
        ///     다음 사람이 어느 쪽을 고쳐야 할지 모른다(표시판_빌드.py 의 H_TOTAL 2.20 은 건드리지 않았다).
        ///   ★ <b>눈으로 맞추는 값</b>이라 손잡이로 남긴다 — 헤드셋에서 보고 이 숫자만 고치면 된다.
        ///   ★ 2026-09-18 <b>30f → 15f</b>(오너 "사이즈도 줄여"). 실척 66m → <b>33m</b>.
        ///     옛값 30 은 "더 많이 키우고" 시절 값인데, 66m 면 RTG(약 20m)보다 세 배라 항구를 눌렀다.
        ///     글자 높이는 실척 0.33m × 배율 이므로 15 에서도 5m — 야드(165m)에서 충분히 읽힌다.</summary>
        const float SignScale = 15f;   // 옛값 30f

        /// <summary>표지판 방향 보정(도, 월드 Y 축) — 바라볼 곳을 정한 뒤 남는 미세 조정용 손잡이.
        ///   ★ 2026-09-18 <b>90f → 0f</b>. 옛 값 90 은 "정면축을 모르니 사람이 보고 정한다"는 전제로 넣은
        ///     것인데, 정면축은 <b>잴 수 있었다</b>: 블렌더 앞면 −Y 가 축 보정 Rx(−90) 뒤 유니티 <b>+Z</b> 가 되고
        ///     (국소 (0,−1,0) → (x, z, −y) = (0,0,+1)), 그건 LookRotation 의 forward 와 같은 축이다.
        ///     즉 보정 0 이라야 바라보는 곳을 정면으로 본다. 90 을 더한 탓에 표지판은 목표에서 <b>90° 빗나가</b>
        ///     서 있었고(실측: 리스폰이 +X 인데 야우 180°, 정면 −Z), 오너가 "돌려"라고 말할 때까지 남아 있었다.
        ///   ★ 손잡이는 남긴다 — 반대로 보이면 180 을, 옆으로 보이면 ±90 을 넣으면 끝난다.</summary>
        const float SignYawOffset = 0f;   // 옛값 90f — 위 주석 참조

        /// <summary>표지판 실척 높이(m) — 표시판_빌드.py 의 H_TOTAL 과 같은 값.
        ///   ★ 이 값은 <b>목표</b>일 뿐 FBX 단위계를 가정하지 않는다. 실제 배율은 BuildSign 이 프리팹을
        ///     띄워 <b>재서</b> 맞춘다(FbxScaleByHeight 와 같은 원리). 그래서 H_TOTAL 과 어긋나도
        ///     표지판이 틀린 크기로 서는 게 아니라 '목표가 바뀌는' 것뿐이라 조용히 깨지지 않는다.</summary>
        const float SignRealHeightMeters = 2.2f;

        GameObject sign;
        LineRenderer band;
        NetLanUI ui;
        Vector3 center;
        bool placed;
        float dwell;
        float nextFind;
        float xrLostFor;
        bool xrSeenRunning;      // 한 번이라도 XR 이 돌았는가 — 평면(비VR) 모드에서 자동 종료가 오발하지 않게
        readonly List<XRDisplaySubsystem> displays = new List<XRDisplaySubsystem>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoSpawn() => CraneHud.EnsureSpawned<ExitZone>("ExitZone");

        void Update()
        {
            var nm = NetworkManager.Singleton;
            bool connected = nm != null && (nm.IsClient || nm.IsServer);
            if (!connected) { SetVisible(false); dwell = 0f; xrLostFor = 0f; return; }

            WatchHeadset();                       // ② 자동 종료 — 존 배치와 무관하게 항상 돈다
            if (!placed && !TryPlace()) return;   // 걷는 땅이 아직 없으면(빌더 미완) 다음 프레임에 재시도

            var cam = Camera.main;
            if (cam == null) { SetVisible(false); return; }

            SetVisible(true);
            float r = radiusMeters * StsConfig.ModelScale;
            Vector3 d = cam.transform.position - center; d.y = 0f;
            bool inside = d.sqrMagnitude <= r * r;

            // 오너 지시 2026-09-17: "나가는 존에 들어가면 그냥 트리거 없이 20초 뒤에 나가게 만들자".
            //   종전에는 호스트에게 트리거 홀드를 요구했다 — 호스트가 끊기면 관전자도 같이 끊기기 때문이었다.
            //   그 보호를 체류시간으로 옮긴다: 2초는 지나가다 스칠 수 있지만 10초는 서 있기로 결심해야 채워진다.
            //   호스트/관전자 구분은 경고 문구에만 남긴다(끊기는 사람이 몇 명인지는 여전히 보여줘야 한다).
            int spectators = SpectatorCount();
            bool host = spectators >= 0;
            bool arming = inside;

            dwell = arming ? dwell + Time.unscaledDeltaTime : 0f;
            band.startColor = band.endColor = inside ? BandInside : BandIdle;

            if (arming && dwell >= dwellSeconds)
            {
                CraneAlarmHUD.ClearNotice();
                Leave(host ? $"존에서 나감 — 호스트(관전자 {spectators}명 함께 종료)" : "존을 밟고 나감");
                return;
            }

            // 헤드셋 상단 공지 — 오너 지시 2026-09-17 "존에 들어오면 … 상단에 N초 후 종료합니다"(N = dwellSeconds).
            //   존 안에 있는 동안만 매 프레임 갱신한다. 밖으로 나가면 갱신이 끊겨 유예 뒤 저절로 사라진다
            //   (여기서 지우지 않아도 남지 않는다 — 호출자가 정리를 잊는 실수 자체를 없앤 설계).
            //   끊기는 사람이 나 말고 더 있으면 그 수를 같이 보여준다. 모르고 끊으면 안 되니까.
            if (inside)
            {
                float left = Mathf.Max(0f, dwellSeconds - dwell);
                string more = spectators > 0 ? $"  <size=26>(관전자 {spectators}명도 함께)</size>" : "";
                CraneAlarmHUD.Notify($"<b>{left:0}초 후 종료합니다</b>{more}", CraneHud.HudColor.Danger);
            }

        }

        // ② 헤드셋 이탈 감시 — XR 디스플레이가 running 에서 멈추면 유예 뒤 종료.
        //   평면 모드(비VR)에서는 애초에 running 이 된 적이 없으므로 xrSeenRunning 가드로 오발을 막는다.
        //
        // ★ 호스트는 '관전자가 없을 때만' 자동 종료한다 (xr-port-04·ae·42 지적 2026-09-16).
        //   호스트가 Shutdown 하면 관전자 전원이 시작 메뉴로 튕긴다. 시연 중 헤드셋을 잠깐 벗는 건 아주 흔하고
        //   (설명하려고·클라이언트에게 씌워 주려고), 벗은 사람에게는 경고를 띄울 화면조차 없다 → 유예를 늘려도 '알고 누른다'가 안 된다.
        //   반대로 '혼자 남은 호스트'의 자동 정리는 그대로 둔다 — 그게 오너가 보고한 원래 버그
        //   (앱을 그냥 끄면 7777 이 잡힌 채 남아 다음 호스트가 실패)의 유일한 자동 해결 경로다.
        //   "명시적 종료로 대체하면 된다"는 논리는 '명시적 종료를 안 하는 경우'를 못 덮는다 — 그게 원래 사고였다.
        void WatchHeadset()
        {
            if (headsetLostGraceSeconds <= 0f) return;

            bool running = false;
            SubsystemManager.GetSubsystems(displays);
            foreach (var d in displays) if (d != null && d.running) { running = true; break; }

            if (running) { xrSeenRunning = true; xrLostFor = 0f; return; }
            if (!xrSeenRunning) return;            // VR 로 시작한 적이 없는 세션 — 감시 대상 아님

            int spectators = SpectatorCount();
            // 관전자가 있으면 타이머 자체를 안 쌓는다 — 안 그러면 관전자가 나간 순간 '이미 지난 시간'으로 즉시 종료된다.
            if (spectators > 0) { xrLostFor = 0f; return; }

            xrLostFor += Time.unscaledDeltaTime;
            if (ShouldAutoEnd(xrLostFor, headsetLostGraceSeconds, spectators))
                Leave($"헤드셋 이탈 {headsetLostGraceSeconds:0}초 경과 — 자동 정리(관전자 없음)");
        }

        /// <summary>헤드셋 이탈 자동 종료 판정 — <b>규칙 그 자체</b>. 배관(서브시스템 폴링)과 분리해 XR 없이도 부를 수 있다.
        ///   배치(-batchmode)에는 XRDisplaySubsystem 이 없어 <see cref="WatchHeadset"/> 경로는 실행조차 안 되므로,
        ///   회귀가 실제로 나는 '규칙'만 떼어 검사 가능하게 둔다(xr-port-42 HostStartProbe ④).
        /// <para>spectators 규약 — <see cref="SpectatorCount"/> 와 같다:
        ///   <b>≥1</b> 나는 호스트고 관전자가 붙어 있다 → 종료 안 함(남을 끊게 된다).
        ///   <b>0</b> 나는 호스트인데 혼자다 → 유예 뒤 종료(잃는 사람 없고 포트 7777 을 푼다 — 오너의 원래 버그).
        ///   <b>−1</b> 나는 호스트가 아니다(관전자) → 유예 뒤 종료(자기 연결만 끊긴다).</para></summary>
        public static bool ShouldAutoEnd(float lostFor, float graceSeconds, int spectators)
            => graceSeconds > 0f && lostFor >= graceSeconds && spectators <= 0;

        /// <summary>내가 호스트일 때 붙어 있는 관전자 수(나 자신 제외). 호스트가 아니면 −1.
        ///   자동 종료 가드와 안내 문구가 <b>같은 수</b>를 보도록 판정을 한 곳에 둔다.</summary>
        int SpectatorCount()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer) return -1;
            return Mathf.Max(0, nm.ConnectedClientsIds.Count - 1);   // 호스트 자신(0번) 제외 — CraneNetMenuHUD 와 같은 셈
        }

        // 오른손 검지 트리거 — 모드 변경(StsCraneVRController)과 같은 입력·같은 임계값 관례.

        void Leave(string why)
        {
            dwell = 0f;
            xrLostFor = 0f;
            if (ui == null) ui = FindAnyObjectByType<NetLanUI>();
            if (ui != null) ui.Leave();
            else { var nm = NetworkManager.Singleton; if (nm != null) nm.Shutdown(); }
            SetVisible(false);
            Debug.Log($"[ExitZone] 세션 종료 — {why}. 시작 메뉴로 돌아갑니다(호스트 포트도 함께 풀림).");
            QaLog.Info("EXIT", "leave", $"reason={why}");
        }

        /// <summary>존 중심 — 걷는 땅(케이슨+야드 포장 합집합)의 네 모서리 중 플레이어 시작점에서 가장 가까운 곳.
        ///   '모서리'라 평소 동선과 겹치지 않고, '가장 가까운' 쪽이라 처음 보는 사람도 눈에 띈다.
        ///   ★ 런타임 존과 에디터 표시용 체스말이 <b>같은 자리</b>를 쓰도록 계산은 여기 한 곳뿐이다
        ///     (눈으로 확인한 자리와 실제 나가는 자리가 어긋나면 그 표시는 쓸모가 없다).</summary>
        public static bool TryComputeCenter(float insetMeters, out Vector3 center)
        {
            center = default;
            if (!CranePlayerStartPlacer.TryGetLand(out Bounds land)) return false;

            // ★ 지정 마커가 있으면 그 자리를 쓴다 — 오너가 체스말로 자리를 정하는 흐름
            //   (QuayPartsPlacer.ApplyPawnToExitZone). 리스폰이 PlayerStartPoint 를 우선하는 것과
            //   같은 규칙이라 다음 사람이 두 곳을 같은 방식으로 읽는다. 마커가 없으면 아래 모서리 계산이 폴백.
            var fixedPoint = GameObject.Find(StsPartNames.ExitZonePoint);
            if (fixedPoint != null)
            {
                // 걷는 땅 밖이면 안으로 끌어당긴다 — 존이 허공이나 바다에 뜨면 밟을 수가 없다(리스폰 클램프와 같은 이유).
                float ins = insetMeters * StsConfig.ModelScale;
                Vector3 fp = fixedPoint.transform.position;
                center = new Vector3(Mathf.Clamp(fp.x, land.min.x + ins, land.max.x - ins),
                                     land.max.y + 0.1f * StsConfig.ModelScale,   // 바닥 z-파이팅 방지
                                     Mathf.Clamp(fp.z, land.min.z + ins, land.max.z - ins));
                return true;
            }

            var marker = GameObject.Find(StsPartNames.PlayerStartPoint);
            Vector3 from = marker != null ? marker.transform.position
                         : Camera.main != null ? Camera.main.transform.position
                         : land.center;

            float inset = insetMeters * StsConfig.ModelScale;
            float x = from.x < land.center.x ? land.min.x + inset : land.max.x - inset;
            float z = from.z < land.center.z ? land.min.z + inset : land.max.z - inset;
            // 바닥과 z-파이팅 방지 — 접근 범위 띠와 같은 실척 0.1m 띄움.
            center = new Vector3(x, land.max.y + 0.1f * StsConfig.ModelScale, z);
            return true;
        }

        bool TryPlace()
        {
            if (Time.unscaledTime < nextFind) return false;
            nextFind = Time.unscaledTime + 0.5f;
            if (!TryComputeCenter(insetMeters, out center)) return false;

            transform.position = center;
            BuildBand();
            // ★ 표지판은 오너 지시 2026-09-18 "유니티에 있는 표지판을 일단 삭제해" 로 <b>띄우지 않는다</b>.
            //   '일단' 이라 되돌리기 쉽게 호출만 막는다 — BuildSign 본문·ExitSign.fbx·표시판_빌드.py 는 그대로 둔다.
            //   되살리려면 아래 한 줄의 주석만 풀면 된다.
            //   ★ 씬에 심은 오브젝트는 애초에 없다(Port.unity 의 ExitSign 0개) — 표지판은 접속 중에만 생기는
            //     런타임 생성물이라, '유니티에서 지운다' 는 곧 이 호출을 막는 것이다.
            // BuildSign();
            placed = true;
            // 좌표는 실척(m)을 앞에 찍는다 — 오너 지시 2026-09-17 "실척 좌표로 해줘".
            //   모델 단위는 1 unit = 24 m 라 숫자가 1/24 로 눌려 사람이 못 읽는다(−0.25 vs −6.00m).
            //   모델 값도 괄호로 같이 남긴다 — 코드에 넣을 땐 그쪽이 필요하다.
            Vector3 real = center * StsConfig.InvModelScale;
            Debug.Log($"[ExitZone] 나가는 존 배치 — 실척 X {real.x:F2}m · Z {real.z:F2}m (모델 {center.x:F4}, {center.z:F4}) · " +
                      $"반경 실척 {radiusMeters:0.#}m · {dwellSeconds:0}초 머물면 종료 · " +
                      $"헤드셋 이탈 {headsetLostGraceSeconds:0}초면 자동 종료");
            return true;
        }

        /// <summary>존 가운데에 '나가는 문' 표지판을 세운다 — 오너 지시 2026-09-17.
        ///
        /// ★ 지금은 <b>호출이 막혀 있다</b>(TryPlace 참조). 표지판은 2026-09-18 오너 지시로 씬에 심는 쪽으로
        ///   옮겼다 — "Scene 를 서버쪽이랑 똑같이 만들어줘". 런타임 생성과 씬 배치가 <b>겹치면 표지판이 둘</b>
        ///   서므로, 되살릴 땐 씬에 심은 ExitSign 을 먼저 지울 것.
        /// ★ 자리·자세·배율은 <see cref="FitSign"/> 한 곳에서 나온다 — 에디터 배치와 같은 식이다.
        /// ★ 리소스가 없으면 띠만 남기고 조용히 넘어간다 — 표지판 때문에 존이 동작을 멈추면 안 된다.</summary>
        void BuildSign()
        {
            if (sign != null) return;
            var prefab = Resources.Load<GameObject>(SignResourcePath);
            if (prefab == null) return;

            sign = Instantiate(prefab, transform);
            sign.name = "ExitSign";
            sign.transform.localPosition = Vector3.zero;   // 존 중심 = 부모 원점

            FitSign(sign, prefab.transform.localRotation, center);
        }

        /// <summary>표지판의 <b>자세·배율</b>. 런타임(ExitZone.BuildSign)과 에디터 배치
        /// (QuayPartsPlacer '나가는 문 표지판 씬에 배치')가 <b>같은 식</b>을 쓰도록 여기 한 곳에만 둔다.
        ///   ★ 두 곳에 베껴 두면 반드시 갈라진다 — 2026-09-17 에만 이 식이 두 번 틀렸다(누움 · 100배).
        ///     씬에 심은 표지판과 VR 에 뜨는 표지판이 다르면 씬을 보고 고칠 수가 없다.</summary>
        /// <param name="axisFix">임포트된 프리팹 루트의 회전(블렌더 Z-up 보정). 지우면 표지판이 눕는다.</param>
        public static void FitSign(GameObject sign, Quaternion axisFix, Vector3 center)
        {
            // 실척으로 만든 모델에 축척(1/24)만 곱한다 — 오너 지시 2026-09-17 "실제 사이즈 만들고 1/24 이렇게 작업해야지".
            //   ★ 종전에는 렌더러 바운즈의 Y 를 '높이' 로 보고 맞췄다. 그런데 FBX 축이 틀어져 들어오면 Y 가
            //     높이가 아니라 폭(1.8m)이 되고, 1.8 을 0.1 로 줄여 18배 작아진 채 누워 버린다 — 실제로 그랬다.
            //     상수 배율은 그런 '조용한 어긋남' 이 없다. 모델이 실척이라는 전제만 지키면 된다.
            // ★★ FBX 축 보정을 지우지 말 것 — 표지판이 바닥에 눕는 원인이 바로 이것이었다(오너 2026-09-17 "바닥에 누워 있어").
            //   블렌더 메시는 Z-up 이라 정점이 높이를 Z 에 갖고 있다(ExitSign.fbx 실측: X 1.80 폭 · Y 0.29 두께 · Z 0…2.20 높이).
            //   그걸 세우는 건 임포트된 루트의 회전 −90°X <b>하나뿐</b>인데, 여기서 rotation 에 그냥 대입하면 그 보정이 날아간다.
            //   LookRotation(수평벡터, up) 은 피치가 0 이라, 대입 즉시 높이축 Z 가 월드 +Z 로 누워 판이 바닥에 깔린다.
            //   그래서 덮어쓰지 않고 <b>곱한다</b>(축 보정 먼저 → 그다음 야우).
            //   임포터에서 축을 구우면(bakeAxisConversion=1) 이 회전이 단위원이 되어 식이 그대로 성립한다.
            sign.transform.localRotation = axisFix;   // 바라볼 곳을 못 구해도 최소한 서 있게

            // ★★ 배율을 계산으로 단정하지 말고 <b>재서 맞춘다</b> — 2026-09-17 여기서 100배를 틀렸다.
            //   종전엔 "실척 모델이니 ModelScale×SignScale 이면 된다"고 단정했는데, 이 FBX 는 노드에
            //   Lcl Scaling 100 이 들어 있어 임포트된 프리팹의 단위가 그 가정과 100배 달랐다. 결과가
            //   실척 66m 여야 할 표지판이 0.7m 로 섰고(로그로 잡혔다), 오너에겐 "작다"가 아니라
            //   "디자인이 깨져 보인다"로 나타났다 — 70cm 판에 EXIT 를 넣으면 멀리서 뭉개진다.
            //   그래서 프리팹을 배율 1 로 세워 높이를 재고 목표 실척으로 수렴시킨다(FbxScaleByHeight 와 같은 식).
            //   ★ 반드시 axisFix 를 먼저 건 뒤에 잰다 — 안 그러면 Y 가 높이가 아니라 두께(0.29m)라 배율이 7배 튄다.
            //   ★ 에디터 헬퍼(QuayPartsPlacer.FbxScaleByHeight)는 PrefabUtility 를 써서 런타임에선 못 부른다.
            sign.transform.localScale = Vector3.one;
            float h = MeasuredHeight(sign);
            float targetWorld = SignRealHeightMeters * SignScale * StsConfig.ModelScale;
            sign.transform.localScale = Vector3.one * (h > 1e-6f ? targetWorld / h : StsConfig.ModelScale);

            // RTG 크레인(야드 블록) 쪽을 바라보게 — 오너 지시 2026-09-18 "돌려 EXIT RTG 크레인을 보는 방향으로".
            //   블렌더 앞면은 −Y(FACE_F)고 축 보정 뒤 유니티 +Z 가 되므로 LookRotation 의 forward 와 맞는다.
            //   ★ 씬에서 RTG 를 <b>찾지 않는다</b>. 이름이 "RTG 크레인_1"(한글·공백)이라 코드의 "RTG_Crane_N"
            //     로는 안 걸리고, 대수·이름은 언제든 바뀐다. RTG 가 서는 자리는 야드 블록이고 그 중심은
            //     PortConfig 가 수식으로 갖고 있다 — 씬 탐색 없이 SSOT 에서 바로 나온다.
            //     Z 는 블록이 안벽 중앙 기준 대칭이라 0(씬 실측: 블록 두 개가 z ±3.5313 에 있다).
            float yardX = PortConfig.YardBlockCenterX(PortConfig.YardLaneStart) * StsConfig.ModelScale;
            Vector3 look = new Vector3(yardX - center.x, 0f, -center.z);
            if (look.sqrMagnitude > 1e-6f)
                sign.transform.rotation = Quaternion.AngleAxis(SignYawOffset, Vector3.up)
                                        * Quaternion.LookRotation(look.normalized, Vector3.up) * axisFix;

            // 진단 — '섰나 누웠나'를 로그 한 줄로 끝낸다. 모델이 높이 2.2m · 폭 1.8m · 두께 0.29m 라
            //   월드 바운즈에서 <b>Y 가 가장 길면 서 있는 것</b>이고, 아니면 누운 것이다(야우로는 X·Z 만 섞인다).
            //   ★ 오너 눈으로만 닫히던 항목을 기계가 먼저 거르게 하려는 것이다 — 헛배포 한 번을 아낀다.
            var rend = sign.GetComponentInChildren<Renderer>();
            if (rend != null)
            {
                Vector3 s = rend.bounds.size * StsConfig.InvModelScale;
                bool upright = s.y >= Mathf.Max(s.x, s.z);
                Debug.Log($"[ExitZone] 표지판 — 실척 바운즈 X {s.x:F1}m · Y {s.y:F1}m · Z {s.z:F1}m " +
                          $"(배율 {SignScale:0}× · 야우보정 {SignYawOffset:0}°) → {(upright ? "서 있음" : "★ 누웠다")}");
            }
        }

        /// <summary>렌더러 전체를 합친 월드 높이. 서브메시가 여럿이거나 자식으로 쪼개져 들어와도 같은 값이 나온다.</summary>
        static float MeasuredHeight(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return 0f;
            var b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            return b.size.y;
        }

        void BuildBand() { if (band == null) band = CreateBand(transform, radiusMeters); }

        /// <summary>바닥 띠(존 경계) 한 개. 런타임과 에디터 배치가 같은 띠를 쓰도록 static 으로 둔다.</summary>
        public static LineRenderer CreateBand(Transform parent, float radiusMeters)
        {
            var go = new GameObject("ExitBand");
            go.transform.SetParent(parent, false);
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);   // 로컬 Y → 월드 Z, 띠 면이 바닥에 눕는다
            var band = go.AddComponent<LineRenderer>();
            band.useWorldSpace = false;
            band.loop = true;
            band.alignment = LineAlignment.TransformZ;
            band.material = BandMaterial();
            band.widthMultiplier = 0.8f * StsConfig.ModelScale;      // 띠 폭 실척 0.8m — 접근 범위 띠와 같은 굵기
            band.startColor = band.endColor = BandIdle;
            band.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            band.receiveShadows = false;

            const int seg = 48;
            float r = radiusMeters * StsConfig.ModelScale;
            band.positionCount = seg;
            for (int i = 0; i < seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f;
                band.SetPosition(i, new Vector3(r * Mathf.Cos(a), r * Mathf.Sin(a), 0f));
            }
            return band;
        }

        // 띠 단면 알파 = 가우시안 — 가운데 진하고 가장자리로 번져 사라진다.
        //   PortDemoDirector.RingMaterial 과 같은 식(색·용도만 다름). 10줄이라 공용화 대신 복제했다 —
        //   합칠 거면 CraneHud 로 올리는 게 맞고, 그건 여러 세션이 동시에 만지는 파일이라 지금은 피했다.
        static Material BandMaterial()
        {
            const int n = 32;
            var tex = new Texture2D(1, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int i = 0; i < n; i++)
            {
                float v = (i + 0.5f) / n * 2f - 1f;
                tex.SetPixel(0, i, new Color(1f, 1f, 1f, Mathf.Exp(-6f * v * v)));
            }
            tex.Apply();
            return new Material(Shader.Find("Sprites/Default")) { mainTexture = tex };
        }

        void SetVisible(bool v)
        {
            if (band != null && band.enabled != v) band.enabled = v;
            if (sign != null && sign.activeSelf != v) sign.SetActive(v);
        }
    }
}
