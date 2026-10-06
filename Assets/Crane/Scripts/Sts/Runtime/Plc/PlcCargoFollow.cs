using System.Collections.Generic;
using UnityEngine;

namespace AIXRCrane.Crane.Sts.Plc
{
    /// <summary>라이브 PLC 화물 — PLC 잠금 신호가 오르면 스프레더 밑 컨테이너를 매달고 내리면 놓는다. PLC 가 진실이라 콘 안착 게이트는 없다.</summary>
    [RequireComponent(typeof(PlcBridge))]
    [DisallowMultipleComponent]
    public sealed class PlcCargoFollow : MonoBehaviour
    {
        [Tooltip("잠금 순간 스프레더 기준점과 컨테이너 윗면 중심의 허용 거리(실척 m) — 수평·수직.")]
        [SerializeField] float pickRadiusM = 1.5f, pickHeightM = 1.5f;

        StsCrane crane;
        PlcBridge bridge;
        SpreaderGrabber grabber;
        SpreaderLockAnimator lockAnim;
        bool was;
        Transform heldYardRoot;   // 야드에서 집었으면 놓을 때 Yard_Containers 밑으로 되돌린다(다음 집기 후보에 남게)

        void Awake()
        {
            crane = GetComponent<StsCrane>();
            bridge = GetComponent<PlcBridge>();
            grabber = GetComponent<SpreaderGrabber>();
            lockAnim = GetComponentInChildren<SpreaderLockAnimator>(true);
        }

        void LateUpdate()
        {
            if (crane == null || bridge == null || !bridge.Active) return;
            bool locked = bridge.Latest.TwistLockLocked;
            if (locked && !was) Pick();
            else if (!locked && was) Place();
            was = locked;
        }

        void Pick()
        {
            if (crane.Attach == null || crane.Attach.HasContainer) return;
            Vector3 gp = grabber != null ? grabber.GrabPoint() : ((Component)crane.Spreader).transform.position;
            float r = pickRadiusM * crane.ModelScale, h = pickHeightM * crane.ModelScale, best = float.MaxValue;
            Transform c = null;
            foreach (var t in Candidates())
            {
                if (t.IsChildOf(crane.transform) || !SceneUtil.TryBounds(t, out var b)) continue;
                float dxz = new Vector2(b.center.x - gp.x, b.center.z - gp.z).magnitude;
                if (dxz <= r && Mathf.Abs(b.max.y - gp.y) <= h && dxz < best) { best = dxz; c = t; }
            }
            if (c == null)
            {
                Debug.LogWarning($"[PlcCargo] {crane.name} 잠금 — 스프레더 밑에 컨테이너가 없다(PLC 자세와 씬 배치가 어긋남). gp={gp}");
                return;
            }
            heldYardRoot = c.parent != null && c.parent.name == "Yard_Containers" ? c.parent : null;
            Vector3 p = c.position; Quaternion q = c.rotation;
            if (!crane.Attach.Attach(c)) return;
            c.SetPositionAndRotation(p, q);   // Attach 는 부착점으로 스냅한다 — PLC 자세 그대로 둔다(PlcCargoReplay 와 같다)
            if (lockAnim != null) lockAnim.SetLocked(true);
        }

        /// <summary>집을 수 있는 컨테이너 — 물리 화물(배, 이름에 Container) + 야드 정적 배치(Yard_Containers 자식, Rigidbody 없음). 배치 내보내기와 같은 목록.</summary>
        public static IEnumerable<Transform> Candidates()
        {
            foreach (var rb in FindObjectsByType<Rigidbody>(FindObjectsSortMode.InstanceID))
                if (rb.name.IndexOf("Container", System.StringComparison.OrdinalIgnoreCase) >= 0) yield return rb.transform;
            var yard = GameObject.Find("Yard_Containers");
            if (yard != null) foreach (Transform t in yard.transform) yield return t;
        }

        void Place()
        {
            if (crane.Attach == null || !crane.Attach.HasContainer) return;
            var held = crane.Attach.AttachedContainer;
            if (grabber != null) grabber.Release();
            else { crane.Attach.Detach(); if (lockAnim != null) lockAnim.SetLocked(false); }
            if (held != null && heldYardRoot != null) held.SetParent(heldYardRoot, true);
            heldYardRoot = null;
        }
    }
}
