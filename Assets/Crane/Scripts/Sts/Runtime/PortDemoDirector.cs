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
    ///   ⑤ 접근 범위를 바닥에 반투명 띠로 그린다 — 안에 들어서면 진해진다(오너 2026-09-16 "게임처럼").
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
            public float groundY;            // 크레인 바운즈 바닥(바퀴 밑) — 접근 띠 높이
            public LineRenderer ring;        // 접근 범위 바닥 띠
        }

        static readonly Color RingIdle = new Color(0.3f, 0.85f, 1f, 0.35f);
        static readonly Color RingInside = new Color(0.3f, 0.85f, 1f, 0.9f);

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

        /// <summary>스모크용 — 띠 점이 접근 경계(Distance = approach)에서 벗어난 최대 거리(실척 m). 모서리 수식·눕힘·주행 추종이 틀리면 커진다.</summary>
        public static float RingMaxErrM()
        {
            if (inst == null || inst.cranes.Count == 0) return float.MaxValue;
            float r = inst.approachMeters * StsConfig.ModelScale, max = 0f;
            foreach (var e in inst.cranes)
                for (int i = 0; i < e.ring.positionCount; i++)
                    max = Mathf.Max(max, Mathf.Abs(Distance(e, e.ring.transform.TransformPoint(e.ring.GetPosition(i))) - r));
            return max / StsConfig.ModelScale;
        }

        static bool IsRtg(StsCrane c) => c.GetComponent<RtgBogieSteering>() != null;   // RTG 는 보기 조향이 붙어 있다

        void Awake() => inst = this;
        void OnDestroy() { if (inst == this) inst = null; }

        IEnumerator Start()
        {
            var ringMat = RingMaterial();
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
                e.ring = Ring(e, ringMat);
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
            foreach (var e in cranes)   // 띠는 매 프레임 주행을 따라간다 — 판정 주기(0.2초)로 옮기면 VR 에서 뚝뚝 끊긴다
            {
                Vector3 o = e.mover.position;
                e.ring.transform.position = new Vector3(o.x, e.groundY + 0.1f * StsConfig.ModelScale, o.z);   // 바닥과 z-파이팅 방지 실척 0.1m
            }
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
            foreach (var e in cranes) e.ring.startColor = e.ring.endColor = e == active && activeNear ? RingInside : RingIdle;
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
            e.groundY = b.min.y;
        }

        // 접근 범위 바닥 띠 — Distance() ≤ approach 인 영역 그대로: 바닥 투영 사각형을 반경 approach 로 부풀린 모서리 둥근 사각형.
        //   크레인마다 외곽이 런타임에 정해져서 메시가 아니라 LineRenderer 로 그린다. 로컬 XY 평면 → 월드 XZ 로 눕힌다.
        LineRenderer Ring(Entry e, Material mat)
        {
            var go = new GameObject($"ApproachRing_{e.crane.name}");
            go.transform.SetParent(transform, false);
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);   // 로컬 Y → 월드 Z, 로컬 Z(띠 면 법선) → 월드 아래
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.loop = true;
            lr.alignment = LineAlignment.TransformZ;
            lr.material = mat;
            lr.widthMultiplier = 0.8f * StsConfig.ModelScale;   // 띠 폭 실척 0.8m
            lr.startColor = lr.endColor = RingIdle;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;

            const int seg = 12;   // 모서리 1/4 원 분할
            float r = approachMeters * StsConfig.ModelScale;
            lr.positionCount = 4 * (seg + 1);
            for (int k = 0; k < 4; k++)   // 모서리 순서: (+x,+z) → (-x,+z) → (-x,-z) → (+x,-z), 각 모서리는 k·90° 에서 시작
            {
                float cx = k == 0 || k == 3 ? e.fpMax.x : e.fpMin.x;
                float cz = k < 2 ? e.fpMax.y : e.fpMin.y;
                for (int i = 0; i <= seg; i++)
                {
                    float a = (k + (float)i / seg) * Mathf.PI * 0.5f;
                    lr.SetPosition(k * (seg + 1) + i, new Vector3(cx + r * Mathf.Cos(a), cz + r * Mathf.Sin(a), 0f));
                }
            }
            return lr;
        }

        // 띠 단면 알파 = 가우시안 exp(-6v²), v∈[-1,1] — 가운데 진하고 양 가장자리로 번져 사라진다.
        static Material RingMaterial()
        {
            const int n = 32;
            var tex = new Texture2D(1, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int i = 0; i < n; i++)
            {
                float v = (i + 0.5f) / n * 2f - 1f;
                tex.SetPixel(0, i, new Color(1f, 1f, 1f, Mathf.Exp(-6f * v * v)));
            }
            tex.Apply();
            return new Material(Shader.Find("Sprites/Default")) { mainTexture = tex };   // GraphicsSettings 항상 포함 셰이더 — 빌드에서도 찾힌다
        }
    }
}
