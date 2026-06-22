using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace Container.Crane.Sts
{
    /// <summary>
    /// 성능 측정 도구(측정 전용). 출력 2가지 — ① 콘솔 [PERF] 한 줄 로그(기본, adb logcat), ② HMD 좌상단 HUD(옵션).
    ///   - 스터터 진단의 핵심은 평균 FPS가 아니라 '최악 프레임타임(worst frame)' → 최우선 측정.
    ///   - CPU/GPU 프레임타임은 FrameTimingManager(플레이어 설정 Frame Timing Stats=ON 필요)에서 읽음.
    ///   - GC 빈도(gen0 수집/초)·관리 힙 크기·동적 강체 수 = '버벅임' 원인 후보를 직접 확인.
    /// 콘솔만 쓰면 showHud=off 권장(캔버스 리빌드 부하/관찰자효과 제거 → 측정 정확). 측정 끝나면 이 스크립트만 지우면 됨.
    /// CraneStatusHUD(우상단)와 같은 head-locked 패턴/헬퍼(CraneHud) 재사용.
    /// </summary>
    [AddComponentMenu("Container/STS Crane/Crane Perf HUD")]
    [DisallowMultipleComponent]
    public sealed class CranePerfHUD : MonoBehaviour
    {
        [Header("참조")]
        [SerializeField] Camera targetCamera;

        [Header("HMD 좌상단 위치 (카메라 로컬 좌표, m)")]
        [SerializeField] Vector3 hmdOffset = new Vector3(-0.22f, 0.09f, CraneHud.HudDistance);   // x=왼쪽(상태HUD 우측과 겹침 방지)
        [SerializeField, Range(-30f, 30f)] float tiltYawDeg = 15f;    // 왼쪽 패널은 안쪽(오른쪽)으로 기울임
        [SerializeField, Range(-30f, 30f)] float tiltPitchDeg = 8f;

        [Header("패널/텍스트")]
        [SerializeField] Vector2 panelPixels = new Vector2(420f, 220f);
        [SerializeField] float worldScale = 0.00075f;
        [SerializeField] Color bgColor = new Color(0f, 0f, 0f, CraneHud.PanelBgAlpha);
        [SerializeField] int fontSize = 18;

        [Header("출력")]
        [Tooltip("콘솔에 [PERF] 한 줄 요약을 주기적으로 찍음(adb logcat으로 읽기, 부하 거의 없음·관찰자효과 적음).")]
        [SerializeField] bool logToConsole = true;
        [Tooltip("HMD 시야 안 패널 표시. 콘솔만 쓸 거면 꺼서 캔버스 리빌드 부하/관찰자효과 제거.")]
        [SerializeField] bool showHud = false;   // 콘솔로 보는 중 — HUD 끔(캔버스 미생성). 다시 보려면 true.
        [Tooltip("콘솔 로그 주기(초). 이 구간의 평균 FPS·최악 프레임타임·GC를 집계해 한 줄로 찍음.")]
        [SerializeField] float logIntervalSec = 2f;

        [Header("측정 설정")]
        [Tooltip("HUD 표시 갱신/집계 창 길이(초). 이 구간의 평균 FPS와 최악 프레임타임을 보여줌.")]
        [SerializeField] float windowSec = 0.5f;
        [Tooltip("기기 주사율(Hz). 0이면 자동 감지 실패 시 72로 가정. 프레임 예산 = 1000/Hz ms.")]
        [SerializeField] float refreshHzOverride = 0f;

        Canvas canvas;
        Text text;
        readonly StringBuilder sb = new StringBuilder(384);
        string lastText;
        float nextTextRefresh;

        // ─── 집계 창(무할당) ───
        int winFrames;          // 창 내 프레임 수
        float winTime;          // 창 내 누적 unscaledDeltaTime
        float winWorstMs;       // 창 내 최악(최대) 프레임타임 ms
        float showFps, showAvgMs, showWorstMs;   // 직전 창에서 확정된 표시값

        // ─── CPU/GPU(FrameTimingManager) ───
        readonly FrameTiming[] timings = new FrameTiming[1];
        float cpuMs, gpuMs;
        bool gpuTimingOk;

        // ─── GC ───
        int prevGcCount;
        float gcPerSec;
        long heapBytes;

        // ─── 콘솔 로그 집계(HUD 창과 독립, logIntervalSec 구간) ───
        int logFrames;
        float logTime, logWorstMs;
        int logGcBase;

        // ─── 강체 수(1초마다만 스캔) ───
        int rbTotal, rbDynamic;
        float nextRbScan;

        float refreshHz = 72f;
        float budgetMs = 13.9f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoSpawn() => CraneHud.EnsureSpawned<CranePerfHUD>("PERF");

        void Start()
        {
            refreshHz = refreshHzOverride > 1f ? refreshHzOverride : DetectRefreshHz();
            budgetMs = 1000f / Mathf.Max(refreshHz, 1f);
            prevGcCount = logGcBase = System.GC.CollectionCount(0);
            if (showHud) { BuildCanvas(); TryAttachToCamera(); }
        }

        static readonly System.Collections.Generic.List<UnityEngine.XR.XRDisplaySubsystem> _displays = new();
        static float DetectRefreshHz()
        {
            // 현행 API: XRDisplaySubsystem.TryGetDisplayRefreshRate (deprecated XRDevice.refreshRate 회피).
            //   에디터/비VR에선 실행 중 디스플레이가 없어 72로 폴백.
            UnityEngine.SubsystemManager.GetSubsystems(_displays);
            foreach (var d in _displays)
                if (d != null && d.running && d.TryGetDisplayRefreshRate(out float hz) && hz > 1f) return hz;
            return 72f;
        }

        void LateUpdate()
        {
            // 매 프레임 집계(스로틀 X — 측정 정확도가 목적). HUD가 꺼져 있어도 콘솔 로깅 위해 항상 측정.
            float dt = Time.unscaledDeltaTime;
            float frameMs = dt * 1000f;

            // CPU/GPU 프레임타임(최신 1프레임)
            FrameTimingManager.CaptureFrameTimings();
            uint got = FrameTimingManager.GetLatestTimings(1, timings);
            if (got > 0)
            {
                cpuMs = (float)timings[0].cpuFrameTime;
                gpuMs = (float)timings[0].gpuFrameTime;
                gpuTimingOk = gpuMs > 0.001f;
            }

            if (Time.unscaledTime >= nextRbScan) { ScanRigidbodies(); nextRbScan = Time.unscaledTime + 1f; }

            // ── HUD 창 집계(0.5s) ──
            if (showHud)
            {
                winFrames++; winTime += dt;
                if (frameMs > winWorstMs) winWorstMs = frameMs;
                if (winTime >= windowSec)
                {
                    showFps = winFrames / Mathf.Max(winTime, 1e-5f);
                    showAvgMs = winTime / winFrames * 1000f;
                    showWorstMs = winWorstMs;
                    int gc = System.GC.CollectionCount(0);
                    gcPerSec = (gc - prevGcCount) / winTime;
                    prevGcCount = gc;
                    heapBytes = System.GC.GetTotalMemory(false);
                    winFrames = 0; winTime = 0f; winWorstMs = 0f;
                }
                if (canvas != null && text != null)
                {
                    if (canvas.transform.parent == null || canvas.transform.parent == transform)
                        TryAttachToCamera();
                    if (CraneHud.Due(ref nextTextRefresh, CraneHud.TextHz))
                        CraneHud.SetTextIfChanged(text, ref lastText, BuildText());
                }
            }

            // ── 콘솔 로그 집계(logIntervalSec, HUD와 독립) ──
            if (logToConsole)
            {
                logFrames++; logTime += dt;
                if (frameMs > logWorstMs) logWorstMs = frameMs;
                if (logTime >= logIntervalSec)
                {
                    float fps = logFrames / Mathf.Max(logTime, 1e-5f);
                    int gc = System.GC.CollectionCount(0);
                    float gcps = (gc - logGcBase) / logTime;
                    logGcBase = gc;
                    long heap = System.GC.GetTotalMemory(false);
                    string cpuGpu = gpuTimingOk ? $"CPU {cpuMs:0.0} GPU {gpuMs:0.0}" : "CPU/GPU 측정대기";
                    string spike = logWorstMs > budgetMs * 1.5f ? "  ⚠스터터" : "";
                    Debug.Log($"[PERF] FPS {fps:0} avg {logTime / logFrames * 1000f:0.0}ms worst {logWorstMs:0.0}ms  " +
                              $"{cpuGpu}  GC {gcps:0.0}/s heap {heap / 1048576f:0.0}MB  RB {rbTotal}(동적{rbDynamic})  예산{budgetMs:0.0}ms{spike}");
                    logFrames = 0; logTime = 0f; logWorstMs = 0f;
                }
            }
        }

        void ScanRigidbodies()
        {
            var bodies = FindObjectsByType<Rigidbody>(FindObjectsSortMode.None);   // 1초마다만 — 풀 씬 스캔 비용 분산
            rbTotal = bodies.Length;
            rbDynamic = 0;
            for (int i = 0; i < bodies.Length; i++)
                if (!bodies[i].isKinematic) rbDynamic++;
        }

        string BuildText()
        {
            sb.Clear();
            string acc = CraneHud.Hex(CraneHud.HudColor.Accent);
            sb.AppendLine($"<b><size=21>성능 PerfHUD</size></b>  <size=14><color=#{acc}>{refreshHz:0}Hz · 예산 {budgetMs:0.0}ms</color></size>");
            sb.AppendLine();

            // FPS — 주사율의 90% 미만이면 경고색
            string fhex = CraneHud.Hex(showFps < refreshHz * 0.9f ? CraneHud.HudColor.Danger : CraneHud.HudColor.Ok);
            sb.AppendLine($"FPS    <b><color=#{fhex}>{showFps,3:0}</color></b>  <color=#999999>(avg {showAvgMs:0.0}ms)</color>");

            // worst frame — 스터터의 직접 지표. 예산의 1.5배 넘으면 빨강(눈에 띄는 끊김)
            string whex = CraneHud.Hex(showWorstMs > budgetMs * 1.5f ? CraneHud.HudColor.Danger
                                     : showWorstMs > budgetMs ? CraneHud.HudColor.Accent
                                     : CraneHud.HudColor.Ok);
            string warn = showWorstMs > budgetMs * 1.5f ? " ⚠ 스터터" : "";
            sb.AppendLine($"worst  <b><color=#{whex}>{showWorstMs,5:0.0} ms</color></b>{warn}");

            // CPU/GPU — 어느 쪽이 병목인지(GPU bound vs CPU bound)
            if (gpuTimingOk)
            {
                sb.AppendLine($"CPU    {cpuMs,5:0.0} ms");
                sb.AppendLine($"GPU    {gpuMs,5:0.0} ms");
            }
            else
                sb.AppendLine($"<color=#999999>CPU/GPU  측정대기(빌드/타이밍)</color>");

            sb.AppendLine();
            // GC — 0이 이상적. 초당 1회 이상이면 스터터 유발 가능 → 경고
            string ghex = CraneHud.Hex(gcPerSec > 0.5f ? CraneHud.HudColor.Danger : CraneHud.HudColor.IdleDim);
            sb.AppendLine($"GC     <color=#{ghex}>{gcPerSec:0.0}회/s</color>  <color=#999999>Heap {heapBytes / 1048576f:0.0}MB</color>");
            sb.AppendLine($"강체   {rbTotal}개  <color=#999999>(동적 {rbDynamic})</color>");
            sb.AppendLine($"<size=12><color=#999999>드로우콜은 OVR Metrics Tool로 측정</color></size>");
            return sb.ToString();
        }

        // ───────── Canvas/배치(CraneStatusHUD와 동일 패턴) ─────────
        void BuildCanvas()
        {
            canvas = CraneHud.BuildPanel(transform, "CranePerfCanvas", panelPixels, worldScale,
                bgColor, fontSize, Color.white, TextAnchor.UpperLeft, new Vector2(12, 10), out text, fitToText: true);
            text.text = "측정 준비 중...";
        }

        void TryAttachToCamera()
        {
            if (canvas == null) return;
            var cam = targetCamera != null ? targetCamera : Camera.main;
            if (cam == null)
            {
                foreach (var c in Camera.allCameras) if (c != null && c.stereoEnabled) { cam = c; break; }
                if (cam == null && Camera.allCameras.Length > 0) cam = Camera.allCameras[0];
            }
            if (cam == null) return;
            canvas.transform.SetParent(cam.transform, worldPositionStays: false);
            canvas.transform.localPosition = hmdOffset;
            CraneHud.FaceCameraChild(canvas.transform, hmdOffset, tiltPitchDeg, tiltYawDeg);
        }
    }
}
