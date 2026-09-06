using System.Collections.Generic;
using UnityEngine;

namespace Container.Crane.Sts
{
    /// <summary>
    /// 스프레더 트위스트락 잠금/해제 모션 — **수직축 90° 회전 전용**(위치 불변).
    /// 사양 SSOT(문서/크레인_동적데이터/rtg_crane_dynamics.json · 문서/스프레더_동작데이터/spreader_operation.json)의
    /// twistlock.dof = {type: rotation, axis: Z, 0°→90°}. 병진 자유도는 없다 —
    /// 스프레더는 로프에 매달려 높이가 고정이고 컨테이너가 상승해 물리는 방식이라 콘이 내려갈 일이 없다.
    /// SpreaderGrabber가 SetLocked()를 호출한다.
    /// 스프레더 자식 중 이름이 Twistlock_Cone / Twistlock_Head(절차) 또는 Spreader_Twistlock_*(FBX)인 것들을 자동 수집.
    /// </summary>
    [AddComponentMenu("Container/STS Crane/Spreader Lock Animator")]
    [DisallowMultipleComponent]
    [ExecuteAlways]   // 에디터 메뉴(잠그기/풀기)로 Play 없이 즉시 확인 가능하게
    public sealed class SpreaderLockAnimator : MonoBehaviour
    {
        [SerializeField] float twistAngle = 90f;   // 잠금 시 수직축 회전(사양: 0° → 90°)
        [SerializeField] float speed = 4f;          // 잠금/해제 진행 속도(1/초)

        // FBX 크레인: 트위스트락 로컬 Y축이 월드 수직과 어긋나므로 '월드 위쪽'을 기준으로 회전.
        [Tooltip("FBX 크레인: 로컬축 어긋남 회피 위해 월드 수직 기준 회전. 절차 크레인은 off.")]
        [SerializeField] bool worldVertical = false;
        public void SetWorldVertical(bool v) => worldVertical = v;

        // 회전 전용이라 rest는 회전만 보관 — 위치는 읽지도 쓰지도 않는다.
        struct Part { public Transform t; public Quaternion restRot; }
        readonly List<Part> parts = new List<Part>();
        float locked01;   // 0=해제, 1=잠금
        float target;

        void Awake() => EnsureCollected();

        void EnsureCollected()
        {
            if (parts.Count > 0) return;
            Collect(StsPartNames.TwistlockCone);   // 절차 크레인
            Collect(StsPartNames.TwistlockHead);
            foreach (var t in GetComponentsInChildren<Transform>(true))   // FBX 크레인(1회)
                if (t.name.StartsWith("Spreader_Twistlock_"))
                    parts.Add(new Part { t = t, restRot = t.localRotation });
        }

        void Collect(string partName)
        {
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (CraneHud.BaseName(t.name) == partName)
                    parts.Add(new Part { t = t, restRot = t.localRotation });
        }

        public void SetLocked(bool locked) => target = locked ? 1f : 0f;

        /// <summary>에디터 메뉴/테스트에서 즉시 반영(애니메이션 없이 최종 상태로 스냅).</summary>
        public void SetLockedNow(bool locked)
        {
            EnsureCollected();
            target = locked01 = locked ? 1f : 0f;
            Apply();
        }

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

        // 위치는 절대 건드리지 않는다 — 트위스트락은 '회전 전용'(사양 SSOT).
        // 옛 dip(아래로 삽입)은 사양에 없는 창작이었고, 로컬 단위 상수라 FBX RTG(로컬 1단위=실척 100m)에서
        // 의도한 1cm가 1m로 나가 콘(높이 0.135m)이 끝빔 밖으로 빠졌다.
        void Apply()
        {
            foreach (var p in parts)
            {
                if (p.t == null) continue;
                if (worldVertical)
                {
                    // 월드 위쪽을 부모 로컬로 변환 → 그 축으로 회전(FBX 축 어긋남 회피).
                    Vector3 lu = p.t.parent != null ? p.t.parent.InverseTransformDirection(Vector3.up) : Vector3.up;
                    if (lu.sqrMagnitude < 1e-8f) lu = Vector3.up; else lu.Normalize();
                    // 월드 수직축 회전 = A·restWorld 프리곱 → localRot = AngleAxis(lu)·restRot.
                    // (순서 중요: rest를 왼쪽에 두면 rest가 identity 아닐 때 회전축이 틀어짐. FBX는 부모 축변환 있어 필수.)
                    p.t.localRotation = Quaternion.AngleAxis(twistAngle * locked01, lu) * p.restRot;
                }
                else
                {
                    p.t.localRotation = p.restRot * Quaternion.Euler(0f, twistAngle * locked01, 0f);
                }
            }
        }
    }
}
