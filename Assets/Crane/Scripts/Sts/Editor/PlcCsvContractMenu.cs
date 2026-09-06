#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Container.Crane.Sts;

namespace Container.Crane.Sts.EditorTools
{
    /// <summary>
    /// PLC CSV 스키마 계약 검사 — Python 생성기와 C# 소비자 사이의 컬럼명 계약을 대조한다.
    ///
    /// 이 결합은 함수 호출이 아니라 "디스크의 CSV 헤더 문자열"이라 컴파일러도 IDE도 못 잡는다.
    /// generate.py에서 컬럼명 하나만 바뀌어도 <see cref="Plc.CsvReplaySource"/>는 그 태그를 0으로 폴백해
    /// 크레인이 조용히 안 움직인다(축 정지·알람 미발생). 그 침묵 실패를 사전에 판정하는 게 목적.
    ///
    /// 3자 대조:
    ///   ① 생산자 선언 : PlcSim/generate.py 의 CSV_FIELDS 리스트
    ///   ② 생산자 산출 : PlcSim/output/&lt;Sxx&gt;/run_NN.csv 의 실제 헤더 행
    ///   ③ 소비자 기대 : CsvReplaySource.cs 의 F("..")/I("..")/B("..") 호출 키
    ///
    /// 판정(grep 키 "[QA]", "=> FAIL"):
    ///   C1 ①파싱      — CSV_FIELDS를 읽었고 중복/빈 이름이 없다
    ///   C2 ③파싱      — 소비자 키를 1개 이상 추출했다
    ///   C3 ③⊆①       — 소비자가 기대하는 키가 전부 생산된다   ★침묵 실패 방지 핵심
    ///   C4 ②==①      — 실제 CSV 헤더가 선언과 순서까지 일치한다(전 파일)
    ///   I5 ①∖③       — 생성만 되고 안 쓰이는 컬럼(정보성 — 확장 여지이지 결함 아님)
    ///
    /// 정적 검사라 씬·Play 모드가 필요 없다. 파일이 없으면 FAIL이 아니라 경고 후 중단(환경 문제와 계약 위반 구분).
    /// </summary>
    public static class PlcCsvContractMenu
    {
        const string GeneratorRel = "PlcSim/generate.py";
        const string ConsumerRel  = "Assets/Crane/Scripts/Sts/Runtime/Plc/CsvReplaySource.cs";
        const string OutputRel    = "PlcSim/output";

        const string Sys = "PLCCSV";

        [MenuItem("PLC/CSV 스키마 계약 검사", false, 4)]
        public static void Run()
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string genPath = Path.Combine(root, GeneratorRel);
            string conPath = Path.Combine(root, ConsumerRel);
            string outDir  = Path.Combine(root, OutputRel);

            if (!File.Exists(genPath)) { Debug.LogWarning($"[{Sys}] 생성기를 못 찾음: {GeneratorRel} — 검사 중단."); return; }
            if (!File.Exists(conPath)) { Debug.LogWarning($"[{Sys}] 소비자를 못 찾음: {ConsumerRel} — 검사 중단."); return; }

            // ── ① 생산자 선언 ─────────────────────────────────────────────
            var declared = ParseCsvFields(File.ReadAllText(genPath));
            var dupDecl = declared.GroupBy(s => s).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            bool c1 = declared.Count > 0 && dupDecl.Count == 0 && !declared.Any(string.IsNullOrWhiteSpace);
            QaLog.Check(Sys, "DECL", c1, $"fields={declared.Count} dup={dupDecl.Count}");
            if (declared.Count == 0)
            {
                Debug.LogWarning($"[{Sys}] {GeneratorRel} 에서 CSV_FIELDS 리스트를 못 읽었습니다 — 생성기 구조가 바뀌었는지 확인.");
                return;
            }

            // ── ③ 소비자 기대 ─────────────────────────────────────────────
            var expected = ParseConsumerKeys(File.ReadAllText(conPath));
            bool c2 = expected.Count > 0;
            QaLog.Check(Sys, "CONSUMER", c2, $"keys={expected.Count}");
            if (!c2)
            {
                Debug.LogWarning($"[{Sys}] {ConsumerRel} 에서 F/I/B 컬럼 키를 못 읽었습니다 — 파서 구조가 바뀌었는지 확인.");
                return;
            }

            // ── C3 소비자 ⊆ 생산자 (침묵 실패 방지 핵심) ──────────────────
            var declSet = new HashSet<string>(declared);
            var orphan = expected.Where(k => !declSet.Contains(k)).ToList();   // 생산 안 되는데 읽음 → 조용히 0
            QaLog.Check(Sys, "CONTRACT", orphan.Count == 0,
                        $"expected={expected.Count} missing={orphan.Count}" +
                        (orphan.Count > 0 ? " cols=" + string.Join(",", orphan) : ""));

            // ── C4 실제 헤더 == 선언 (전 파일) ────────────────────────────
            var csvFiles = Directory.Exists(outDir)
                ? Directory.GetFiles(outDir, "run_*.csv", SearchOption.AllDirectories).OrderBy(p => p).ToArray()
                : new string[0];

            var headerBad = new List<string>();
            foreach (var f in csvFiles)
            {
                string head = File.ReadLines(f).FirstOrDefault();
                var cols = string.IsNullOrEmpty(head)
                    ? new List<string>()
                    : head.Split(',').Select(s => s.Trim()).ToList();

                if (!cols.SequenceEqual(declared))
                {
                    string rel = f.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar);
                    var miss  = declared.Except(cols).ToList();
                    var extra = cols.Except(declared).ToList();
                    string why = miss.Count == 0 && extra.Count == 0
                        ? "순서 불일치"
                        : $"누락[{string.Join(",", miss)}] 잉여[{string.Join(",", extra)}]";
                    headerBad.Add($"{rel} (n={cols.Count}) — {why}");
                }
            }
            if (csvFiles.Length == 0)
                Debug.LogWarning($"[{Sys}] {OutputRel} 에 run_*.csv 가 없습니다 — 헤더 검사(C4) 건너뜀. 'python3 PlcSim/generate.py' 로 생성.");
            else
                QaLog.Check(Sys, "HEADER", headerBad.Count == 0, $"files={csvFiles.Length} bad={headerBad.Count}");

            // ── I5 미소비 컬럼(정보성) ────────────────────────────────────
            var expSet = new HashSet<string>(expected);
            var unused = declared.Where(c => !expSet.Contains(c)).ToList();
            QaLog.Info(Sys, "UNUSED", $"count={unused.Count}/{declared.Count}");

            // ── 사람이 읽는 블록 보고 ─────────────────────────────────────
            var sb = new StringBuilder(1024);
            sb.AppendLine($"═══ [{Sys}] PLC CSV 스키마 계약 검사 ═══");
            sb.AppendLine($"  ① 생성기 선언 : {declared.Count}개  ({GeneratorRel} · CSV_FIELDS)");
            sb.AppendLine($"  ② 실제 산출   : {csvFiles.Length}개 파일  ({OutputRel}/**/run_*.csv)");
            sb.AppendLine($"  ③ 소비자 기대 : {expected.Count}개  ({Path.GetFileName(ConsumerRel)} · F/I/B 키)");

            sb.AppendLine($"\n■ C3 소비자⊆생산자 — {(orphan.Count == 0 ? "PASS" : $"FAIL {orphan.Count}건")}");
            if (orphan.Count == 0) sb.AppendLine("  ✔ 소비자가 읽는 컬럼이 전부 생산됩니다(침묵 0 폴백 없음).");
            else foreach (var o in orphan) sb.AppendLine($"  ✖ {o} — CSV에 없음 → 항상 0으로 재생됨(축 정지·알람 미발생)");

            if (csvFiles.Length > 0)
            {
                sb.AppendLine($"\n■ C4 헤더==선언 — {(headerBad.Count == 0 ? "PASS" : $"FAIL {headerBad.Count}/{csvFiles.Length} 파일")}");
                foreach (var b in headerBad) sb.AppendLine($"  ✖ {b}");
                if (headerBad.Count == 0) sb.AppendLine($"  ✔ {csvFiles.Length}개 파일 헤더가 선언과 순서까지 동일합니다.");
            }

            sb.AppendLine($"\n■ I5 생성되나 미소비 — {unused.Count}개 (결함 아님 · 소비자 확장 여지)");
            foreach (var u in unused) sb.AppendLine($"  ○ {u}");

            if (dupDecl.Count > 0)
                sb.AppendLine($"\n■ C1 선언 중복 — {string.Join(",", dupDecl)}");

            sb.AppendLine("\n※ 이 계약은 파일 경유라 컴파일 검사에 안 잡힙니다. generate.py 의 컬럼명·순서를 바꾸면 반드시 재실행하십시오.");
            Debug.Log(sb.ToString());
        }

        // generate.py 의 `CSV_FIELDS = [ "a", "b", ... ]` 리스트 리터럴에서 컬럼명을 순서대로 추출.
        // 주석(#) 안의 문자열이 섞여 들어가지 않도록 슬라이스를 먼저 정리한다.
        static List<string> ParseCsvFields(string py)
        {
            var result = new List<string>();
            var m = Regex.Match(py, @"^\s*CSV_FIELDS\s*=\s*\[", RegexOptions.Multiline);
            if (!m.Success) return result;

            int open = py.IndexOf('[', m.Index);
            int depth = 0, end = -1;
            for (int i = open; i < py.Length; i++)
            {
                if (py[i] == '[') depth++;
                else if (py[i] == ']') { depth--; if (depth == 0) { end = i; break; } }
            }
            if (end < 0) return result;

            string body = StripHashComments(py.Substring(open + 1, end - open - 1));
            foreach (Match s in Regex.Matches(body, "\"([^\"]*)\"|'([^']*)'"))
                result.Add(s.Groups[1].Success && s.Groups[1].Length > 0 ? s.Groups[1].Value : s.Groups[2].Value);
            return result;
        }

        // CsvReplaySource 의 F("..") / I("..") / B("..") 호출 키를 중복 없이 추출(선언부 F(string name)은 미매칭).
        static List<string> ParseConsumerKeys(string cs)
        {
            var seen = new HashSet<string>();
            var result = new List<string>();
            foreach (Match m in Regex.Matches(StripSlashComments(cs), @"(?<![A-Za-z0-9_.])[FIB]\(\s*""([^""]+)""\s*\)"))
            {
                string k = m.Groups[1].Value;
                if (seen.Add(k)) result.Add(k);
            }
            return result;
        }

        // 문자열 리터럴 안의 '#'은 보존하고 파이썬 주석만 제거.
        static string StripHashComments(string src)
        {
            var sb = new StringBuilder(src.Length);
            char quote = '\0';
            for (int i = 0; i < src.Length; i++)
            {
                char c = src[i];
                if (quote != '\0') { sb.Append(c); if (c == quote) quote = '\0'; continue; }
                if (c == '"' || c == '\'') { quote = c; sb.Append(c); continue; }
                if (c == '#') { while (i < src.Length && src[i] != '\n') i++; sb.Append('\n'); continue; }
                sb.Append(c);
            }
            return sb.ToString();
        }

        // 문자열 리터럴 안의 '//'는 보존하고 C# 주석(// 및 /* */)만 제거.
        static string StripSlashComments(string src)
        {
            var sb = new StringBuilder(src.Length);
            bool inStr = false;
            for (int i = 0; i < src.Length; i++)
            {
                char c = src[i];
                if (inStr)
                {
                    sb.Append(c);
                    if (c == '\\' && i + 1 < src.Length) { sb.Append(src[++i]); continue; }
                    if (c == '"') inStr = false;
                    continue;
                }
                if (c == '"') { inStr = true; sb.Append(c); continue; }
                if (c == '/' && i + 1 < src.Length && src[i + 1] == '/')
                { while (i < src.Length && src[i] != '\n') i++; sb.Append('\n'); continue; }
                if (c == '/' && i + 1 < src.Length && src[i + 1] == '*')
                { i += 2; while (i + 1 < src.Length && !(src[i] == '*' && src[i + 1] == '/')) i++; i++; sb.Append(' '); continue; }
                sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
#endif
