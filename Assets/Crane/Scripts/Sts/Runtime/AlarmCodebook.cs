using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using ContainerProject;

namespace Container.Crane.Sts
{
    /// <summary>코드북 단일 항목(JSON 1행과 1:1). ㈜엠비이 코드북 MBE-DOC-2026-XR-002 #65.</summary>
    [Serializable]
    public class AlarmEntry
    {
        public int code;          // 4자리. 천 단위 = 소스(1=GT,2=TR,3=HO,4=SP,5=SYS,6=ENV)
        public string source;     // "GT" | "TR" | "HO" | "SP" | "SYS" | "ENV"
        public int sev;           // 0=Info,1=Warning,2=Critical,3=Fatal
        public bool autoStop;     // 발생 시 PLC 자동 정지 여부
        public bool xr;           // XR 단말 시각화 대상(코드북 취소선=false)
        public string ko;         // 한글 설명
        public string en;         // 영문 약어
        public string scenario;   // MBE-DOC-2026-XR-004 시나리오 ID(빈 문자열=해당 없음)
        public string note;       // 비고(XR 비대상 사유 등)

        public FaultSeverity Severity => (FaultSeverity)Mathf.Clamp(sev, 0, 3);

        /// <summary>HUD 한 줄 표기: "[3012] HO 과부하 (정격 초과)".</summary>
        public override string ToString() => $"[{code}] {ko}";
    }

    [Serializable]
    class AlarmBookData
    {
        public string doc;
        public int totalDefined;
        public AlarmEntry[] entries;
    }

    /// <summary>
    /// 알람 코드북(170개) 단일 출처(SSOT). Resources의 JSON을 1회 로드해 코드→항목으로 색인.
    /// ※ 코드·심각도·메시지는 임의 정의 금지 — 항상 이 데이터(=㈜엠비이 코드북)를 따른다.
    /// 개정 시 JSON만 교체하면 런타임 전체에 반영됨.
    ///
    /// 표시·라벨 규칙은 코드북 §7(우선순위/색)·§8(AI 라벨)을 따르며,
    /// 색/등급 매핑은 [[CraneFault]]의 SevColor/ToGrade를 재사용해 단일화한다.
    /// </summary>
    public static class AlarmCodebook
    {
        const string ResourcePath = "Sts/AlarmCodebook";   // Assets/Crane/Resources/Sts/AlarmCodebook.json

        static Dictionary<int, AlarmEntry> _byCode;
        static AlarmEntry[] _all;
        static string _doc;
        static bool _loadOk;   // 코드북이 정상 로드·파싱돼 항목이 1개 이상 색인됐는가(=SSOT 가용).

        static void EnsureLoaded()
        {
            if (_all != null) return;

            var ta = Resources.Load<TextAsset>(ResourcePath);
            if (ta == null)
            {
                Debug.LogError($"[AlarmCodebook] Resources/{ResourcePath}.json 을 찾지 못함 — 폴더명 'Resources' 하위 경로 확인.");
                _all = Array.Empty<AlarmEntry>();
                _byCode = new Dictionary<int, AlarmEntry>();
                _loadOk = false;   // fail-to-safe: 못 찾으면 '알람 시스템 오프라인'으로 본다(조용한 무효화 금지).
                return;
            }

            AlarmBookData data = null;
            try { data = JsonUtility.FromJson<AlarmBookData>(ta.text); }
            catch (Exception e) { Debug.LogError($"[AlarmCodebook] JSON 파싱 실패: {e.Message}"); }

            _all   = data?.entries ?? Array.Empty<AlarmEntry>();
            _doc   = data?.doc;
            _byCode = new Dictionary<int, AlarmEntry>(_all.Length);
            foreach (var e in _all)
            {
                if (e == null) continue;
                if (!_byCode.ContainsKey(e.code)) _byCode.Add(e.code, e);
                else Debug.LogWarning($"[AlarmCodebook] 중복 코드 {e.code} — 첫 항목 유지.");
            }

            // 로드 성공 판정 = 색인된 항목이 1개 이상(파싱 성공 + 비어있지 않음).
            // 비면 위 try/catch 또는 빈 JSON으로 LogError가 이미 찍혔거나(파싱 실패), 데이터가 0건 — 어느 쪽이든 SSOT 불가.
            _loadOk = _byCode.Count > 0;
            if (!_loadOk)
                Debug.LogError($"[AlarmCodebook] 코드북이 비어 있음(파싱 실패 또는 0건) — 알람 시스템 오프라인으로 간주(fail-to-safe).");

            // [외부감사 S4 2026-06-17 · 구본승] 코드북 무결성 자가검증(EnsureLoaded는 1회만 실행 — 로그도 1회).
            //   ① 선언 총량(totalDefined) vs 실제 항목 수 — JSON 편집 누락/중복을 탐지.
            //   ② 중복 코드로 색인에서 빠진 항목 수 — _all 길이와 색인 수의 차.
            //   ③ XR 비대상(xr:false) 개수 — 원본 문서(MBE-DOC-2026-XR-002) '23 vs 25' 불일치 추적건.
            //      실제값을 로깅해 원본 PDF와 대조 가능케 함(어느 쪽이 정본인지는 원본 확인 필요).
            if (_loadOk && data != null)
            {
                if (_all.Length != data.totalDefined)
                    Debug.LogWarning($"[AlarmCodebook] 항목 수 불일치 — 선언 totalDefined={data.totalDefined}, 실제 entries={_all.Length}. JSON 정합 확인 필요.");
                if (_byCode.Count != _all.Length)
                    Debug.LogWarning($"[AlarmCodebook] 색인 수({_byCode.Count}) ≠ 항목 수({_all.Length}) — 중복 코드로 누락된 항목 존재.");
                int xrOff = 0;
                foreach (var e in _all) if (e != null && !e.xr) xrOff++;
                Debug.Log($"[AlarmCodebook] 로드 {_all.Length}건 · 색인 {_byCode.Count}건 · XR 비대상 {xrOff}건 " +
                          $"(원본 대조 추적: XR 비대상 23 vs 25 불일치건 — 정본은 원본 문서 확인 필요).");
            }
        }

        /// <summary>코드북 전체(로드된 순서). 1차 접근 시 지연 로드.</summary>
        public static IReadOnlyList<AlarmEntry> All { get { EnsureLoaded(); return _all; } }

        /// <summary>로드된 항목 수(정상 시 170).</summary>
        public static int Count { get { EnsureLoaded(); return _all.Length; } }

        /// <summary>
        /// 코드북(SSOT)이 정상 로드됐는가 — Count&gt;0 && 파싱 성공. fail-to-safe 게이트.
        /// false면 알람 정의를 신뢰할 수 없으므로 호출부는 '알람 시스템 오프라인 안전정지'로 전이해야 한다.
        /// (CraneFault가 이 플래그로 비상 FaultDef를 강제. 정상(true) 시엔 거동 불변.)
        /// </summary>
        public static bool IsLoaded { get { EnsureLoaded(); return _loadOk; } }

        /// <summary>코드북 문서 식별자.</summary>
        public static string Doc { get { EnsureLoaded(); return _doc; } }

        /// <summary>코드로 항목 조회. 없으면 false.</summary>
        public static bool TryGet(int code, out AlarmEntry entry)
        {
            EnsureLoaded();
            return _byCode.TryGetValue(code, out entry);
        }

        /// <summary>코드로 항목 조회. 없으면 null.</summary>
        public static AlarmEntry Get(int code)
        {
            EnsureLoaded();
            return _byCode.TryGetValue(code, out var e) ? e : null;
        }

        /// <summary>소스(GT/TR/HO/SP/SYS/ENV)별 항목.</summary>
        public static IEnumerable<AlarmEntry> BySource(string source)
        {
            EnsureLoaded();
            return _all.Where(e => e != null && string.Equals(e.source, source, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>XR 단말 표시 대상만(코드북 취소선 제외).</summary>
        public static IEnumerable<AlarmEntry> XrVisible
        {
            get { EnsureLoaded(); return _all.Where(e => e != null && e.xr); }
        }

        // ── 표시·라벨 헬퍼 (CraneFault와 단일화) ──

        /// <summary>심각도 표시색 — 코드북 §7. (Fatal 적 / Critical 주황 / Warning 노랑 / Info 회색)</summary>
        public static Color Color(AlarmEntry e) => CraneFault.SevColor(e.Severity);

        /// <summary>심각도 한글 표기 — 심각/위험/주의/정보.</summary>
        public static string SevLabel(AlarmEntry e) => CraneFault.SevLabel(e.Severity);

        /// <summary>AI 상태 라벨(정상/주의/이상) — 코드북 §8.</summary>
        public static LoadGrade AiGrade(AlarmEntry e) => CraneFault.ToGrade(e.Severity);

        /// <summary>HUD 한 줄 표기: "[3012] HO 과부하 (정격 초과)".</summary>
        public static string Format(AlarmEntry e) => e == null ? "" : e.ToString();

        /// <summary>
        /// 동시 활성 알람 정렬 키 — 코드북 §7: 심각도 높은 순 우선.
        /// (동일 심각도 내 '최근 발생 우선'은 호출부의 발생시각으로 별도 처리.)
        /// </summary>
        public static int Compare(AlarmEntry a, AlarmEntry b)
        {
            if (a == null) return b == null ? 0 : 1;
            if (b == null) return -1;
            return b.sev.CompareTo(a.sev);   // 내림차순(Fatal=3 우선)
        }
    }
}
