using UnityEngine;

namespace Container.Crane.Sts
{
    /// <summary>
    /// STS Crane 컴포지션 루트.
    /// 각 부속(boom/trolley/spreader/attach)은 자체 책임을 가진 컴포넌트로 분리되어 있고,
    /// 이 클래스는 그들에 대한 참조를 모아 외부에 일관된 API를 노출만 한다(SRP + Facade).
    /// </summary>
    [AddComponentMenu("Container/STS Crane/STS Crane (Root)")]
    [DisallowMultipleComponent]
    public sealed class StsCrane : MonoBehaviour
    {
        [Header("Hierarchy 참조")]
        [SerializeField] Transform boom;
        [SerializeField] TrolleyMover trolley;
        [SerializeField] SpreaderHoist spreader;
        [SerializeField] SpreaderAttach attach;
        [SerializeField] GantryMover gantry;

        [Header("Gizmo 표시")]
        [SerializeField] bool drawGizmos = true;
        [SerializeField] Color boomColor    = new Color(0.3f, 0.7f, 1f, 0.6f);
        [SerializeField] Color railColor    = new Color(0.2f, 0.9f, 1f, 1f);
        [SerializeField] Color hoistColor   = new Color(1f, 0.85f, 0.2f, 1f);
        [SerializeField] Color gantryColor  = new Color(0.4f, 1f, 0.3f, 1f);

        public IAxisMover Trolley => trolley;
        public IAxisMover Spreader => spreader;
        public SpreaderAttach Attach => attach;
        public IAxisMover Gantry => gantry;

        // 운영상태(운전/정지/대기/이상) — 같은 GameObject의 CraneOpMode를 lazy 참조(없으면 런타임 부착).
        //   인스펙터/프리팹 직렬화를 건드리지 않도록 필드가 아닌 getter로만 보유한다.
        CraneOpMode opMode;
        public CraneOpMode OpMode =>
            opMode != null ? opMode : (opMode = GetComponent<CraneOpMode>() ?? gameObject.AddComponent<CraneOpMode>());

        /// <summary>
        /// 모델이 실척의 몇 배로 생성됐는지(StsCraneCreator.Scale=1/24와 동일). 단일 소스 — 속도/거리
        /// 환산(모델 units ↔ 실제 m)이 필요한 HUD·컨트롤러가 각자 상수를 두지 말고 이 값을 참조한다.
        /// </summary>
        public float ModelScale => StsConfig.ModelScale;   // SSOT — 값(1/24)은 StsConfig 단일 정의

        /// <summary>
        /// Builder가 한 번에 참조를 주입할 때 사용. 직접 인스펙터로 끌어 넣어도 동작은 동일.
        /// </summary>
        public void Configure(Transform boom, TrolleyMover trolley,
                              SpreaderHoist spreader, SpreaderAttach attach,
                              GantryMover gantry = null)
        {
            this.boom = boom;
            this.trolley = trolley;
            this.spreader = spreader;
            this.attach = attach;
            this.gantry = gantry;
        }

#if UNITY_EDITOR
        void OnDrawGizmos()
        {
            if (!drawGizmos) return;

            if (boom != null && trolley != null)
            {
                // 붐 레일 전체 길이를 한 줄로 표시 (트롤리 가동 범위 = 붐 작업 범위).
                Gizmos.color = boomColor;
                Vector3 ba = boom.TransformPoint(new Vector3(trolley.Min, 0f, 0f));
                Vector3 bb = boom.TransformPoint(new Vector3(trolley.Max, 0f, 0f));
                Gizmos.DrawLine(ba, bb);
            }

            if (trolley != null)
            {
                Gizmos.color = railColor;
                Vector3 a = trolley.transform.parent.TransformPoint(
                    new Vector3(trolley.Min, trolley.transform.localPosition.y, trolley.transform.localPosition.z));
                Vector3 b = trolley.transform.parent.TransformPoint(
                    new Vector3(trolley.Max, trolley.transform.localPosition.y, trolley.transform.localPosition.z));
                Gizmos.DrawLine(a, b);
            }

            if (spreader != null)
            {
                Gizmos.color = hoistColor;
                Vector3 a = spreader.transform.parent.TransformPoint(
                    new Vector3(spreader.transform.localPosition.x, spreader.Min, spreader.transform.localPosition.z));
                Vector3 b = spreader.transform.parent.TransformPoint(
                    new Vector3(spreader.transform.localPosition.x, spreader.Max, spreader.transform.localPosition.z));
                Gizmos.DrawLine(a, b);
            }

            // 갠트리 주행 범위 — 루트 Z축 [Min, Max] 라인. 다른 무버처럼 항상 표시.
            if (gantry != null)
            {
                Gizmos.color = gantryColor;
                Transform p = transform.parent;
                Vector3 lp = transform.localPosition;
                Vector3 a, b;
                if (p != null)
                {
                    a = p.TransformPoint(new Vector3(lp.x, lp.y + 0.05f, gantry.Min));
                    b = p.TransformPoint(new Vector3(lp.x, lp.y + 0.05f, gantry.Max));
                }
                else
                {
                    a = new Vector3(transform.position.x, transform.position.y + 0.05f, gantry.Min);
                    b = new Vector3(transform.position.x, transform.position.y + 0.05f, gantry.Max);
                }
                Gizmos.DrawLine(a, b);
                Gizmos.DrawWireSphere(a, 0.04f);
                Gizmos.DrawWireSphere(b, 0.04f);
            }
        }
#endif
    }
}
