#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AIXRCrane.Crane.Sts.EditorTools
{
    /// <summary>PLC ↔ 3D 부품 ID — 씬 STS 에 ID 붙이기, 매핑표(CranePartId 의 표가 SSOT)를 문서로 내보내기.</summary>
    public static class CranePartIdMenu
    {
        const string DocRel = "문서/PLC_부품ID_매핑.md";

        [MenuItem("PLC/부품 ID 부여 (STS·RTG)", false, 20)]
        public static void StampScene()
        {
            var cranes = Object.FindObjectsByType<StsCrane>();
            if (cranes.Length == 0) { Debug.LogWarning("[PartId] 씬에 크레인 없음"); return; }
            foreach (var c in cranes)
            {
                int missing = CranePartId.Stamp(c.transform);
                EditorSceneManager.MarkSceneDirty(c.gameObject.scene);
                string why = CranePartId.Verify(c.transform);
                QaLog.Check("PARTID", "stamp", missing == 0 && why == "", $"crane={c.name} parts={CranePartId.PartsFor(c.transform).Length} missing={missing} {why}");
            }
        }

        [MenuItem("PLC/부품 ID 매핑표 내보내기", false, 21)]
        public static void Export()
        {
            var alarms = new Dictionary<string, List<int>>();
            foreach (var a in AlarmCodebook.All)
            {
                string k = a.part ?? "";
                if (!alarms.TryGetValue(k, out var l)) alarms[k] = l = new List<int>();
                l.Add(a.code);
            }
            var tagsOf = new Dictionary<string, List<string>>();
            foreach (var t in CranePartId.Tags)
            {
                if (!tagsOf.TryGetValue(t.part, out var l)) tagsOf[t.part] = l = new List<string>();
                l.Add(t.tag);
            }

            var sb = new StringBuilder();
            sb.AppendLine("# PLC ↔ 3D 부품 ID 매핑");
            sb.AppendLine();
            sb.AppendLine("> 자동 생성 — 직접 고치지 말고 `Assets/Crane/Scripts/Sts/Runtime/CranePartId.cs`(부품·태그)와 `Assets/Crane/Resources/Sts/AlarmCodebook.json`(알람 `part`)을 고친 뒤 Unity 메뉴 **PLC/부품 ID 매핑표 내보내기**.");
            sb.AppendLine();
            sb.AppendLine("## ID 체계");
            sb.AppendLine();
            sb.AppendLine("- 형식 `계통.부품[.위치]` — 계통은 PLC 태그 접두·알람 소스와 같다: `GT` 주행 · `TR` 횡행 · `HO` 권상 · `SP` 스프레더 · `OP` 운전 · `SYS` 시스템 · `ENV` 환경.");
            sb.AppendLine("- 위치: `L`/`S` = 육측(−X)/해측(+X), `1`/`2` = −Z/+Z. 같은 이름 부품들의 중심 기준으로 고르므로 생성 순서 번호(`_1`, `_2`…)와 무관하다.");
            sb.AppendLine("- 그룹 ID(`GT.BOGIE`)는 그 ID로 시작하는 부품 전부를 가리킨다. 태그·알람이 어느 바퀴인지 주지 않을 때 쓴다.");
            sb.AppendLine("- 크레인이 여러 대면 서버 전체 ID = `크레인 루트 이름` + `.` + 부품 ID (예: `STS_Crane.GT.BOGIE.L1`).");
            sb.AppendLine("- ID는 씬의 GameObject에 `CranePartId` 컴포넌트로 붙는다. 크레인 생성(StsCraneCreator) 끝에서 자동으로, 기존 씬은 메뉴 **PLC/부품 ID 부여 (STS·RTG)**. 플레이 시작 시 `[QA] PARTID/selfcheck`가 표와 씬을 대조한다.");
            sb.AppendLine();
            sb.AppendLine("## 1. PLC 태그 → 부품 ID");
            sb.AppendLine();
            sb.AppendLine("주소는 ㈜엠비이 데이터 포인트 리스트(MBE-DOC-2026-XR-002) 원본에서 확인된 것만 적었다. 빈 칸 = 원본 확인 필요.");
            sb.AppendLine();
            sb.AppendLine("| PLC 태그 | 주소 | 형식·단위 | 부품 ID | 비고 |");
            sb.AppendLine("|---|---|---|---|---|");
            foreach (var t in CranePartId.Tags)
                sb.AppendLine($"| `{t.tag}` | {t.address} | {t.type} | {(t.part == "" ? "—" : "`" + t.part + "`")} | {t.note} |");
            sb.AppendLine();
            sb.AppendLine("## 2. 부품 ID → 3D 부품");
            sb.AppendLine();
            sb.AppendLine("| 부품 ID | 3D 이름(번호 제외) | 위치 | 설명 | PLC 태그 | 알람 코드 |");
            sb.AppendLine("|---|---|---|---|---|---|");
            foreach (var p in CranePartId.Parts)
                sb.AppendLine($"| `{p.id}` | `{p.baseName}` | {(p.at == "" ? "유일" : p.at)} | {p.note} | {Join(TagsFor(tagsOf, p.id))} | {Codes(AlarmsFor(alarms, p.id))} |");
            sb.AppendLine();
            sb.AppendLine("### RTG (FBX)");
            sb.AppendLine();
            sb.AppendLine($"같은 ID를 쓴다. 이름 끝 `*` = 그 접두 부품들 중 위치로 하나, 빈 이름 = 크레인 루트. 위치 코드는 기하 기준(L/S = −X/+X). RTG 모델에 없는 부품: {Join(new List<string>(CranePartId.RtgAbsent))} — 이 알람은 RTG에서 강조할 부품이 없다.");
            sb.AppendLine();
            sb.AppendLine("| 부품 ID | 3D 이름 | 위치 | 설명 |");
            sb.AppendLine("|---|---|---|---|");
            foreach (var p in CranePartId.RtgParts)
                sb.AppendLine($"| `{p.id}` | {(p.baseName == "" ? "(루트)" : "`" + p.baseName + "`")} | {(p.at == "" ? "유일" : p.at)} | {p.note} |");
            sb.AppendLine();
            sb.AppendLine("## 3. 알람 코드 → 부품 ID");
            sb.AppendLine();
            sb.AppendLine("3D에 없는 구성품(인버터·케이블릴·변압기 등)은 가장 가까운 상위 부품에 붙였다. `—` = 부품이 아닌 알람(통신·로그인·기상 등).");
            sb.AppendLine();
            sb.AppendLine("| 부품 ID | 개수 | 알람 코드 |");
            sb.AppendLine("|---|---|---|");
            var keys = new List<string>(alarms.Keys);
            keys.Sort(System.StringComparer.Ordinal);
            foreach (var k in keys)
                sb.AppendLine($"| {(k == "" ? "—" : "`" + k + "`")} | {alarms[k].Count} | {Codes(alarms[k])} |");
            sb.AppendLine();
            sb.AppendLine("## 확인 필요");
            sb.AppendLine();
            sb.AppendLine("- 태그 주소 27개 — 데이터 포인트 리스트 원본.");
            sb.AppendLine("- 로드셀 #1~#4(3015~3018)의 물리 위치 — 지금은 #1=L1, #2=L2, #3=S1, #4=S2로 가정.");
            sb.AppendLine("- `GT_Direction` 1=Stern 이 크레인 ±Z 중 어느 쪽인지 — 선박 접안 방향에 따름.");
            sb.AppendLine("- RTG(FBX) 크레인 부품 ID — 아직 표 없음.");

            string path = Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..")), DocRel);
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..")), JsonRel), PartsJson(), new UTF8Encoding(false));
            Debug.Log($"[PartId] 매핑표 내보냄 → {DocRel}, {JsonRel} (부품 {CranePartId.Parts.Length}, 태그 {CranePartId.Tags.Length}, 알람 {AlarmCodebook.All.Length})");
        }

        const string JsonRel = "Server/parts.json";   // 통합서버 /parts 가 읽는다 — 서버가 XR 과 같은 표로 부품 상태를 낸다

        static string PartsJson()
        {
            var sb = new StringBuilder("{\n  \"sts\": [");
            AppendParts(sb, CranePartId.Parts);
            sb.Append("],\n  \"rtg\": [");
            AppendParts(sb, CranePartId.RtgParts);
            sb.Append("],\n  \"rtgAbsent\": [");
            for (int i = 0; i < CranePartId.RtgAbsent.Length; i++) sb.Append(i > 0 ? ", " : "").Append(Q(CranePartId.RtgAbsent[i]));
            sb.Append("],\n  \"tags\": [");
            for (int i = 0; i < CranePartId.Tags.Length; i++)
            {
                var t = CranePartId.Tags[i];
                sb.Append(i > 0 ? "," : "").Append($"\n    {{\"tag\": {Q(t.tag)}, \"type\": {Q(t.type)}, \"part\": {Q(t.part)}, \"note\": {Q(t.note)}}}");
            }
            return sb.Append("\n  ]\n}\n").ToString();
        }

        static void AppendParts(StringBuilder sb, CranePartId.Part[] parts)
        {
            for (int i = 0; i < parts.Length; i++)
                sb.Append(i > 0 ? "," : "").Append($"\n    {{\"id\": {Q(parts[i].id)}, \"note\": {Q(parts[i].note)}}}");
        }

        static string Q(string v) => "\"" + (v ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        // 이 ID 를 가리키는 태그·알람 — 정확히 이 ID 이거나 부품 아닌 그룹 ID. 부품인 상위 ID(GT)는 자기 행에만.
        static bool Covers(string key, string id)
        {
            if (key == "") return false;
            if (id == key) return true;
            if (!id.StartsWith(key + ".")) return false;
            foreach (var p in CranePartId.Parts) if (p.id == key) return false;
            return true;
        }

        static List<string> TagsFor(Dictionary<string, List<string>> m, string id)
        {
            var r = new List<string>();
            foreach (var kv in m) if (Covers(kv.Key, id)) r.AddRange(kv.Value);
            return r;
        }

        static List<int> AlarmsFor(Dictionary<string, List<int>> m, string id)
        {
            var r = new List<int>();
            foreach (var kv in m) if (Covers(kv.Key, id)) r.AddRange(kv.Value);
            r.Sort();
            return r;
        }

        static string Join(List<string> tags) => tags.Count == 0 ? "" : "`" + string.Join("` `", tags) + "`";
        static string Codes(List<int> c) => string.Join(" ", c);
    }
}
#endif
