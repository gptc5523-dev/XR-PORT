using UnityEngine;

namespace Container.Crane.Sts
{
    /// <summary>
    /// 흔들림 물리 — 스프레더와 그 부모 사이에 노드(<see cref="NodeName"/>)를 끼우고 그 노드만 수평으로 옮긴다.
    /// 스프레더 아래 전부(트위스트락·푸셔·잡은 컨테이너·RTG 시브)가 같이 흔들린다. 무버는 아무도 이 노드를 쓰지 않고,
    /// 노드는 회전·스케일 항등이라 스프레더 로컬 자세·WorldAxis 는 끼우기 전과 같다(STS·FBX RTG 배선 차이와 무관).
    ///
    /// 식은 <see cref="SwayDynamics"/>(자체검사 통과). 입력은 실척 SI:
    ///   · 매달림점 가속 a = 트롤리 월드 위치의 2차 차분 — 트롤리 횡행과 갠트리 주행이 다 들어간다.
    ///   · 로프 길이 L = 트롤리 − 스프레더 높이차, L̇ = 그 차분(권상).
    ///   · 바람 F = ½·ρ·Cd·A·v|v| 를 축마다 — X 로 부는 바람은 컨테이너 (길이z × 높이) 면, Z 로 부는 바람은 (폭x × 높이) 면.
    ///     풍속은 PlcBridge 가 켜져 있으면 PLC 스냅샷, 아니면 <see cref="WindMps"/>·<see cref="WindFromDeg"/> + 돌풍.
    ///     방위: +Z = 북 · +X = 동, '불어오는' 방위(기상 관례) — 270°(서풍)는 +X 로 분다.
    ///   · 질량 m = 스프레더·헤드블록 + 잡은 컨테이너 표시 하중.
    /// 멈춤: 받침에 얹혔거나(SpreaderGrabber.IsLanded)·권상 하한이면 플리퍼·셀가이드가 잡듯 <see cref="LandTau"/> 시정수로 0,
    ///   옆으로 부딪히면(LoadCollision) 흔들림 속도 0.
    /// 빈 스프레더는 바람을 안 받는다고 둔다 — 격자 프레임이라 받는 면적이 작다(가속 흔들림은 받는다).
    /// </summary>
    [DefaultExecutionOrder(40)]   // 축 무버(PlcBridge −100 · VR 0) 뒤 · SpreaderGrabber 통과방지(50) 앞
    [DisallowMultipleComponent]
    public sealed class CraneSway : MonoBehaviour
    {
        public const string NodeName = "SwayNode";

        /// <summary>흔들림 방지 장치 — PLC 생성기도 켠 크레인으로 데이터를 만든다(PlcSim/generate.py antisway = True).</summary>
        public static bool AntiSway = true;
        /// <summary>감쇠비 — 장치 있음 0.7(과도 없이 가장 빨리 잦아드는 근처), 로프·공기뿐 0.02.</summary>
        public const double ZetaAntiSway = 0.7, ZetaBare = 0.02;
        /// <summary>풍속(m/s)·불어오는 방위(°). 기본 = PlcSim/generate.py Sim(wind=8.0, wind_dir=270.0).</summary>
        public static float WindMps = 8f, WindFromDeg = 270f;
        /// <summary>돌풍 — 평균풍속에 ±이 비율을 펄린 잡음(주기 ~5초)으로 얹는다. 난류강도 0.15~0.2 가 항만 통상.</summary>
        public static float Gust = 0.2f;

        const double SpreaderMassKg = 13000;   // 텔레스코픽 스프레더 ~10t + 헤드블록 ~3t (카탈로그 범위 — 벤더 확정 시 교체)
        const float TeleportMps = 20f;         // 매달림점·로프가 이보다 빠르면 순간이동(CSV 되감기·네트워크 스냅) — 흔들림 초기화
        const float LandTau = 0.2f;

        StsCrane crane;
        SpreaderGrabber grabber;
        Plc.PlcBridge bridge;
        Transform node, spreader, pivot;
        AxisMoverBase hoist;
        double thX, omX, thZ, omZ;
        Vector3 lastPivot, lastVel;
        float lastL, gustSeed;
        int primed;
        Transform sizedFor;
        Vector3 heldSizeM;

        /// <summary>지금 흔들림 변위(월드, 모델 단위). 자동 운전은 부착점에서 이만큼 빼고 목표를 잡는다.</summary>
        public Vector3 Offset { get; private set; }
        /// <summary>두 축 합 진폭(실척 m) — 영점을 지나는 순간에도 진폭을 준다.</summary>
        public float AmplitudeM { get; private set; }
        public bool Settled(float tolM) => AmplitudeM <= tolM;

        public static CraneSway Of(StsCrane c) => c != null ? c.GetComponent<CraneSway>() : null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            foreach (var c in FindObjectsByType<StsCrane>(FindObjectsSortMode.None))
                if (c.GetComponent<CraneSway>() == null) c.gameObject.AddComponent<CraneSway>();
        }

        void Start()
        {
            crane = GetComponent<StsCrane>();
            grabber = GetComponent<SpreaderGrabber>();
            bridge = GetComponent<Plc.PlcBridge>();
            spreader = crane != null && crane.Spreader is Component s ? s.transform : null;
            pivot = crane != null && crane.Trolley is Component t ? t.transform : null;
            hoist = crane != null ? crane.Spreader as AxisMoverBase : null;
            if (spreader == null || pivot == null || spreader.parent == null) { enabled = false; return; }

            var parent = spreader.parent;
            node = new GameObject(NodeName).transform;
            node.SetParent(parent, false);
            node.SetSiblingIndex(spreader.GetSiblingIndex());
            spreader.SetParent(node, false);   // 노드가 항등이라 로컬 자세·스케일 그대로 = 월드 자세 그대로
            gustSeed = Random.value * 100f;
        }

        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            float inv = 1f / StsConfig.ModelScale;
            Vector3 p = pivot.position;
            float L = (p.y - spreader.position.y) * inv;
            Vector3 v = (p - lastPivot) * (inv / dt);
            Vector3 a = (v - lastVel) / dt;
            float Ldot = (L - lastL) / dt;
            lastPivot = p; lastVel = v; lastL = L;
            if (v.magnitude > TeleportMps || Mathf.Abs(Ldot) > TeleportMps) { thX = omX = thZ = omZ = 0; primed = 0; }
            if (primed < 2) { primed++; a = Vector3.zero; Ldot = 0f; }

            // 바람(실척 m/s) — 부는 방향 = 불어오는 방위의 반대
            float wMps = WindMps, wFrom = WindFromDeg;
            if (bridge != null && bridge.isActiveAndEnabled && bridge.Active) { var snap = bridge.Latest; wMps = snap.WindSpeed; wFrom = snap.WindDirection; }
            float gust = 1f + Gust * (2f * Mathf.PerlinNoise(gustSeed, Time.time * 0.2f) - 1f);
            float rad = wFrom * Mathf.Deg2Rad;
            double wx = -Mathf.Sin(rad) * wMps * gust, wz = -Mathf.Cos(rad) * wMps * gust;

            double mass = SpreaderMassKg, fx = 0, fz = 0;
            var attach = crane.Attach;
            var held = attach != null ? attach.AttachedContainer : null;
            if (held != sizedFor) { sizedFor = held; heldSizeM = held != null && CraneDemoRunner.TryBounds(held, out var hb) ? hb.size * inv : Vector3.zero; }
            if (held != null)
            {
                mass += attach.AttachedMassKg;
                fx = SwayDynamics.WindForce(wx, heldSizeM.z * heldSizeM.y);
                fz = SwayDynamics.WindForce(wz, heldSizeM.x * heldSizeM.y);
            }

            double zeta = AntiSway ? ZetaAntiSway : ZetaBare;
            SwayDynamics.Step(ref thX, ref omX, L, Ldot, zeta, a.x, fx, mass, dt);
            SwayDynamics.Step(ref thZ, ref omZ, L, Ldot, zeta, a.z, fz, mass, dt);

            bool landed = (grabber != null && grabber.IsLanded) || (hoist != null && hoist.AtLowerLimit);
            if (landed) { double k = System.Math.Exp(-dt / LandTau); thX *= k; thZ *= k; omX = omZ = 0; }
            if (grabber != null && grabber.LoadCollision) omX = omZ = 0;

            double Lc = System.Math.Max(L, SwayDynamics.MinRopeM);
            Offset = new Vector3((float)(Lc * thX), 0f, (float)(Lc * thZ)) * StsConfig.ModelScale;
            // 진폭은 평형점 기준 — θ̈ = 0, θ̇ = 0 이면 θ_eq = (F/m − a)/g. 바람의 정적 편향은 흔들림이 아니라서 뺀다.
            double eqX = (fx / mass - a.x) / SwayDynamics.G, eqZ = (fz / mass - a.z) / SwayDynamics.G;
            double ax = SwayDynamics.Amplitude(thX - eqX, omX, L), az = SwayDynamics.Amplitude(thZ - eqZ, omZ, L);
            AmplitudeM = (float)System.Math.Sqrt(ax * ax + az * az);
            node.localPosition = node.parent.InverseTransformVector(Offset);
        }
    }
}
