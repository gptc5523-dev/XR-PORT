using UnityEngine;

namespace AIXRCrane.Crane.Sts
{
    /// <summary>스프레더 푸셔/든 컨테이너 콜라이더에 붙어, 외부 컨테이너와 옆·아래로 부딪힌 접촉을 SpreaderGrabber 에 보고(3013 HO Snag).
    /// PhysX 접점 법선의 |y| 로 충돌(작음)과 적층(큼)을 구분하고, sleep 방지로 WakeUp() 한다. 강체 없는 바닥·크레인 자식은 제외.</summary>
    [DisallowMultipleComponent]
    public sealed class LoadCollisionRelay : MonoBehaviour
    {
        public SpreaderGrabber owner;

        [Tooltip("접점 법선의 |y|가 이 값 미만이면 '옆/아래 충돌'(경보), 이상이면 '위 적층'(무시). 0.7≈수직에서 45°.")]
        public float sideNormalMaxY = 0.7f;

        void OnCollisionEnter(Collision c) => Handle(c);
        void OnCollisionStay(Collision c) => Handle(c);

        void Handle(Collision c)
        {
            var other = c.rigidbody;                                      // 우리가 부딪힌 상대 강체
            if (owner == null || other == null) return;                  // 강체 없는 바닥/구조물 제외
            if (other.transform.IsChildOf(owner.transform)) return;      // 크레인 자식(스프레더/든 화물) 제외

            int n = c.contactCount;
            for (int i = 0; i < n; i++)
            {
                if (Mathf.Abs(c.GetContact(i).normal.y) < sideNormalMaxY)   // 수평성분 우세 = 측면/바닥 충돌
                {
                    owner.NotifyContact();
                    other.WakeUp();   // sleep 방지 — 가만히 대고 있어도 Stay가 계속 오게
                    return;
                }
            }
        }
    }
}
