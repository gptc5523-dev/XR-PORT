using UnityEngine;

namespace AIXRCrane.Crane.Sts
{
    /// <summary>붐호이스트 로프 — 정점 시브(고정)와 붐 브라이들(러핑 추종) 사이를 매 프레임 다시 그린다.
    /// 서로 다른 부모를 가지므로 월드 좌표로 배치. [ExecuteAlways]로 에디터에서도 추종.</summary>
    [AddComponentMenu("AI-XR Crane/STS Crane/Boom Hoist Rig")]
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public sealed class BoomHoistRig : MonoBehaviour
    {
        [SerializeField] Transform[] apexPoints;   // 정점 시브 접점(고정)
        [SerializeField] Transform[] boomPoints;   // 붐 브라이들 부착점(러핑 추종)
        [SerializeField] Transform[] ropes;        // 로프 실린더(기본 축 Y, 높이 2)
        [SerializeField] float radius = 0.004f;

        public void Configure(Transform[] apex, Transform[] boom, Transform[] rope, float r)
        {
            apexPoints = apex; boomPoints = boom; ropes = rope; radius = r;
            Apply();
        }

        void LateUpdate() => Apply();

        void Apply()
        {
            if (apexPoints == null || boomPoints == null || ropes == null) return;
            int n = Mathf.Min(ropes.Length, Mathf.Min(apexPoints.Length, boomPoints.Length));
            for (int i = 0; i < n; i++)
            {
                Transform a = apexPoints[i], b = boomPoints[i], r = ropes[i];
                if (a == null || b == null || r == null) continue;
                Vector3 pa = a.position, pb = b.position;
                Vector3 dir = pa - pb;
                float len = dir.magnitude;
                r.position = (pa + pb) * 0.5f;
                r.rotation = len > 1e-6f ? Quaternion.FromToRotation(Vector3.up, dir / len) : Quaternion.identity;
                r.localScale = new Vector3(radius * 2f, len * 0.5f, radius * 2f);
            }
        }
    }
}
