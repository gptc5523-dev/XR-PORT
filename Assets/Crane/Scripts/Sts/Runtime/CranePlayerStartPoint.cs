using UnityEngine;

namespace Container.Crane.Sts
{
    /// <summary>
    /// 시작 위치 마커 — 이 오브젝트의 '위치'와 '바라보는 방향(파란 Z축, forward)'을 CranePlayerStartPlacer가
    /// 읽어 씬 진입 시 로컬 플레이어 리그를 그 지점·방향으로 배치한다.
    ///   사용법: 씬에서 이 마커를 원하는 곳(예: 레인 끝)에 끌어다 두고, 파란 Z축을 항구쪽으로 돌리면 끝.
    ///   (좌표를 코드에 박지 않고 디자이너가 씬에서 직접 보고 맞추는 WYSIWYG 방식)
    /// 런타임에는 아무 일도 안 한다 — 순수 마커. 에디터에서만 기즈모로 보인다.
    /// </summary>
    [AddComponentMenu("Container/STS Crane/Crane Player Start Point")]
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
