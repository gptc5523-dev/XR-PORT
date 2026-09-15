using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using Container.Crane.Sts.Plc;

namespace Container.Crane.Sts
{
    /// <summary>
    /// 시연 시나리오 총괄 — 오너 지시 2026-09-14: "서버에 처음 접속할 때 크레인들이 움직여야 됨. RTG·STS 가 각 컨테이너
    /// 5개만 옮기는 걸 PLC 말고 시나리오로. 접근하면 멈추고 조종할 수 있게. RTG VR 조종도."
    ///
    ///   ① 씬이 뜨면 크레인마다 <see cref="CraneDemoRunner"/> 를 붙이고 PlcBridge 를 끈다(PLC 재생 대신 시나리오).
    ///   ② 옮길 컨테이너와 내려놓을 자리는 여기서 한 벌(<see cref="CraneDemoRunner.Site"/>)로 나눠 준다 — 크레인끼리 같은
    ///      컨테이너·자리를 잡지 않게. STS 가 먼저(배 컨테이너), RTG 는 가까운 야드 컨테이너를 나눠 갖는다.
    ///   ③ 조종기는 한 번에 한 대 — 플레이어와 가장 가까운 크레인의 <see cref="StsCraneVRController"/> 만 켠다. 셋 다 켜 두면
    ///      스틱 하나로 세 대가 같이 움직이고, 로코모션 켜기/끄기를 서로 덮어쓴다. 조종 중(운전실 시점 포함)엔 안 바꾼다.
    ///   ④ 그 크레인 발치에 접근했거나 조종 중이면 시나리오가 멈춘다(<see cref="Holds"/>). 떠나면 이어서 돈다.
    ///
    /// 빌드에선 항상 켜진다. 에디터에선 메뉴 'PLC/시연 시나리오 (에디터 Play)' 를 켰을 때만 — PLC·KPI 작업을 방해하지 않게.
    /// 가설 구현·Quest 미검증.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PortDemoDirector : MonoBehaviour
    {
        /// <summary>에디터 Play 에서도 켤지(EditorPrefs). 빌드는 무관하게 항상 켠다.</summary>
        public const string EditorPrefKey = "PortDemo.InEditor";

        [Tooltip("크레인 바닥 투영 외곽에서 이 거리(실척 m) 안이면 '접근' — 그 크레인 시나리오가 멈춘다.")]
        [SerializeField] float approachMeters = 6f;
        [Tooltip("조종기를 다른 크레인으로 넘기려면 그쪽이 이만큼(실척 m) 더 가까워야 한다 — 경계에서 깜빡임 방지.")]
        [SerializeField] float switchMarginMeters = 2f;
        [Tooltip("가까운 크레인 판정 주기(초).")]
        [SerializeField] float selectInterval = 0.2f;

        sealed class Entry
        {
            public StsCrane crane;
            public StsCraneVRController ctrl;
            public CraneDemoRunner runner;
            public Transform mover;          // 크레인과 같이 주행하는 기준(갠트리)
            public Vector2 fpMin, fpMax;     // 바닥 투영 외곽(XZ) — mover 기준 상대
        }

        readonly List<Entry> cranes = new List<Entry>();
        Entry active;
        bool activeNear;
        float nextSelect;
        static PortDemoDirector inst;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
#if UNITY_EDITOR
            if (!UnityEditor.EditorPrefs.GetBool(EditorPrefKey, false)) return;
#endif
            if (FindAnyObjectByType<PortDemoDirector>() != null || FindAnyObjectByType<StsCrane>() == null) return;
            new GameObject("PortDemoDirector (auto)").AddComponent<PortDemoDirector>();
        }

        /// <summary>이 크레인의 시나리오를 멈춰야 하나 — 조종기를 받는 크레인이고, 플레이어가 발치에 왔거나 조종 중.</summary>
        public static bool Holds(StsCrane c) =>
            inst != null && inst.active != null && inst.active.crane == c && (inst.activeNear || inst.active.ctrl.ControlActive);

        static bool IsRtg(StsCrane c) => c.GetComponent<RtgBogieSteering>() != null;   // RTG 는 보기 조향이 붙어 있다

        void Awake() => inst = this;
        void OnDestroy() { if (inst == this) inst = null; }

        IEnumerator Start()
        {
            foreach (var c in FindObjectsByType<StsCrane>(FindObjectsSortMode.None))
            {
                foreach (var b in c.GetComponents<PlcBridge>()) b.enabled = false;   // PLC 재생 말고 시나리오 — OnDisable 이 PlcDriven 도 끈다
                var ctrl = c.GetComponent<StsCraneVRController>();
                if (ctrl == null) ctrl = c.gameObject.AddComponent<StsCraneVRController>();   // RTG 도 VR 조종
                var e = new Entry
                {
                    crane = c, ctrl = ctrl,
                    runner = c.gameObject.AddComponent<CraneDemoRunner>(),
                    mover = c.Gantry is Component g ? g.transform : c.transform,
                };
                Footprint(e);
                cranes.Add(e);
            }
            // STS 먼저, RTG 는 주행축(Z) 순 — 이 순서가 곧 컨테이너·자리 우선권이다(실행마다 같게).
            cranes.Sort((a, b) => IsRtg(a.crane) != IsRtg(b.crane)
                ? IsRtg(a.crane).CompareTo(IsRtg(b.crane))
                : a.mover.position.z.CompareTo(b.mover.position.z));
            foreach (var e in cranes) e.ctrl.enabled = false;
            if (cranes.Count > 0) Activate(cranes[0]);

            yield return null;   // 그랩버·무버·보기 조향이 Start 를 마친 뒤에 잰다

            var site = new CraneDemoRunner.Site();
            var yardName = new Regex(@"^Cont(20|40)_\d+$");
            foreach (var lg in FindObjectsByType<LODGroup>(FindObjectsSortMode.None))
            {
                var t = lg.transform;
                bool ship = t.name.StartsWith("ShipContainer"), yard = !ship && yardName.IsMatch(t.name);
                if (!ship && !yard) continue;
                if (!CraneDemoRunner.TryBounds(t, out var b)) continue;
                if (yard) MakeGrabbable(t, b);   // 야드 컨테이너는 콜라이더·강체가 없어 VR 로 못 집는다
                (ship ? site.ship : site.yard).Add(t);
                site.occupied.Add(b);
            }
            site.haveLand = CranePlayerStartPlacer.TryGetLand(out site.land);
            foreach (var e in cranes) if (IsRtg(e.crane)) site.rtgs.Add(e.crane);

            foreach (var e in cranes) e.runner.Plan(site);
            foreach (var e in cranes) e.runner.Go();
        }

        // 야드 배경 컨테이너를 잡을 수 있게 — PlcCargoReplay.MakeBox 와 같은 구성(바운즈 콜라이더 + kinematic 강체).
        static void MakeGrabbable(Transform t, Bounds b)
        {
            if (t.GetComponentInChildren<Collider>() == null)
            {
                var s = t.lossyScale;
                var bc = t.gameObject.AddComponent<BoxCollider>();
                bc.center = t.InverseTransformPoint(b.center);
                bc.size = new Vector3(b.size.x / s.x, b.size.y / s.y, b.size.z / s.z);
            }
            var rb = t.GetComponent<Rigidbody>();
            if (rb == null) rb = t.gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true; rb.useGravity = false;   // 놓은 자리에 그대로 — 야드 바닥엔 콜라이더가 없다
        }

        void Update()
        {
            if (cranes.Count == 0 || Time.unscaledTime < nextSelect) return;
            nextSelect = Time.unscaledTime + selectInterval;
            var cam = Camera.main;
            if (cam == null) { activeNear = false; return; }

            Vector3 p = cam.transform.position;
            Entry best = null; float bestD = float.MaxValue;
            foreach (var e in cranes)
            {
                float d = Distance(e, p);
                if (d < bestD) { bestD = d; best = e; }
            }
            // 붙잡는 건 '운전 중'(조종·갠트리 모드 또는 운전실 시점)일 때만 — 이동모드로 걷는 중엔 조종 토글이 켜져 있어도 넘긴다.
            //   옛 조건(ControlActive)은 STS 를 조종하다 B 로 이동모드로 바꿔 RTG 로 걸어가도 조종기가 STS 에 잠겨
            //   RTG 를 조종할 수 없었다(2026-09-15 오너 보고 · RtgControlSmoke: RTG 발치 거리 0 인데 조종기 STS 유지).
            bool locked = active != null && (active.ctrl.CabView || (active.ctrl.ControlActive && active.ctrl.CraneMode));
            if (!locked && best != active && (active == null || bestD + switchMarginMeters * StsConfig.ModelScale < Distance(active, p)))
                Activate(best);
            activeNear = active != null && Distance(active, p) <= approachMeters * StsConfig.ModelScale;
        }

        void Activate(Entry e)
        {
            bool control = active != null && active.ctrl.ControlActive;   // 걸어서 넘어가도 조종 토글(모드 HUD)은 이어받는다
            foreach (var x in cranes)
                if (x != e && x.ctrl.enabled) { x.ctrl.ControlActive = false; x.ctrl.enabled = false; }   // 끄면 로코모션이 이동모드로 복구된다
            e.ctrl.enabled = true;
            e.ctrl.ControlActive = control;
            active = e;
            Debug.Log($"[PortDemo] 조종기 → {e.crane.name}");
        }

        // 바닥 투영 외곽까지의 수평거리(0 = 크레인 밑). 외곽은 시작 때 렌더러로 한 번 재고, 주행은 mover 로 따라간다.
        static float Distance(Entry e, Vector3 p)
        {
            Vector3 o = e.mover.position;
            float dx = Mathf.Max(o.x + e.fpMin.x - p.x, 0f, p.x - (o.x + e.fpMax.x));
            float dz = Mathf.Max(o.z + e.fpMin.y - p.z, 0f, p.z - (o.z + e.fpMax.y));
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        static void Footprint(Entry e)
        {
            Vector3 o = e.mover.position;
            if (!CraneDemoRunner.TryBounds(e.crane.transform, out var b)) b = new Bounds(o, Vector3.zero);
            e.fpMin = new Vector2(b.min.x - o.x, b.min.z - o.z);
            e.fpMax = new Vector2(b.max.x - o.x, b.max.z - o.z);
        }
    }
}
