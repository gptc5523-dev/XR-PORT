using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace AIXRCrane.Crane.Sts
{
    /// <summary>3D 부품 자동 태그·설명(WBS 6.6) — ID·씬 기하·PLC 태그·알람 코드북으로 규칙 생성. 정적 설명만(지금 상태는 CraneStateSummary), Part.note 는 사람 확인본.</summary>
    public static class PartDescriber
    {
        public sealed class Result
        {
            public string id, system, kind, at, axis = "", desc;
            public Vector3 sizeM;   // 실척 m(렌더러 바운즈 ÷ 모델 축척), 못 재면 0
            public bool assembly;   // ID 붙은 오브젝트가 메시 2개 이상 — 아니면 대표 메시 하나라 크기를 문장에 안 쓴다
            public readonly List<string> plc = new List<string>();
            public readonly List<int> alarms = new List<int>();
            public readonly List<string> causes = new List<string>();
        }

        static readonly Dictionary<string, string> Systems = new Dictionary<string, string>
        {
            { "GT", "갠트리" }, { "TR", "트롤리" }, { "HO", "권상" }, { "SP", "스프레더" },
            { "OP", "운전실" }, { "SYS", "시스템" }, { "ENV", "환경" },
        };

        static readonly Dictionary<string, string> Kinds = new Dictionary<string, string>
        {
            { "BOGIE", "주행 보기" }, { "DRIVE", "구동부" }, { "WHEEL", "차륜" }, { "FESTOON", "페스툰(급전 케이블)" },
            { "HEADBLOCK", "헤드블록" }, { "DRUM", "드럼" }, { "LOADCELL", "로드셀" }, { "TELE", "텔레스코픽 암" },
            { "TWL", "트위스트락" }, { "MH", "기계실" }, { "ANEMO", "풍속계" },
        };

        /// <summary>6.4 원인 키 ← 알람 코드. SSOT = PlcSim/gen_ai.py CAUSES(코드 있는 것) — 내보내기 점검이 대조한다.</summary>
        public static readonly Dictionary<int, string> CauseByCode = new Dictionary<int, string>
        {
            { 6001, "wind_warn" }, { 6002, "wind_stop" }, { 3012, "overload" }, { 3013, "snag" },
            { 3002, "hoist_overtemp" }, { 3003, "bearing_temp" }, { 1001, "overcurrent_gt" }, { 2001, "overcurrent_tr" },
            { 3001, "overcurrent_ho" }, { 1012, "anticollision" }, { 5001, "estop" }, { 4003, "partial_lock" },
            { 4006, "no_landing" }, { 5023, "comm_lost" },
        };

        /// <summary>위치 코드 → 사람 말. STS 는 육측/해측, RTG 는 −X/+X(손으로 쓴 표와 같은 말).</summary>
        public static string Place(string at, bool rtg)
        {
            if (string.IsNullOrEmpty(at)) return "";
            string z = at[at.Length - 1] == '1' ? "−Z" : "+Z";
            if (at.Length == 1) return z;
            string x = at[0] == 'L' ? (rtg ? "−X" : "육측") : (rtg ? "+X" : "해측");
            return x + " " + z;
        }

        /// <summary>크레인 루트·부품 표 행 → 태그·설명. root 가 없거나 ID 미부여면 축·크기만 빈다.</summary>
        public static Result Describe(Transform root, CranePartId.Part p, CranePartId.Part[] table)
        {
            bool rtg = table == CranePartId.RtgParts;
            var segs = p.id.Split('.');
            var r = new Result { id = p.id, system = segs[0], kind = segs.Length > 1 ? segs[1] : "", at = p.at };

            foreach (var t in CranePartId.Tags) if (CranePartId.Covers(t.part, p.id, table)) r.plc.Add(t.tag);
            foreach (var a in AlarmCodebook.All)
                if (CranePartId.Covers(a.part ?? "", p.id, table))
                {
                    r.alarms.Add(a.code);
                    if (CauseByCode.TryGetValue(a.code, out var c) && !r.causes.Contains(c)) r.causes.Add(c);
                }
            r.alarms.Sort();

            var hit = root != null ? CranePartId.Find(root, p.id).Find(x => x.id == p.id) : null;
            if (hit != null)
            {
                r.axis = AxisOf(hit.transform, root);
                r.sizeM = SizeM(hit.transform, out int n);
                r.assembly = n >= 2;
            }
            r.desc = Sentence(r, rtg);
            return r;
        }

        // 가장 가까운 축 이동기 — 부품이 그 축과 함께 움직인다.
        static string AxisOf(Transform t, Transform root)
        {
            for (var x = t; x != null; x = x == root ? null : x.parent)
            {
                if (x.GetComponent<SpreaderHoist>() != null) return "권상";
                if (x.GetComponent<TrolleyMover>() != null) return "횡행";
                if (x.GetComponent<GantryMover>() != null) return "갠트리 주행";
            }
            return "";
        }

        static Vector3 SizeM(Transform t, out int n)
        {
            n = 0; var b = new Bounds();
            foreach (var r in t.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer || r is LineRenderer || r is TrailRenderer) continue;
                if (n++ == 0) b = r.bounds; else b.Encapsulate(r.bounds);
            }
            return n > 0 ? b.size / StsConfig.ModelScale : Vector3.zero;
        }

        static string Sentence(Result r, bool rtg)
        {
            var sb = new StringBuilder();
            string sys = Systems.TryGetValue(r.system, out var s) ? s : r.system;
            if (r.kind == "") sb.Append(sys).Append(r.system == "OP" ? "" : " 전체");
            else sb.Append(sys).Append(" 계통 ").Append(Kinds.TryGetValue(r.kind, out var k) ? k : r.kind);
            string place = Place(r.at, rtg);
            if (place != "") sb.Append(" — ").Append(place);
            sb.Append('.');

            var parts = new List<string>();
            bool root = r.kind == "";
            if (r.axis == "갠트리 주행") parts.Add(r.system == "GT" && root ? (rtg ? "타이어로 주행" : "레일을 따라 주행") : "크레인과 함께 주행");
            else if (r.axis == "횡행") parts.Add(r.system == "TR" && root ? "거더를 따라 횡행" : "트롤리와 함께 횡행");
            else if (r.axis == "권상") parts.Add("권상 로프에 매달려 오르내림");
            if (r.assembly) parts.Add($"약 {M(r.sizeM.x)}×{M(r.sizeM.y)}×{M(r.sizeM.z)} m");
            if (parts.Count > 0) sb.Append(' ').Append(string.Join(", ", parts)).Append('.');

            if (r.plc.Count > 0)
                sb.Append(" PLC ").Append(string.Join("·", r.plc.GetRange(0, Mathf.Min(3, r.plc.Count)))).Append(r.plc.Count > 3 ? $" 외 {r.plc.Count - 3}" : "");
            if (r.alarms.Count > 0)
            {
                sb.Append(r.plc.Count > 0 ? " · 알람 " : " 알람 ");
                for (int i = 0; i < Mathf.Min(2, r.alarms.Count); i++)
                {
                    var a = AlarmCodebook.Get(r.alarms[i]);
                    sb.Append(i > 0 ? "·" : "").Append(r.alarms[i]).Append(' ').Append(a != null ? Short(a.ko, a.source) : "");
                }
                if (r.alarms.Count > 2) sb.Append($" 외 {r.alarms.Count - 2}건");
            }
            return sb.ToString();
        }

        static string M(float v) => v >= 10f ? v.ToString("0") : v.ToString("0.0");

        // 코드북 문구 앞 소스 약어("HO 과부하")는 부품 문장 안에서 중복이라 뗀다.
        static string Short(string ko, string source) => !string.IsNullOrEmpty(source) && ko.StartsWith(source + " ") ? ko.Substring(source.Length + 1) : ko;
    }
}
