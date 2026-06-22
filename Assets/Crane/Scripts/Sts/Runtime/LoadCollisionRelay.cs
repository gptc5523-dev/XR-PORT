using UnityEngine;

namespace Container.Crane.Sts
{
    /// <summary>
    /// 스프레더 푸셔/든 컨테이너 콜라이더에 붙어, 외부 컨테이너와 '옆·아래로' 부딪힌 접촉을 SpreaderGrabber에 보고(3013 HO Snag).
    ///   - 박스 겹침을 계산하지 않고 PhysX가 주는 '접점(contact) + 법선(normal)'을 직접 본다
    ///     → 닿은 자리(바닥 포함) 어디든, 박스가 밀려나기 전 '닿는 순간' 잡힌다(겹침 방식의 일찍 뜸·바닥 누락 해소).
    ///   - 법선 수직성분 |normal.y|이 작으면(옆/아래로 쿵) 충돌로 보고, 크면(똑바로 위에서 사뿐) 적층으로 보고 무시.
    ///   - sleep 방지: 접촉한 컨테이너를 WakeUp() — 가만히 대고 있어도 OnCollisionStay가 계속 와 경보가 유지된다.
    /// 강체 없는 바닥/구조물, 크레인 자식(스프레더/든 화물)은 제외한다.
    /// </summary>
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
                    other.WakeUp();   // sleep 방지 — 가만히 대고 있어도 Stay 계속 오게(가만히 대면 신호 끊기던 문제)
                    return;
                }
            }
        }
    }
}
