using UnityEngine;

namespace AIXRCrane.Crane.Sts
{
    /// <summary>스프레더 컨테이너 attach/detach. Attach = attachPoint 자식+kinematic, Detach = 복원+새 부모.</summary>
    [AddComponentMenu("AI-XR Crane/STS Crane/Spreader Attach")]
    [DisallowMultipleComponent]
    public sealed class SpreaderAttach : MonoBehaviour
    {
        [Tooltip("컨테이너가 결합될 좌표 — 비워두면 자기 자신 Transform 사용.")]
        [SerializeField] Transform attachPoint;

        Transform attached;
        Rigidbody attachedBody;
        bool savedUseGravity;
        bool savedIsKinematic;
        float attachedLoadKg;   // 표시용 하중(kg) — 물리 mass와 분리
        string attachedDisplayId;   // 잡는 순간 확정되는 ISO6346 번호, 없으면 null

        public bool HasContainer => attached != null;
        public Transform AttachedContainer => attached;
        /// <summary>적재 하중(kg, 표시용). ContainerLoad 값 우선(미니어처라 물리 mass 무의미), 없으면 Rigidbody.mass.</summary>
        public float AttachedMassKg => attachedLoadKg > 0f ? attachedLoadKg : (attachedBody != null ? attachedBody.mass : 0f);
        /// <summary>적재 하중(톤).</summary>
        public float AttachedLoadTons => AttachedMassKg / 1000f;
        /// <summary>표시용 ISO6346 번호(잡는 순간 결정적 확정, 없으면 null).
        /// ContainerInstance.DisplayId, 없으면 이름 시드로 생성.</summary>
        public string AttachedDisplayId => attachedDisplayId;

        Transform Point => attachPoint != null ? attachPoint : transform;
        /// <summary>컨테이너가 매달리는 기준 Transform(네트워크 동기화가 클라이언트 부착에 사용).</summary>
        public Transform AttachAnchor => Point;

        public void Configure(Transform point)
        {
            attachPoint = point;
        }

        /// <summary>컨테이너 결합(이미 잡고 있으면 무시). 월드 자세 유지한 채 부착점 자식으로만 — 정렬은 호출자 몫.</summary>
        public bool Attach(Transform container)
        {
            if (container == null || attached != null) return false;

            attached = container;
            attachedBody = container.GetComponent<Rigidbody>();
            // 표시용 하중: ContainerInstance 무게, 없으면 이름 해시로 결정적 산출.
            float tons = 0f;
            var attachedInstance = container.GetComponentInParent<ContainerInstance>();   // 부두 배치 등 없을 수 있음
            if (attachedInstance != null) tons = attachedInstance.LoadTons;
            if (tons <= 0f) tons = ContainerLoad.WeightTons(container.name);
            attachedLoadKg = tons * 1000f;
            // 표시 번호: DisplayId, 없으면 이름 시드로 결정적 생성(다시 잡아도 같은 번호).
            attachedDisplayId =
                attachedInstance != null && !string.IsNullOrEmpty(attachedInstance.DisplayId)
                    ? attachedInstance.DisplayId
                    : ContainerIdGenerator.FormatForDisplay(
                          ContainerIdGenerator.GenerateDeterministic(CraneHud.BaseName(container.name)));
            if (attachedBody != null)
            {
                savedUseGravity = attachedBody.useGravity;
                savedIsKinematic = attachedBody.isKinematic;
                attachedBody.useGravity = false;
                attachedBody.isKinematic = true;
            }
            // ★ 월드 자세 보존 — localRotation 을 덮어쓰면 회전된 부착점 아래서 컨테이너가 같이 돈다.
            container.SetParent(Point, worldPositionStays: true);
            return true;
        }

        /// <summary>결합 해제. newParent를 주면 그 아래로 이동(예: 야드 슬롯), null이면 씬 루트로.</summary>
        public Transform Detach(Transform newParent = null)
        {
            if (attached == null) return null;

            attached.SetParent(newParent, worldPositionStays: true);
            if (attachedBody != null)
            {
                attachedBody.useGravity = savedUseGravity;
                attachedBody.isKinematic = savedIsKinematic;
            }
            var released = attached;
            attached = null;
            attachedBody = null;
            attachedLoadKg = 0f;
            attachedDisplayId = null;   // 놓으면 식별 미표시로 복귀
            return released;
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            Gizmos.color = HasContainer ? new Color(0.2f, 1f, 0.3f, 1f)
                                        : new Color(1f, 0.4f, 0.4f, 1f);
            Gizmos.matrix = Point.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(0.25f, 0.05f, 0.12f));
        }
#endif
    }
}
