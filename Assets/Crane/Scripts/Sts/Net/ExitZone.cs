using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.XR;

namespace AIXRCrane.Crane.Sts.Net
{
    /// <summary>부두 모서리의 나가는 존 — dwell(기본 10초) 서 있으면 세션 종료. 헤드셋 이탈 시에도 유예 뒤 자동 종료.
    /// 씬에 안 붙여도 런타임에 자동 스폰된다.</summary>
    [AddComponentMenu("AI-XR Crane/Net/Exit Zone")]
    [DisallowMultipleComponent]
    public sealed class ExitZone : MonoBehaviour
    {
        [Header("존")]
        [Tooltip("존 반경(실척 m). 걷다가 실수로 들어오지 않게 부두 '모서리'에 둔다.")]
        [SerializeField] float radiusMeters = DefaultRadiusMeters;
        [Tooltip("존 안에 이만큼 서 있어야 나간다(초). 지나가다 스치는 것으로 안 끊기게.")]
        [SerializeField] float dwellSeconds = 10f;   // 체류시간으로 오조작 방지
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

        /// <summary>표지판(ExitSign.fbx) 리소스 경로(Resources/ 기준, 확장자 없음). QuayPartsPlacer 가 존 중심에 심는다.
        /// 바닥 띠(LineRenderer)는 경계 표시용으로 따로 유지.</summary>
        public const string SignResourcePath = "Models/ExitSign";
        // 표지판도 실척 × ModelScale(1/24)로만 선다 — 연출 배율 없음, 사람 눈높이 실제 크기.

        /// <summary>표지판 방향 미세 보정(도, 월드 Y). 블렌더 앞면 −Y 가 축 보정 뒤 유니티 +Z 라 0 이 정면.
        /// 반대로 보이면 180, 옆이면 ±90.</summary>
        const float SignYawOffset = 0f;

        /// <summary>표지판 실척 높이(m) — 표시판_빌드.py 의 H_TOTAL. FitSign 이 프리팹을 재서 수렴시키므로
        /// 값이 달라도 조용히 깨지지 않는다.</summary>
        const float SignRealHeightMeters = 2.2f;

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

            // 트리거 없이 체류시간(dwell)으로 오조작을 막는다 — 지나가다 스치면 안 채워지고 서 있어야 채워진다.
            // 호스트/관전자 구분은 경고 문구에만 남긴다(끊기는 인원 수는 보여줘야 한다).
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

            // 헤드셋 상단에 남은 초를 공지 — 존 안에 있는 동안만 갱신, 밖으로 나가면 유예 뒤 저절로 사라진다.
            // 관전자가 더 있으면 그 수도 같이 보여준다.
            if (inside)
            {
                float left = Mathf.Max(0f, dwellSeconds - dwell);
                string more = spectators > 0 ? $"  <size=26>(관전자 {spectators}명도 함께)</size>" : "";
                CraneAlarmHUD.Notify($"<b>{left:0}초 후 종료합니다</b>{more}", CraneHud.HudColor.Danger);
            }

        }

        // 헤드셋 이탈 감시 — running 이 멈추면 유예 뒤 종료. 평면 모드는 xrSeenRunning 가드로 오발 방지.
        // 호스트는 관전자가 없을 때만 자동 종료한다 — 있으면 전원이 튕기므로.
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

        /// <summary>헤드셋 이탈 자동 종료 판정 — 배관과 분리해 XR 없이도 테스트 가능.
        /// spectators: ≥1 종료 안 함, 0 유예 뒤 종료(호스트 단독), −1 유예 뒤 종료(관전자).</summary>
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

        /// <summary>존 중심 — 걷는 땅 네 모서리 중 플레이어 시작점에서 가장 가까운 곳.
        /// 런타임 존과 에디터 체스말이 같은 자리를 쓰도록 계산은 여기 한 곳뿐이다.</summary>
        public static bool TryComputeCenter(float insetMeters, out Vector3 center)
        {
            center = default;
            if (!CranePlayerStartPlacer.TryGetLand(out Bounds land)) return false;

            // 지정 마커(체스말, QuayPartsPlacer.ApplyPawnToExitZone)가 있으면 그 자리를 쓴다.
            // 리스폰의 PlayerStartPoint 우선과 같은 규칙. 없으면 아래 모서리 계산이 폴백.
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
            placed = true;
            // 좌표는 실척(m)을 앞에 찍는다 — 모델 단위(1u=24m)는 숫자가 눌려 사람이 못 읽는다.
            // 모델 값도 괄호로 같이 남긴다(코드에 넣을 땐 그쪽 필요).
            Vector3 real = center * StsConfig.InvModelScale;
            Debug.Log($"[ExitZone] 나가는 존 배치 — 실척 X {real.x:F2}m · Z {real.z:F2}m (모델 {center.x:F4}, {center.z:F4}) · " +
                      $"반경 실척 {radiusMeters:0.#}m · {dwellSeconds:0}초 머물면 종료 · " +
                      $"헤드셋 이탈 {headsetLostGraceSeconds:0}초면 자동 종료");
            return true;
        }

        /// <summary>표지판 자세·배율 공용 식(QuayPartsPlacer 도 호출, 한 곳에만 둔다).
        /// axisFix = 임포트된 프리팹 루트 회전(블렌더 Z-up 보정) — 지우면 표지판이 눕는다.</summary>
        public static void FitSign(GameObject sign, Quaternion axisFix, Vector3 center)
        {
            // 실척 모델에 축척(1/24)만 곱한다 — 렌더러 바운즈로 재면 FBX 축 틀어짐에 조용히 어긋난다.
            // FBX 축 보정(axisFix)을 rotation 에 그냥 대입하지 말 것 — 표지판이 눕는다. 곱해서 유지한다.
            sign.transform.localRotation = axisFix;   // 바라볼 곳을 못 구해도 최소한 서 있게

            // 배율을 단정하지 말고 재서 맞춘다 — FBX 마다 단위가 다를 수 있다(100배 차이 사례).
            // axisFix 를 먼저 건 뒤에 재야 한다 — 안 그러면 Y 가 두께가 되어 배율이 튄다.
            sign.transform.localScale = Vector3.one;
            float h = SceneUtil.TryBounds(sign.transform, out var signB) ? signB.size.y : 0f;
            float targetWorld = SignRealHeightMeters * StsConfig.ModelScale;
            sign.transform.localScale = Vector3.one * (h > 1e-6f ? targetWorld / h : StsConfig.ModelScale);

            // RTG(야드 블록) 쪽을 바라보게 — 이름으로 씬 탐색하지 않고 PortConfig 수식으로 중심을 구한다.
            // 블렌더 앞면 −Y 가 축 보정 뒤 유니티 +Z 라 LookRotation 의 forward 와 맞는다.
            float yardX = PortConfig.YardBlockCenterX(PortConfig.YardLaneStart) * StsConfig.ModelScale;
            Vector3 look = new Vector3(yardX - center.x, 0f, -center.z).normalized;
            // STS 방향도 더해 이등분선을 본다(단위벡터 합) — 각도를 손으로 적으면 존·야드 이동 시 어긋난다.
            // STS 가 없으면 RTG 쪽만 본다.
            var sts = GameObject.Find(StsPartNames.StsCraneRoot);
            if (sts != null)
            {
                Vector3 p = sts.transform.position;
                look += new Vector3(p.x - center.x, 0f, p.z - center.z).normalized;
            }
            if (look.sqrMagnitude > 1e-6f)
                sign.transform.rotation = Quaternion.AngleAxis(SignYawOffset, Vector3.up)
                                        * Quaternion.LookRotation(look.normalized, Vector3.up) * axisFix;

            // 진단 로그 — 월드 바운즈 Y 가 가장 길면 서 있는 것, 아니면 누운 것(야우로는 X·Z 만 섞인다).
            var rend = sign.GetComponentInChildren<Renderer>();
            if (rend != null)
            {
                Vector3 s = rend.bounds.size * StsConfig.InvModelScale;
                bool upright = s.y >= Mathf.Max(s.x, s.z);
                Debug.Log($"[ExitZone] 표지판 — 실척 바운즈 X {s.x:F1}m · Y {s.y:F1}m · Z {s.z:F1}m " +
                          $"(야우보정 {SignYawOffset:0}°) → {(upright ? "서 있음" : "★ 누웠다")}");
            }
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

        // 띠 단면 알파 = 가우시안(가운데 진함, 가장자리로 번져 사라짐). PortDemoDirector.RingMaterial 과 같은 식.
        // 10줄이라 공용화 대신 복제 — 합치려면 CraneHud 로.
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
        }
    }
}
