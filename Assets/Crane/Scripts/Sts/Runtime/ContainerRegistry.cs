using System.Collections.Generic;
using UnityEngine;

namespace AIXRCrane.Crane.Sts
{
    /// <summary>씬 컨테이너의 고유 번호(WBS 5.6) — 씬 로드 순간의 계층 순서(씬 파일 순서라 모든 기기가 같다)로 한 번 매긴다. 배 컨테이너 238개가 모두 'ShipContainer' 라 이름순 번호가 기기마다 달랐다.</summary>
    public static class ContainerRegistry
    {
        const string YardRoot = "Yard_Containers";   // 야드 정적 배치(강체 없음) — PlcCargoFollow 와 같은 이름
        static List<Transform> list;
        static List<string> keys;
        static Dictionary<Transform, int> index;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { list = null; keys = null; index = null; }

        // 잡히면 스프레더 자식이 되므로 아무것도 움직이기 전(Start 전)에 만든다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Build()
        {
            list = new List<Transform>(); keys = new List<string>(); index = new Dictionary<Transform, int>();
            var seen = new Dictionary<string, int>();
            foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects()) Walk(root.transform, seen);
        }

        static void Walk(Transform t, Dictionary<string, int> seen)
        {
            bool yard = t.parent != null && t.parent.name == YardRoot;
            if (yard || (t.GetComponent<Rigidbody>() != null && t.name.IndexOf("Container", System.StringComparison.OrdinalIgnoreCase) >= 0))
            {
                string path = (t.parent != null ? t.parent.name : "") + "/" + t.name;
                seen.TryGetValue(path, out int n);
                seen[path] = n + 1;
                index[t] = list.Count;
                list.Add(t);
                keys.Add(path + "#" + n);
                return;   // 컨테이너 안(LOD·문)은 보지 않는다
            }
            for (int i = 0; i < t.childCount; i++) Walk(t.GetChild(i), seen);
        }

        static void Ensure() { if (list == null) Build(); }

        public static IReadOnlyList<Transform> All { get { Ensure(); return list; } }

        /// <summary>네트워크로 주고받는 번호 — 없으면 −1.</summary>
        public static int IndexOf(Transform t) { Ensure(); return t != null && index.TryGetValue(t, out int i) ? i : -1; }

        public static Transform At(int i) { Ensure(); return i >= 0 && i < list.Count ? list[i] : null; }

        /// <summary>번호·하중을 만드는 시드 — 등록된 컨테이너면 '부모/이름#순번'(기기마다 같고 서로 다르다), 아니면 fallback.</summary>
        public static string Seed(Transform t, string fallback) { int i = IndexOf(t); return i >= 0 ? keys[i] : fallback; }
    }
}
