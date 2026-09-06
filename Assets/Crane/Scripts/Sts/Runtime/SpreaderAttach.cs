using UnityEngine;
using ContainerProject;

namespace Container.Crane.Sts
{
    /// <summary>
    /// 스프레더에 컨테이너를 attach/detach.
    /// - Attach: 컨테이너를 attachPoint의 자식으로 만들고 Rigidbody를 kinematic으로 전환.
    /// - Detach: 부모를 풀고 Rigidbody를 동적 상태로 복원, 옵션으로 새 부모 지정.
    /// 트위스트락 동작(0.4s 가정)은 즉시 처리. 추후 애니메이션이 필요하면 이벤트로 분리.
    /// </summary>
    [AddComponentMenu("Container/STS Crane/Spreader Attach")]
    [DisallowMultipleComponent]
    public sealed class SpreaderAttach : MonoBehaviour
    {
        [Tooltip("컨테이너가 결합될 좌표 — 비워두면 자기 자신 Transform 사용.")]
        [SerializeField] Transform attachPoint;

        Transform attached;
        Rigidbody attachedBody;
        ContainerInstance attachedInstance;   // 잡은 컨테이너의 ID·등급 보유 컴포넌트(부두 배치 등 없을 수 있음).
        bool savedUseGravity;
        bool savedIsKinematic;
        float attachedLoadKg;   // 잡은 컨테이너의 표시용 하중(kg). ContainerLoad 산출값 — 물리 mass와 분리.
        string attachedDisplayId;   // 잡는 순간 확정되는 ISO6346 표시 번호(결정적). 안 잡았으면 null → HUD에 식별 미표시.

        public bool HasContainer => attached != null;
        public Transform AttachedContainer => attached;
        /// <summary>적재된 컨테이너 하중(kg). 없으면 0. ContainerLoad 산출값 우선(1/24 미니어처라 물리 mass는 무의미),
        /// 없으면 Rigidbody.mass 폴백. 라벨/HUD 표시용.</summary>
        public float AttachedMassKg => attachedLoadKg > 0f ? attachedLoadKg : (attachedBody != null ? attachedBody.mass : 0f);
        /// <summary>적재 하중(톤).</summary>
        public float AttachedLoadTons => AttachedMassKg / 1000f;
        /// <summary>HUD/라벨 표시용 ISO6346 식별번호 — 잡는 순간 확정(결정적). 잡은 게 없으면 null(HUD에 미표시).
        /// ContainerInstance가 있으면 그 DisplayId, 없으면 컨테이너 이름 시드로 결정적 생성한 번호.</summary>
        public string AttachedDisplayId => attachedDisplayId;

        Transform Point => attachPoint != null ? attachPoint : transform;
        /// <summary>컨테이너가 매달리는 기준 Transform. 네트워크 동기화가 클라이언트에서 동일 위치에 컨테이너를 붙이는 데 사용.</summary>
        public Transform AttachAnchor => Point;

        public void Configure(Transform point)
        {
            attachPoint = point;
        }

        /// <summary>목표 월드 스케일을 부모 lossyScale로 역산해 로컬 스케일로. 0 나눗셈은 원값 유지.</summary>
        static Vector3 LocalScaleFor(Vector3 targetWorld, Vector3 parentLossy) => new Vector3(
            Mathf.Approximately(parentLossy.x, 0f) ? targetWorld.x : targetWorld.x / parentLossy.x,
            Mathf.Approximately(parentLossy.y, 0f) ? targetWorld.y : targetWorld.y / parentLossy.y,
            Mathf.Approximately(parentLossy.z, 0f) ? targetWorld.z : targetWorld.z / parentLossy.z);

        /// <summary>
        /// 컨테이너를 결합. 이미 잡고 있으면 무시(중복 잡기 방지).
        /// 월드 크기는 보존한다 — 부착점 스케일(FBX RTG ≈4.1667)이 상속되지 않게.
        /// </summary>
        public bool Attach(Transform container)
        {
            if (container == null || attached != null) return false;

            attached = container;
            attachedBody = container.GetComponent<Rigidbody>();
            // 표시용 하중(kg) 산출 — 물리 mass와 분리(1/24 미니어처라 실제 mass는 무의미).
            // ContainerInstance(ID 부여 컨테이너) 있으면 그 무게, 없으면(부두 배치 등) 이름 해시로 결정적 산출.
            float tons = 0f;
            attachedInstance = container.GetComponentInParent<ContainerInstance>();
            if (attachedInstance != null) tons = attachedInstance.LoadTons;
            if (tons <= 0f) tons = ContainerLoad.WeightTons(container.name);
            attachedLoadKg = tons * 1000f;
            // 표시 번호 확정 — ContainerInstance가 있으면 그 ISO6346 DisplayId, 없으면(부두/Kit 배치)
            // 컨테이너 이름을 시드로 결정적 생성 → 같은 컨테이너는 다시 잡아도 항상 같은 번호.
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
            // 월드 크기 보존 — SetParent(worldPositionStays:false)는 localScale을 그대로 두므로
            // 부착점 lossyScale이 1이 아니면 그대로 상속돼 컨테이너가 그만큼 팽창한다.
            //   · STS 절차: 부착점 lossyScale=1 → 무해(그래서 여태 안 드러났다)
            //   · FBX RTG: 스프레더 lossyScale≈4.1667 → 잡는 순간 20ft 6.06m가 25.2m로 부풀었다
            // 잡기 전 월드 스케일을 그대로 재현하도록 로컬 스케일을 역산한다.
            Vector3 worldScale = container.lossyScale;
            container.SetParent(Point, worldPositionStays: false);
            container.localPosition = Vector3.zero;
            container.localRotation = Quaternion.identity;
            container.localScale = LocalScaleFor(worldScale, Point.lossyScale);
            return true;
        }

        /// <summary>
        /// 결합 해제. newParent를 주면 그 아래로 이동(예: 야드 슬롯), null이면 씬 루트로.
        /// </summary>
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
            attachedInstance = null;
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
