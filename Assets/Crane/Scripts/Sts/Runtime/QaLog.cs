using System.Text;
using UnityEngine;

namespace AIXRCrane.Crane.Sts
{
    /// <summary>QA 판정용 콘솔 로그 포맷기 — 포맷: [QA] SYS/EVENT f=&lt;frame&gt; t=&lt;time&gt; | k=v ... [=&gt; PASS|FAIL].
    /// Info는 상태만 찍고, Check는 조건을 자체판정한다(FAIL은 LogError). grep 키: "[QA]", "=&gt; FAIL".</summary>
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
