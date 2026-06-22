using System.Text;
using UnityEngine;

namespace Container.Crane.Sts
{
    /// <summary>
    /// QA 판정용 콘솔 로그 단일 포맷기 — 외부 QA 팀 시나리오(문서/QA_테스트시나리오.md) 검증용.
    ///
    ///   포맷:  [QA] SYS/EVENT f=&lt;frame&gt; t=&lt;time&gt; | k1=v1 k2=v2 ... [=&gt; PASS|FAIL]
    ///
    /// 오너가 VR 데이터를 직접 못 보고 Console만 보므로, 모든 합격/불합격이 한 줄로 판정되게 한다.
    ///   · Info : 상태/수치만 찍는다(사람이 기대 키값과 대조).
    ///   · Check: 조건을 받아 끝에 " => PASS" / " => FAIL"를 붙인다(자체판정). FAIL은 LogError로 띄워 눈에 띄게.
    ///
    /// 기존 각 컴포넌트의 debugLog 라인과 독립 — Enabled로만 켜고 끈다(기본 on). grep 키: "[QA]", "=> FAIL".
    /// </summary>
    public static class QaLog
    {
        /// <summary>QA 라인 전역 on/off. 릴리스 빌드에서 끄려면 false.</summary>
        public static bool Enabled = true;

        // f=frame t=time | <fields> 머리부를 만든다. fields는 "k=v k=v ..." 형태(호출부에서 조립).
        static string Head(string sys, string evt, string fields)
        {
            var sb = new StringBuilder(96);
            sb.Append("[QA] ").Append(sys).Append('/').Append(evt)
              .Append(" f=").Append(Time.frameCount)
              .Append(" t=").Append(Time.time.ToString("F3"));
            if (!string.IsNullOrEmpty(fields)) sb.Append(" | ").Append(fields);
            return sb.ToString();
        }

        /// <summary>상태/수치 라인(판정 없음). fields는 "k=v k=v" 문자열(보통 보간 문자열).</summary>
        public static void Info(string sys, string evt, string fields)
        {
            if (!Enabled) return;
            Debug.Log(Head(sys, evt, fields));
        }

        /// <summary>조건 자체판정 라인. cond=true→" => PASS"(Log), false→" => FAIL"(LogError).</summary>
        public static void Check(string sys, string evt, bool cond, string fields)
        {
            if (!Enabled) return;
            string line = Head(sys, evt, fields) + (cond ? " => PASS" : " => FAIL");
            if (cond) Debug.Log(line);
            else Debug.LogError(line);
        }

        /// <summary>Vector3를 (x,y,z) F3로.</summary>
        public static string V(Vector3 v) => $"({v.x:F3},{v.y:F3},{v.z:F3})";
        /// <summary>float F3.</summary>
        public static string F(float v) => v.ToString("F3");
    }
}
