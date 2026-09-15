using System;

namespace Container.Crane.Sts
{
    /// <summary>
    /// 매달린 화물(스프레더 + 컨테이너)의 흔들림 — 수평 두 축을 각각 작은 각 진자로 푼다. 실척 SI 단위.
    /// Unity 에 기대지 않는다 — <see cref="SelfCheck"/> 를 dotnet 으로 바로 돌려 식을 검증하기 위해서다.
    ///
    ///   θ̈ = −(g/L)·θ − (2·L̇/L)·θ̇ − 2ζ·√(g/L)·θ̇ − a/L + F/(m·L)        흔들림 변위 d = L·θ
    ///
    ///   · 1·2항: 길이가 변하는 진자 L·θ̈ + 2·L̇·θ̇ + g·θ = 0 (작은 각). 권상(L̇ &lt; 0)하면 진폭이 커진다.
    ///   · 3항: 흔들림 감쇠. ζ 는 흔들림 방지(anti-sway) 유무로 고른다 — 로프·공기만이면 0.02, 장치가 있으면 0.7.
    ///   · 4항: 매달림점(트롤리) 가속 a. 정상 상태에서 로프는 tanθ = a/g 만큼 기운다.
    ///   · 5항: 바람 등 수평 외력 F. 정상 상태 편향 d = L·F/(m·g).
    /// 적분은 반암시 오일러(ω 먼저, θ 는 새 ω 로) — 진동계에서 에너지가 불어나지 않는 symplectic 방식이다.
    ///
    /// 자체검사: 이 파일 하나를 Compile Include 한 net 콘솔에서 SwayDynamics.SelfCheck() 를 부른다 — 어긋나면 예외.
    ///   2026-09-15 실행: 주기 0.00% · 바람 편향 0.00% · 가속 뒤처짐 0.00% · 감쇠 0.18% · 권상 단열 0.64%.
    /// </summary>
    public static class SwayDynamics
    {
        public const double G = 9.81;
        /// <summary>공기 밀도 kg/m³ — ISA 해면 15°C.</summary>
        public const double AirDensity = 1.225;
        /// <summary>직육면체 컨테이너 항력계수(바람이 면에 수직) — 날카로운 모서리 박스의 통상값 1.05~1.3 가운데.</summary>
        public const double ContainerCd = 1.2;
        /// <summary>식이 발산하지 않게 쓰는 최소 로프 길이(m). 도킹 직전(실측 ~3.5m)보다 짧다.</summary>
        public const double MinRopeM = 1.0;

        /// <summary>한 축 한 스텝. theta(rad)·omega(rad/s) 를 dt 만큼 전진.</summary>
        public static void Step(ref double theta, ref double omega, double ropeM, double ropeRate,
                                double zeta, double pivotAccel, double forceN, double massKg, double dt)
        {
            double L = Math.Max(ropeM, MinRopeM);
            double w0 = Math.Sqrt(G / L);
            double alpha = -w0 * w0 * theta - (2.0 * ropeRate / L) * omega - 2.0 * zeta * w0 * omega
                           - pivotAccel / L + forceN / (massKg * L);
            omega += alpha * dt;
            theta += omega * dt;
        }

        /// <summary>면적 area(m²)에 상대풍속 v(m/s, 부호=방향)가 만드는 항력 N = ½·ρ·Cd·A·v·|v|.</summary>
        public static double WindForce(double v, double area) => 0.5 * AirDensity * ContainerCd * area * v * Math.Abs(v);

        /// <summary>지금 상태의 흔들림 진폭(m) = √(d² + (ḋ/ω₀)²) — 영점을 지나는 순간에도 진폭을 준다.</summary>
        public static double Amplitude(double theta, double omega, double ropeM)
        {
            double L = Math.Max(ropeM, MinRopeM);
            return L * Math.Sqrt(theta * theta + omega * omega * L / G);
        }

        /// <summary>식 검증 — 해석해가 있는 다섯 경우를 수치적분과 맞춘다. 어긋나면 예외, 맞으면 요약 문자열.</summary>
        public static string SelfCheck()
        {
            const double dt = 0.02;   // Unity 고정 틱과 같다
            var r = new System.Text.StringBuilder();

            // ① 주기 T = 2π√(L/g) — 감쇠 0, L = 25m. 위로 지나는 영점 사이 시간을 잰다(선형 보간).
            {
                double L = 25, th = 0.05, om = 0, t = 0, prev = th, first = -1, last = -1; int n = 0;
                double dtf = 0.001;
                while (t < 120)
                {
                    Step(ref th, ref om, L, 0, 0, 0, 0, 1, dtf); t += dtf;
                    if (prev < 0 && th >= 0)
                    {
                        double tc = t - dtf * th / (th - prev);
                        if (first < 0) first = tc; else { last = tc; n++; }
                    }
                    prev = th;
                }
                double T = (last - first) / n, want = 2 * Math.PI * Math.Sqrt(L / G);
                Expect("주기", T, want, 0.005, r);
            }
            // ② 바람 정상 편향 d = L·F/(m·g) — 40ft 옆면 31.6m², 20m/s, 빈 컨테이너 3.8t + 스프레더 13t.
            {
                double L = 25, m = 16800, F = WindForce(20, 12.192 * 2.591), th = 0, om = 0;
                for (int i = 0; i < 60 / dt; i++) Step(ref th, ref om, L, 0, 0.7, 0, F, m, dt);
                Expect("바람 편향", L * th, L * F / (m * G), 0.01, r);
            }
            // ③ 가속 중 뒤처짐 d = −L·a/g — 트롤리 정격 가속 0.6 m/s²(CraneAxisProfile).
            {
                double L = 25, a = 0.6, th = 0, om = 0;
                for (int i = 0; i < 60 / dt; i++) Step(ref th, ref om, L, 0, 0.7, a, 0, 1, dt);
                Expect("가속 뒤처짐", L * th, -L * a / G, 0.01, r);
            }
            // ④ 감쇠 포락선 A(t) = A₀·e^(−ζω₀t) — ζ = 0.02, 60초.
            {
                double L = 25, z = 0.02, th = 0.05, om = 0, A0 = Amplitude(th, om, L);
                double sec = 60; for (int i = 0; i < sec / dt; i++) Step(ref th, ref om, L, 0, z, 0, 0, 1, dt);
                Expect("감쇠", Amplitude(th, om, L), A0 * Math.Exp(-z * Math.Sqrt(G / L) * sec), 0.02, r);
            }
            // ⑤ 천천히 감아올리면 각진폭 ∝ L^(−3/4) — 단열불변량 E/ω 보존(E ∝ L·θ², ω ∝ L^(−1/2)). 30m → 10m, 400초.
            {
                double L0 = 30, L1 = 10, sec = 400, th = 0.02, om = 0, L = L0, rate = (L1 - L0) / sec;
                double A0 = Amplitude(th, om, L0) / L0;
                for (int i = 0; i < sec / dt; i++) { Step(ref th, ref om, L, rate, 0, 0, 0, 1, dt); L += rate * dt; }
                Expect("권상 단열", Amplitude(th, om, L1) / L1, A0 * Math.Pow(L1 / L0, -0.75), 0.03, r);
            }
            return r.ToString();
        }

        static void Expect(string what, double got, double want, double relTol, System.Text.StringBuilder r)
        {
            double err = Math.Abs(got - want) / Math.Max(Math.Abs(want), 1e-12);
            r.AppendLine($"{what}: 수치 {got:G5} · 해석 {want:G5} · 오차 {err * 100:F2}% (허용 {relTol * 100:F1}%)");
            if (!(err <= relTol)) throw new Exception($"[SwayDynamics] {what} 불일치 — 수치 {got} vs 해석 {want} (오차 {err:P2})");
        }
    }
}
