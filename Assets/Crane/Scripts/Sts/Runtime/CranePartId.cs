using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace AIXRCrane.Crane.Sts
{
    /// <summary>PLC ↔ 3D 부품 ID("계통.부품[.위치]") — 이 표(Parts·Tags)가 매핑 SSOT, 문서/PLC_부품ID_매핑.md 는 내보낸 것.
    /// 위치는 크레인 루트 좌표로 고른다(L/S=육측−X/해측+X, 1/2=−Z/+Z) — 생성 순서가 바뀌어도 같은 부품. 그룹 ID 는 접두 일치 전부.</summary>
    [DisallowMultipleComponent]
    public sealed class CranePartId : MonoBehaviour
    {
        public string id;

        public readonly struct Part
        {
            public readonly string id, baseName, at, note;
            public Part(string id, string baseName, string at, string note) { this.id = id; this.baseName = baseName; this.at = at; this.note = note; }
        }

        public readonly struct Tag
        {
            public readonly string tag, address, type, part, note;
            public Tag(string tag, string address, string type, string part, string note) { this.tag = tag; this.address = address; this.type = type; this.part = part; this.note = note; }
        }

        /// <summary>STS 부품 — id, 생성 이름(번호 뗀 것), 위치(""=유일), 설명.</summary>
        public static readonly Part[] Parts =
        {
            new Part("GT",             StsPartNames.StsCraneRoot, "",   "갠트리 주행 전체(크레인 루트, GantryMover)"),
            new Part("GT.BOGIE.L1",    "Bogie_Pivot",        "L1", "주행 보기 — 육측 −Z"),
            new Part("GT.BOGIE.L2",    "Bogie_Pivot",        "L2", "주행 보기 — 육측 +Z"),
            new Part("GT.BOGIE.S1",    "Bogie_Pivot",        "S1", "주행 보기 — 해측 −Z"),
            new Part("GT.BOGIE.S2",    "Bogie_Pivot",        "S2", "주행 보기 — 해측 +Z"),
            new Part("TR",             "Trolley",            "",   "트롤리 전체(TrolleyMover)"),
            new Part("TR.DRIVE",       "Travel_Bedplate",    "",   "횡행 구동부(기계실 — 로프 견인식 모터·브레이크·인코더)"),
            new Part("TR.WHEEL.L1",    "Trolley_Bogie",      "L1", "트롤리 차륜 — 육측 −Z"),
            new Part("TR.WHEEL.L2",    "Trolley_Bogie",      "L2", "트롤리 차륜 — 육측 +Z"),
            new Part("TR.WHEEL.S1",    "Trolley_Bogie",      "S1", "트롤리 차륜 — 해측 −Z"),
            new Part("TR.WHEEL.S2",    "Trolley_Bogie",      "S2", "트롤리 차륜 — 해측 +Z"),
            new Part("TR.FESTOON",     "Trolley_FestoonBox", "",   "트롤리 페스툰(급전 케이블)"),
            new Part("HO",             "SpreaderRoot",       "",   "권상 로프 계통(HoistRopeRig)"),
            new Part("HO.HEADBLOCK",   "Spreader_Head",      "",   "헤드블록 — 권상 위치·하중이 실리는 곳"),
            new Part("HO.DRIVE",       "Hoist_Bedplate",     "",   "권상 구동부(기계실 — 모터·브레이크·인코더)"),
            new Part("HO.DRUM.1",      "Drum_Pedestal",      "1",  "권상 드럼 — −Z"),
            new Part("HO.DRUM.2",      "Drum_Pedestal",      "2",  "권상 드럼 — +Z"),
            new Part("HO.LOADCELL.L1", "Head_Rope_Socket",   "L1", "로드셀 #1(가정) — 헤드블록 로프 소켓 육측 −Z"),
            new Part("HO.LOADCELL.L2", "Head_Rope_Socket",   "L2", "로드셀 #2(가정) — 육측 +Z"),
            new Part("HO.LOADCELL.S1", "Head_Rope_Socket",   "S1", "로드셀 #3(가정) — 해측 −Z"),
            new Part("HO.LOADCELL.S2", "Head_Rope_Socket",   "S2", "로드셀 #4(가정) — 해측 +Z"),
            new Part("SP",             "Spreader",           "",   "스프레더 전체(텔레스코프·잠금 애니)"),
            new Part("SP.TELE.1",      "TeleArm_R",          "",   "텔레스코픽 암 — −Z"),
            new Part("SP.TELE.2",      "TeleArm_L",          "",   "텔레스코픽 암 — +Z"),
            new Part("SP.TWL.L1",      StsPartNames.TwistlockCone, "L1", "트위스트락 — 육측 −Z"),
            new Part("SP.TWL.L2",      StsPartNames.TwistlockCone, "L2", "트위스트락 — 육측 +Z"),
            new Part("SP.TWL.S1",      StsPartNames.TwistlockCone, "S1", "트위스트락 — 해측 −Z"),
            new Part("SP.TWL.S2",      StsPartNames.TwistlockCone, "S2", "트위스트락 — 해측 +Z"),
            new Part("OP",             StsPartNames.OperatorCab, "",   "운전실(콘솔·비상정지·운전모드)"),
            new Part("SYS.MH",         StsPartNames.MachineryHouse, "",   "기계실(전원·PLC·안전 CPU·화재감지)"),
            new Part("ENV.ANEMO",      "Anemo_Hub",          "",   "풍속계(아펙스 상부)"),
        };

        /// <summary>PLC 태그 — ㈜엠비이 데이터 포인트 리스트(MBE-DOC-2026-XR-002) DB100/DB101 의 XR 대상 31개(CsvReplaySource 열 이름).
        /// 주소는 IPlcSource 에 적힌 4개만 안다 — 나머지는 원본 확인 필요. part ""= 부품이 아니다.</summary>
        public static readonly Tag[] Tags =
        {
            new Tag("GT_Position",           "", "REAL m",    "GT",             ""),
            new Tag("GT_Velocity",           "", "REAL m/s",  "GT",             "부호 = 방향"),
            new Tag("GT_Running",            "", "BOOL",      "GT",             ""),
            new Tag("GT_Direction",          "", "BOOL",      "GT",             "1 = Stern"),
            new Tag("TR_Position",           "", "REAL m",    "TR",             ""),
            new Tag("TR_Velocity",           "", "REAL m/s",  "TR",             ""),
            new Tag("TR_Running",            "", "BOOL",      "TR",             ""),
            new Tag("TR_Direction",          "", "BOOL",      "TR",             "1 = Sea"),
            new Tag("HO_Position",           "", "REAL m",    "HO.HEADBLOCK",   ""),
            new Tag("HO_Velocity",           "", "REAL m/s",  "HO.HEADBLOCK",   ""),
            new Tag("HO_Running",            "", "BOOL",      "HO.HEADBLOCK",   ""),
            new Tag("HO_Direction",          "", "BOOL",      "HO.HEADBLOCK",   "1 = Up"),
            new Tag("HO_Load",               "DB100.DBD70", "REAL t", "HO.HEADBLOCK", "4로프 합산"),
            new Tag("SP_Mode",               "DB100.DBW100", "INT",   "SP",     "0=20ft 1=40ft 2=45ft 3=Twin"),
            new Tag("SP_TwistLock_Locked",   "", "BOOL",      "SP.TWL",         "4개 묶음"),
            new Tag("SP_TwistLock_Unlocked", "", "BOOL",      "SP.TWL",         "4개 묶음"),
            new Tag("SP_Landed",             "", "BOOL",      "SP.TWL",         "착지 핀 = 트위스트락 모서리"),
            new Tag("SP_Container_Detected", "", "BOOL",      "SP",             ""),
            new Tag("OP_Mode",               "DB100.DBW130", "INT",   "OP",     "0=수동 1=반자동 2=자동 3=정비"),
            new Tag("OP_Ready",              "", "BOOL",      "OP",             ""),
            new Tag("OP_Running",            "", "BOOL",      "OP",             ""),
            new Tag("OP_Standby",            "", "BOOL",      "OP",             ""),
            new Tag("OP_Emergency_Stop",     "", "BOOL",      "OP",             "운전실 비상정지"),
            new Tag("ENV_Wind_Speed",        "", "REAL m/s",  "ENV.ANEMO",      ""),
            new Tag("ENV_Wind_Direction",    "", "REAL deg",  "ENV.ANEMO",      ""),
            new Tag("ENV_Wind_Alarm",        "", "BOOL",      "ENV.ANEMO",      ""),
            new Tag("ALM_Active",            "", "BOOL",      "",               "DB101 — 부품은 알람 코드북 part 칸"),
            new Tag("ALM_Latest_Code",       "", "INT",       "",               "DB101 — 코드 → 코드북 part"),
            new Tag("ALM_Latest_Severity",   "", "INT",       "",               "DB101"),
            new Tag("ALM_Latest_Source",     "", "INT",       "",               "DB101 — 1=GT 2=TR 3=HO 4=SP 5=SYS 6=ENV"),
            new Tag("COM_Link_Status",       "DB100.DBX200.0", "BOOL", "",      "통신 상태 — 부품 아님"),
        };

        /// <summary>크레인 루트 아래 부품에 ID 를 붙인다(기존 ID 는 지우고 다시). 못 찾거나 위치가 겹친 부품 수를 돌려준다.</summary>
        public static int Stamp(Transform root)
        {
            foreach (var old in root.GetComponentsInChildren<CranePartId>(true)) DestroyImmediate(old);
            var byBase = new Dictionary<string, List<Transform>>();
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                string b = CraneHud.BaseName(t.name);
                if (!byBase.TryGetValue(b, out var l)) byBase[b] = l = new List<Transform>();
                l.Add(t);
            }
            int missing = 0;
            foreach (var p in Parts)
            {
                var hit = byBase.TryGetValue(p.baseName, out var all) ? Select(root, all, p.at) : null;
                if (hit == null) { missing++; Debug.LogWarning($"[PartId] {p.id}: '{p.baseName}' {p.at} 없음 또는 여러 개"); continue; }
                hit.gameObject.AddComponent<CranePartId>().id = p.id;
            }
            return missing;
        }

        // 위치 코드로 하나 고르기 — 같은 이름 부품들의 중심(크레인 루트 좌표) 기준 부호. 딱 하나가 아니면 null.
        static Transform Select(Transform root, List<Transform> all, string at)
        {
            if (at == "") return all.Count == 1 ? all[0] : null;
            Vector3 c = Vector3.zero;
            foreach (var t in all) c += root.InverseTransformPoint(t.position);
            c /= all.Count;
            Transform hit = null;
            foreach (var t in all)
            {
                Vector3 d = root.InverseTransformPoint(t.position) - c;
                string code = at.Length == 2 ? (d.x < 0 ? "L" : "S") + (d.z < 0 ? "1" : "2") : (d.z < 0 ? "1" : "2");
                if (code != at) continue;
                if (hit != null) return null;
                hit = t;
            }
            return hit;
        }

        /// <summary>ID 또는 그룹 ID 로 부품 찾기 — "SP.TWL" 은 SP.TWL.* 네 개.</summary>
        public static List<CranePartId> Find(Transform root, string id)
        {
            var r = new List<CranePartId>();
            if (string.IsNullOrEmpty(id)) return r;
            foreach (var p in root.GetComponentsInChildren<CranePartId>(true))
                if (p.id == id || p.id.StartsWith(id + ".")) r.Add(p);
            return r;
        }

        /// <summary>표 ↔ 씬 대조 — 부품 ID 는 정확히 1개, 태그·알람 부품은 1개 이상 찾아져야 한다. 문제 목록(빈 문자열 = 통과).</summary>
        public static string Verify(Transform root)
        {
            if (root.GetComponentInChildren<CranePartId>(true) == null) return "ID 없음 — 메뉴 PLC/부품 ID 부여 (STS) 후 씬 저장";
            var sb = new StringBuilder();
            foreach (var p in Parts)
            {
                int n = 0;
                foreach (var x in root.GetComponentsInChildren<CranePartId>(true)) if (x.id == p.id) n++;
                if (n != 1) sb.Append($"{p.id}×{n} ");
            }
            foreach (var t in Tags)
                if (t.part != "" && Find(root, t.part).Count == 0) sb.Append($"{t.tag}→{t.part}? ");
            foreach (var a in AlarmCodebook.All)
                if (!string.IsNullOrEmpty(a.part) && Find(root, a.part).Count == 0) sb.Append($"{a.code}→{a.part}? ");
            return sb.ToString();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            foreach (var c in FindObjectsByType<StsCrane>())
            {
                if (c.GetComponent<RtgBogieSteering>() != null) continue;   // ponytail: STS 만 — RTG(FBX) 부품 ID 는 표가 따로 필요할 때
                string why = Verify(c.transform);
                QaLog.Check("PARTID", "selfcheck", why == "", $"crane={c.name} parts={Parts.Length} tags={Tags.Length} {why}");
            }
        }
    }
}
