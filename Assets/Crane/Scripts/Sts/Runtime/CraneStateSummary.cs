using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using AIXRCrane.Crane.Sts.Plc;

namespace AIXRCrane.Crane.Sts
{
    /// <summary>상태 요약 패널·알림(WBS 6.5) — 크레인마다 정상·주의·이상 + 요약 한 줄, 서버 /summary(AI)가 없거나 멈추면 알람 코드 규칙, 나빠지면 배너 공지.</summary>
    [DisallowMultipleComponent]
    public sealed class CraneStateSummary : MonoBehaviour
    {
        [SerializeField] Vector3 hmdOffset = new Vector3(-0.24f, 0.09f, CraneHud.HudDistance);   // 운전 상태판(오른쪽 위)과 마주 보게 왼쪽 위
        [SerializeField, Range(-30f, 30f)] float tiltYawDeg = 15f;
        [SerializeField, Range(-30f, 30f)] float tiltPitchDeg = 8f;
        [SerializeField] float worldScale = 0.00075f;
        [SerializeField] int fontSize = 18;
        [Tooltip("상태가 나빠졌을 때 알람 배너 공지를 띄워 두는 시간(초).")]
        [SerializeField] float alertSeconds = 6f;

        const float PollS = 1f, AiStaleS = 15f, AlertGraceS = 10f;   // 시작 직후 PLC 위치로 끌려가며 나는 일시 가속도 알람은 알리지 않는다
        static readonly string[] StateKo = { "정상", "주의", "이상" };

        /// <summary>서버 /summary 응답 — 6.4 추론과 맞춘 계약, JsonUtility 용 필드 이름 그대로.</summary>
        [System.Serializable]
        sealed class AiJson { public string crane; public long t; public int state; public string cause; public int code; public string text; public float conf; public string model; public string activity; public bool rule; }

        sealed class Entry
        {
            public StsCrane crane; public string name;
            public int state = -1; public string text = ""; public bool ai;
            public volatile AiJson inbox;          // 폴링 스레드가 넣고 메인 스레드가 꺼낸다
            public long aiT; public float aiFreshAt = -999f; public AiJson last;
            public volatile bool polling; public float nextPoll;
        }

        static readonly List<Entry> entries = new List<Entry>();
        Canvas canvas;
        Text text;
        string lastText;
        float nextRefresh, nextScan;
        readonly StringBuilder sb = new StringBuilder(256);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoSpawn() => CraneHud.EnsureSpawned<CraneStateSummary>("SUMMARY");

        /// <summary>모바일 관전 화면용 한 줄(색 점 + 요약문 + 출처). 아직 판정 전이면 "".</summary>
        public static string LineFor(StsCrane c)
        {
            foreach (var e in entries)
                if (e.crane == c && e.state >= 0) return Line(e);
            return "";
        }

        void Start()
        {
            canvas = CraneHud.BuildPanel(transform, "StateSummaryCanvas", new Vector2(520f, 120f), worldScale,
                new Color(0f, 0f, 0f, CraneHud.PanelBgAlpha), fontSize, Color.white, TextAnchor.UpperLeft,
                new Vector2(12, 8), out text, fitToText: true);
            canvas.enabled = false;
        }

        void OnDestroy() => entries.Clear();

        void LateUpdate()
        {
            if (canvas == null) return;
            if (canvas.transform.parent == null || canvas.transform.parent == transform)
            {
                var cam = CraneHud.HudCamera(null);
                if (cam != null) { CraneHud.AttachHeadLocked(canvas.transform, cam, hmdOffset, tiltPitchDeg, tiltYawDeg); canvas.transform.localScale = Vector3.one * worldScale; }
            }
            if (!CraneHud.Due(ref nextRefresh, CraneHud.TextHz)) return;

            if (entries.Count == 0 && Time.unscaledTime >= nextScan)
            {
                nextScan = Time.unscaledTime + 2f;
                foreach (var c in FindObjectsByType<StsCrane>(FindObjectsSortMode.None).OrderBy(c => c.name))
                    entries.Add(new Entry { crane = c, name = c.name });   // 서버 AI 문장도 GameObject 이름을 쓴다(계약)
            }

            sb.Clear();
            foreach (var e in entries)
            {
                if (e.crane == null) continue;
                Poll(e);
                Evaluate(e);
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(Line(e));
            }
            canvas.enabled = !CraneLink.FlatMode && !CraneLink.MenuOpen && sb.Length > 0;   // 모바일은 머리 고정 캔버스가 안 보여 MobileSpectatorHud 가 띄운다
            CraneHud.SetTextIfChanged(text, ref lastText, sb.ToString());
        }

        void Evaluate(Entry e)
        {
            var got = e.inbox;
            if (got != null)
            {
                e.inbox = null; e.last = got;
                if (got.t != e.aiT) { e.aiT = got.t; e.aiFreshAt = Time.unscaledTime; }
            }
            int state; string line;
            bool fresh = e.last != null && Time.unscaledTime - e.aiFreshAt < AiStaleS;
            bool ai = fresh && !e.last.rule;   // 서버가 규칙으로 낸 판정도 문장은 쓰되 출처는 '규칙'
            if (fresh) { state = Mathf.Clamp(e.last.state, 0, 2); line = e.last.text; }
            else RuleSummary(e, out state, out line);

            if (e.state >= 0 && state > e.state && Time.timeSinceLevelLoad > AlertGraceS)   // 나빠질 때만 — 좋아지는 건 패널 색으로 충분
                CraneAlarmHUD.Notify(line, StateColor(state), alertSeconds);
            e.state = state; e.text = line; e.ai = ai;
        }

        // AI 판정이 없을 때 — 활성 알람 코드(호스트 권위) → 코드북 sev(0 정상·1 주의·≥2 이상, gen_ai·서버와 같은 기준).
        static void RuleSummary(Entry e, out int state, out string line)
        {
            int code = CraneLink.ActiveAlarmCode(e.crane);
            var a = code != 0 ? AlarmCodebook.Get(code) : null;
            state = a == null ? 0 : a.sev >= 2 ? 2 : a.sev;
            line = state == 0 ? $"[정상] {e.name} — {Activity(e.crane)}" : $"[{StateKo[state]}] {e.name} — {a.ko} (코드 {code})";
        }

        // gen_ai.ACT_KO 와 같은 말 — AI 문장과 섞여 보여도 어색하지 않게.
        static string Activity(StsCrane c)
        {
            bool loaded = c.Attach != null && c.Attach.HasContainer;
            if (c.OpMode != null && c.OpMode.Current == OpMode.Running) return loaded ? "컨테이너 이송 중" : "공차 이동 중";
            return "대기 중";
        }

        // 서버 모드 PLC 를 쓰는 크레인만 — 같은 주소·토큰으로 /summary 를 묻는다. 404(판정 전)·503(AI 꺼짐)·끊김은 조용히 규칙으로.
        void Poll(Entry e)
        {
            if (e.polling || Time.unscaledTime < e.nextPoll) return;
            e.nextPoll = Time.unscaledTime + PollS;
            if (!e.crane.TryGetComponent<PlcBridge>(out var b) || !(b.Source is ServerPlcSource src) || !src.IsConnected) return;
            e.polling = true;
            src.GetText("/summary?crane=" + System.Uri.EscapeDataString(e.crane.name)).ContinueWith(t =>
            {
                if (t.Status == System.Threading.Tasks.TaskStatus.RanToCompletion)
                    try { var j = JsonUtility.FromJson<AiJson>(t.Result); if (j != null && !string.IsNullOrEmpty(j.text)) e.inbox = j; } catch { }
                e.polling = false;
            });
        }

        static string Line(Entry e) =>
            $"<color=#{CraneHud.Hex(StateColor(e.state))}>●</color> {e.text}  <color=#{CraneHud.Hex(CraneHud.HudColor.IdleDim)}>{(e.ai ? "AI" : "규칙")}</color>";

        static Color StateColor(int s) => s >= 2 ? CraneHud.HudColor.Danger : s == 1 ? CraneFault.SevColor(FaultSeverity.Warning) : CraneHud.HudColor.Ok;
    }
}
