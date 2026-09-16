using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;

namespace Container.Crane.Sts.Net
{
    /// <summary>
    /// 부두 모서리 바닥의 **나가는 존** — 밟고 잠깐 서 있으면 세션을 끊고 시작 메뉴로 돌아간다.
    ///
    /// <para><b>왜 필요한가</b> — 오너 2026-09-16 "호스트로 접속했다가 그냥 앱을 끄면 호스트 세션이 그대로 남아
    /// 호스트가 접속이 안 된다". 서버에서는 크레인 5대가 <b>한 머신에서 같은 포트(7777)를 공유</b>하고,
    /// 먼저 호스트를 누른 인스턴스가 그 포트를 쥔다(<see cref="NetLanUI"/>.StartHost 주석 참조).
    /// 헤드셋을 벗어도 서버의 Crane.exe 는 24시간 계속 살아 있어 포트를 놓지 않는다 → 다음 호스트는 반드시 실패한다.
    /// VR 에는 나갈 길이 아예 없었다: <see cref="NetLanUI"/> 의 '연결 끊기'는 IMGUI 라 헤드셋에 안 그려지고,
    /// 시작 메뉴(<see cref="CraneNetMenuHUD"/>)는 접속 6초 뒤 사라지며 나가기 항목이 없다.</para>
    ///
    /// <para><b>두 갈래로 막는다</b>
    /// ① <b>존을 밟고 나가기</b> — 의도적으로 끝낼 때. 지나가다 스치는 것으로는 안 끊기게 dwell(기본 2초)을 둔다.
    /// ② <b>헤드셋 이탈 자동 종료</b> — 실제로는 대부분 존을 안 밟고 그냥 앱을 끈다. 그때가 근본 원인이므로
    ///    XR 디스플레이가 멈추면 유예 뒤 스스로 Shutdown 한다. ①만으로는 같은 사고가 계속 난다.</para>
    ///
    /// 씬에 안 붙여도 <c>[RuntimeInitializeOnLoadMethod]</c> 로 자동 스폰 — Port.unity 를 건드리지 않는다
    /// (다른 세션도 같은 씬을 편집 중이라 씬 변경은 충돌 위험이 크다). 접속 중일 때만 보인다.
    /// </summary>
    [AddComponentMenu("Container/Net/Exit Zone")]
    [DisallowMultipleComponent]
    public sealed class ExitZone : MonoBehaviour
    {
        [Header("존")]
        [Tooltip("존 반경(실척 m). 걷다가 실수로 들어오지 않게 부두 '모서리'에 둔다.")]
        [SerializeField] float radiusMeters = 3f;
        [Tooltip("존 안에 이만큼 서 있어야 나간다(초). 지나가다 스치는 것으로 안 끊기게.")]
        [SerializeField] float dwellSeconds = 2f;
        [Tooltip("걷는 땅 모서리에서 안쪽으로 띄울 거리(실척 m) — 띠가 경계 밖으로 새지 않게.")]
        [SerializeField] float insetMeters = DefaultInsetMeters;

        /// <summary>기본 인셋(실척 m) — 에디터 표시용 체스말도 같은 값을 써야 같은 자리에 선다.</summary>
        public const float DefaultInsetMeters = 6f;

        [Header("헤드셋 이탈 자동 종료 (근본 대책)")]
        [Tooltip("헤드셋이 빠진 뒤 이만큼 지나면 세션을 자동 종료(초). 0 이하면 끔. " +
                 "★ 호스트는 '관전자가 없을 때만' 적용된다 — 남을 끊는 자동 종료는 하지 않는다(WatchHeadset 주석).")]
        [SerializeField] float headsetLostGraceSeconds = 15f;

        [Header("호스트 확인 입력")]
        [Tooltip("호스트가 존에서 나갈 때 함께 당기고 있어야 하는 오른손 트리거 임계값. 관전자는 서 있기만 하면 된다(자기만 끊기므로). " +
                 "새 규약을 만들지 않고 모드 변경(StsCraneVRController.modeTriggerThreshold)과 같은 관례를 쓴다.")]
        [SerializeField, Range(0.1f, 0.95f)] float hostTriggerThreshold = 0.6f;

        // 나가기 = 빨강(HudColor.Danger 계열). 접근 범위 띠(청록)와 색으로 구분돼 헷갈리지 않는다.
        static readonly Color BandIdle = new Color(0.92f, 0.20f, 0.18f, 0.35f);
        static readonly Color BandInside = new Color(0.92f, 0.20f, 0.18f, 0.95f);

        LineRenderer band;
        Canvas canvas;
        Text text;
        NetLanUI ui;
        Vector3 center;
        bool placed;
        float dwell;
        float nextFind;
        float xrLostFor;
        bool xrSeenRunning;      // 한 번이라도 XR 이 돌았는가 — 평면(비VR) 모드에서 자동 종료가 오발하지 않게
        string lastText = "";
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

            // 호스트는 '서 있기'만으로 안 끊는다 — 끊기는 사람이 자기 혼자가 아니기 때문.
            //   관전자는 서 있기만 하면 된다(자기만 끊김). 트리거 홀드는 오너가 오늘 이미 익힌 동작이라 새로 배울 게 없다.
            int spectators = SpectatorCount();
            bool host = spectators >= 0;
            bool arming = inside && (!host || TriggerHeld());

            dwell = arming ? dwell + Time.unscaledDeltaTime : 0f;
            band.startColor = band.endColor = inside ? BandInside : BandIdle;

            if (arming && dwell >= dwellSeconds)
            {
                Leave(host ? $"존에서 나감 — 호스트(관전자 {spectators}명 함께 종료)" : "존을 밟고 나감");
                return;
            }

            // 끊기는 사람이 나 말고 더 있으면 반드시 먼저 보여준다.
            string warn = spectators > 0 ? $"\n<size=17><color=#EB332E>관전자 {spectators}명도 함께 끊깁니다</color></size>" : "";
            CraneHud.SetTextIfChanged(text, ref lastText,
                !inside
                    ? "<b><size=30><color=#EB332E>나가기</color></size></b>\n" +
                      $"<size=18><color=#999999>이 자리에 {(host ? "트리거를 당긴 채 " : "")}{dwellSeconds:0}초 서 있으면\n" +
                      "접속을 끊고 시작 화면으로 갑니다</color></size>" + warn
                : arming
                    ? $"<b><size=34><color=#EB332E>나가는 중… {Mathf.Max(0f, dwellSeconds - dwell):0.0}초</color></size></b>\n" +
                      "<size=18><color=#999999>존에서 나오거나 트리거를 놓으면 취소돼요</color></size>" + warn
                    : "<b><size=30><color=#EB332E>나가기</color></size></b>\n" +
                      "<size=18><color=#5FE0FF>오른손 트리거를 당긴 채 서 있으세요</color></size>" + warn);

            if (canvas != null) CraneHud.FaceCameraAbove(canvas.transform, transform, 2.2f * StsConfig.ModelScale, cam);
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
        bool TriggerHeld()
        {
            var right = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            return right.isValid && right.TryGetFeatureValue(CommonUsages.trigger, out float t) && t > hostTriggerThreshold;
        }

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
            BuildLabel();
            placed = true;
            Debug.Log($"[ExitZone] 나가는 존 배치 — {center} · 반경 실척 {radiusMeters:0.#}m · {dwellSeconds:0}초 머물면 종료 · " +
                      $"헤드셋 이탈 {headsetLostGraceSeconds:0}초면 자동 종료");
            return true;
        }

        void BuildBand()
        {
            if (band != null) return;
            var go = new GameObject("ExitBand");
            go.transform.SetParent(transform, false);
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);   // 로컬 Y → 월드 Z, 띠 면이 바닥에 눕는다
            band = go.AddComponent<LineRenderer>();
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

        void BuildLabel()
        {
            if (canvas != null) return;
            canvas = CraneHud.BuildPanel(transform, "ExitZoneCanvas", new Vector2(420, 170), 0.0016f,
                new Color(0f, 0f, 0f, CraneHud.PanelBgAlpha), 26, Color.white, TextAnchor.MiddleCenter,
                new Vector2(24, 18), out text, fitToText: true);
        }

        void SetVisible(bool v)
        {
            if (band != null && band.enabled != v) band.enabled = v;
            if (canvas != null && canvas.gameObject.activeSelf != v) canvas.gameObject.SetActive(v);
        }
    }
}
