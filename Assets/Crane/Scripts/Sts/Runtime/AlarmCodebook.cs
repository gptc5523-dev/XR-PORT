using System;
using System.Collections.Generic;
using UnityEngine;

namespace AIXRCrane.Crane.Sts
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
        public string part;       // 3D 부품 ID(CranePartId.Parts, 그룹 ID 가능). ""= 부품 없음(통신·로그인·기상 등)

        public FaultSeverity Severity => (FaultSeverity)Mathf.Clamp(sev, 0, 3);

        /// <summary>HUD 한 줄 표기: "[3012] HO 과부하 (정격 초과)".</summary>
        public override string ToString() => $"[{code}] {ko}";
    }

    [Serializable]
    class AlarmBookData
    {
        public int totalDefined;
        public AlarmEntry[] entries;
    }

    /// <summary>알람 코드북(170개) SSOT — Resources JSON 1회 로드, 코드→항목 색인. 코드·심각도·메시지는 임의 정의 금지.</summary>
    public static class AlarmCodebook
    {
        const string ResourcePath = "Sts/AlarmCodebook";   // Assets/Crane/Resources/Sts/AlarmCodebook.json

        static Dictionary<int, AlarmEntry> _byCode;
        static AlarmEntry[] _all;
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
            _byCode = new Dictionary<int, AlarmEntry>(_all.Length);
            foreach (var e in _all)
            {
                if (e == null) continue;
                if (!_byCode.ContainsKey(e.code)) _byCode.Add(e.code, e);
                else Debug.LogWarning($"[AlarmCodebook] 중복 코드 {e.code} — 첫 항목 유지.");
            }

            // 로드 성공 = 색인 1개 이상. 파싱 실패든 0건이든 비면 SSOT 불가.
            _loadOk = _byCode.Count > 0;
            if (!_loadOk)
                Debug.LogError($"[AlarmCodebook] 코드북이 비어 있음(파싱 실패 또는 0건) — 알람 시스템 오프라인으로 간주(fail-to-safe).");

            // 무결성 자가검증(1회) — 선언 수·중복·XR 비대상 수를 원본 문서와 대조(XR 비대상 23 vs 25 추적 중).
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

        /// <summary>코드북 정상 로드 여부 — fail-to-safe 게이트. false 면 호출부는 알람 오프라인 안전정지로.</summary>
        public static bool IsLoaded { get { EnsureLoaded(); return _loadOk; } }

        /// <summary>전체 항목(로드 실패면 빈 배열).</summary>
        public static AlarmEntry[] All { get { EnsureLoaded(); return _all; } }

        /// <summary>코드로 항목 조회. 없으면 null.</summary>
        public static AlarmEntry Get(int code)
        {
            EnsureLoaded();
            return _byCode.TryGetValue(code, out var e) ? e : null;
        }

        // 표시·라벨 헬퍼 (CraneFault와 단일화)

        /// <summary>심각도 표시색 — 코드북 §7. (Fatal 적 / Critical 주황 / Warning 노랑 / Info 회색)</summary>
        public static Color Color(AlarmEntry e) => CraneFault.SevColor(e.Severity);
    }
}
