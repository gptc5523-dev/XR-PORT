using UnityEngine;

namespace AIXRCrane
{
    /// <summary>씬 계층 공용 조회 — 렌더러 바운즈 합, 이름으로 하위 찾기. 크레인·컨테이너·선박·에디터 도구가 같이 쓴다.</summary>
    public static class SceneUtil
    {
        /// <summary>t 아래 렌더러 바운즈 합(월드). 렌더러가 없으면 false. includeInactive = 꺼진 LOD·자식까지.</summary>
        public static bool TryBounds(Transform t, out Bounds b, bool includeInactive = false)
        {
            b = default;
            bool any = false;
            foreach (var r in t.GetComponentsInChildren<Renderer>(includeInactive))
            {
                if (any) b.Encapsulate(r.bounds);
                else { b = r.bounds; any = true; }
            }
            return any;
        }

        /// <summary>렌더러 배열(미리 모아 둔 것)의 바운즈 합. 꺼졌거나 사라진 렌더러는 뺀다. 하나도 없으면 false.</summary>
        public static bool TryBoundsOf(Renderer[] rends, out Bounds b)
        {
            b = default;
            bool any = false;
            foreach (var r in rends)
            {
                if (r == null || !r.enabled) continue;
                if (any) b.Encapsulate(r.bounds);
                else { b = r.bounds; any = true; }
            }
            return any;
        }

        /// <summary>렌더러 바운즈 합, 렌더러가 없으면 go 위치의 크기 0 바운즈.</summary>
        public static Bounds BoundsOrPoint(GameObject go, bool includeInactive = false)
            => TryBounds(go.transform, out var b, includeInactive) ? b : new Bounds(go.transform.position, Vector3.zero);

        /// <summary>root 자신 포함, 하위에서 이름이 정확히 같은 첫 Transform(깊이 우선). 없으면 null.</summary>
        public static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform c in root)
            {
                var r = FindDeep(c, name);
                if (r != null) return r;
            }
            return null;
        }
    }
}
