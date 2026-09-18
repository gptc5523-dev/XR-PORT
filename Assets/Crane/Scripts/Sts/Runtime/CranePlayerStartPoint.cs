using UnityEngine;

namespace AIXRCrane.Crane.Sts
{
    /// <summary>시작 위치 마커 — 위치·파란 Z축(forward) 방향을 CranePlayerStartPlacer가 읽어 씬 진입 시 플레이어를 배치한다.
    /// 씬에서 마커를 끌어다 놓고 Z축을 원하는 방향으로 돌리면 끝(WYSIWYG). 런타임엔 아무 일도 안 하는 순수 마커.</summary>
    [AddComponentMenu("AI-XR Crane/STS Crane/Crane Player Start Point")]
    [DisallowMultipleComponent]    public sealed class CranePlayerStartPoint : MonoBehaviour
    {
#if UNITY_EDITOR
        void OnDrawGizmos()
        {
            Vector3 p = transform.position;
            Vector3 f = transform.forward;

            // 멀리서도 보이게: 발 위치 구체 + 위로 솟은 기둥 + 정면 화살표 + 글자 라벨.
            Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.95f);
            Gizmos.DrawSphere(p, 0.06f);                          // 발 위치
            Vector3 top = p + Vector3.up * 0.6f;
            Gizmos.DrawLine(p, top);                              // 눈에 띄는 수직 기둥
            Gizmos.DrawWireSphere(top, 0.05f);

            Gizmos.color = new Color(1f, 0.55f, 0.1f, 0.95f);     // 바라보는 방향(주황 화살표)
            Gizmos.DrawLine(p, p + f * 0.45f);
            Vector3 r = transform.right * 0.10f;
            Gizmos.DrawLine(p + f * 0.45f, p + f * 0.33f + r);
            Gizmos.DrawLine(p + f * 0.45f, p + f * 0.33f - r);

            UnityEditor.Handles.color = Color.cyan;
            UnityEditor.Handles.Label(top + Vector3.up * 0.05f, "▶ PLAYER START");
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, 0.12f);
        }
#endif
    }
}
