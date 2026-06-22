using System.Collections.Generic;
using UnityEngine;

namespace Container.Crane.Sts
{
    /// <summary>
    /// 스프레더 트위스트락 잠금/해제 모션. 잡을 때 트위스트락이 아래로 삽입(딥) + 90° 트위스트,
    /// 놓을 때 원위치로. SpreaderGrabber가 SetLocked()를 호출한다.
    /// 스프레더 자식 중 이름이 Twistlock_Cone / Twistlock_Head 인 것들을 자동 수집.
    /// </summary>
    [AddComponentMenu("Container/STS Crane/Spreader Lock Animator")]
    [DisallowMultipleComponent]
    public sealed class SpreaderLockAnimator : MonoBehaviour
    {
        [SerializeField] float twistAngle = 90f;   // 잠금 시 Y축 회전
        [SerializeField] float dip = 0.01f;         // 잠금 시 아래로 삽입(m)
        [SerializeField] float speed = 4f;          // 잠금/해제 진행 속도(1/초)

        struct Part { public Transform t; public Vector3 restPos; public Quaternion restRot; }
        readonly List<Part> parts = new List<Part>();
        float locked01;   // 0=해제, 1=잠금
        float target;

        void Awake()
        {
            Collect(StsPartNames.TwistlockCone);
            Collect(StsPartNames.TwistlockHead);
        }

        void Collect(string partName)
        {
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (CraneHud.BaseName(t.name) == partName)
                    parts.Add(new Part { t = t, restPos = t.localPosition, restRot = t.localRotation });
        }

        public void SetLocked(bool locked) => target = locked ? 1f : 0f;

        /// <summary>잠금/해제 1회 소요 시간(초)으로 설정.</summary>
        public void SetLockSeconds(float seconds) => speed = 1f / Mathf.Max(0.05f, seconds);

        /// <summary>잠금 지령 상태(목표). 라벨/HUD 표시용.</summary>
        public bool Locked => target > 0.5f;
        /// <summary>실제 잠금 진행도 0..1(애니메이션 중간값). 1=완전 잠금.</summary>
        public float LockProgress => locked01;

        void Update()
        {
            if (locked01 == target) return;
            locked01 = Mathf.MoveTowards(locked01, target, Mathf.Max(0.1f, speed) * Time.deltaTime);
            Apply();
        }

        void Apply()
        {
            foreach (var p in parts)
            {
                if (p.t == null) continue;
                p.t.localRotation = p.restRot * Quaternion.Euler(0f, twistAngle * locked01, 0f);
                Vector3 pos = p.restPos;
                pos.y -= dip * locked01;
                p.t.localPosition = pos;
            }
        }
    }
}
