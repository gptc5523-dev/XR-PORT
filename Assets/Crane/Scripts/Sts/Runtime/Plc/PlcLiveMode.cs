using UnityEngine;

namespace AIXRCrane.Crane.Sts.Plc
{
    /// <summary>라이브 PLC 모드 — 실행 인자 -plc(에디터는 메뉴)면 시연 대신 모든 크레인이 통합서버 PLC 데이터로 움직이고 잠금 신호로 컨테이너를 집고 놓는다.</summary>
    public static class PlcLiveMode
    {
        public const string EditorPrefKey = "PlcLiveMode.On", EditorUrlKey = "PlcLiveMode.Url";
        static int on = -1;

        public static bool On
        {
            get
            {
                if (on >= 0) return on == 1;
                bool v = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-plc") >= 0;
#if UNITY_EDITOR
                v |= UnityEditor.EditorPrefs.GetBool(EditorPrefKey, false);
#endif
                on = v ? 1 : 0;
                return v;
            }
        }

        /// <summary>에디터에서 로컬 서버를 가리킬 때만 — 기기는 ServerPlcSource.ResolveUrl 경로를 쓴다.</summary>
        public static string EditorUrl
        {
            get
            {
#if UNITY_EDITOR
                return UnityEditor.EditorPrefs.GetString(EditorUrlKey, "");
#else
                return "";
#endif
            }
        }

        static bool chosen;

        /// <summary>서버 VR(-plc -vr)이 시작 메뉴에서 'PLC 모니터링 / 크레인 조종'을 고르기 전인가 — 고르기 전엔 라이브 PLC 를 붙이지 않는다(씬 Awake 보다 먼저 알아야 해서 인자로 계산).</summary>
        public static bool Pending => !chosen && On && System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-vr") >= 0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetCache() { on = -1; chosen = false; }   // 도메인 리로드 없이 플레이를 다시 시작해도 메뉴 값을 새로 읽는다

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (On && !Pending) Attach();
        }

        /// <summary>시작 메뉴 '호스트 · PLC 모니터링'.</summary>
        public static void ChooseLive()
        {
            chosen = true;
            foreach (var b in Object.FindObjectsByType<PlcBridge>()) b.UseLiveSource();   // 씬에 미리 있던 STS 다리(CSV 재생)를 서버로
            Attach();
        }

        /// <summary>시작 메뉴 '호스트 · 크레인 조종' — 시연 감독(조종 존·자동 운전)을 띄운다.</summary>
        public static void ChooseManual() { chosen = true; on = 0; PortDemoDirector.Spawn(); }

        /// <summary>시작 메뉴 '참가' — 호스트를 따르기만 하니 라이브 PLC 를 붙이지 않는다.</summary>
        public static void ChooseJoin() => chosen = true;

        // 씬의 크레인마다 서버 구동 다리와 화물 추종을 붙인다 — STS 는 씬에 PlcBridge 가 있고 RTG 는 여기서 생긴다.
        static void Attach()
        {
            int n = 0;
            foreach (var c in Object.FindObjectsByType<StsCrane>())
            {
                if (c.GetComponent<PlcBridge>() == null) c.gameObject.AddComponent<PlcBridge>();
                if (c.GetComponent<PlcCargoFollow>() == null) c.gameObject.AddComponent<PlcCargoFollow>();
                n++;
            }
            Debug.Log($"[PlcLive] 라이브 PLC 모드 — 크레인 {n}대가 통합서버 데이터로 움직인다(시연 자동 운전 끔).");
        }
    }
}
