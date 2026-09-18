using System;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;
using UnityEngine.InputSystem;
using AIXRCrane.Crane.Sts;
using NVec = System.Numerics.Vector3;
using NQuat = System.Numerics.Quaternion;

namespace AIXRCrane.Crane.Flat
{
    /// <summary>XREAL One Pro 머리 추적 — 안경이 USB 로 꽂힌 '이 기기'에서 IMU 를 읽어 고개 방향을 낸다.
    ///   오너 2026-09-18 "안경은 머리 돌리면 좌·우·위·아래 다 볼 수 있거든 … 라이브러리 형태로".
    ///   XREAL SDK 없이 돈다(Unity 6000.4 호환 걱정 없음). 안경이 없으면 조용히 3초마다 다시 본다.
    ///
    ///   ★ 통로: 안경의 USB 이더넷 169.254.2.1:52998(TCP, 읽기 전용 — 아무것도 보내지 않는다).
    ///     공개 예제 One-Pro-IMU-Retriever-Demo(MIT)의 앞 표식을 따랐고, 나머지는 2026-09-18 실측으로 정했다:
    ///     메시지 134 B 고정(예제의 끝 표식은 이 펌웨어와 다르다) · 14 B 에 int64 나노초 시각(이웃 차 ≈ 1 ms) ·
    ///     34 B 부터 float 6개(자이로 rad/s 3 + 가속도 m/s² 3) ·
    ///     78 B 에 센서 표식. 초당 센서 1000 + 기타 400.
    ///   ★ 축(실측): X=오른쪽, Y=아래, Z=앞 — 오른손 좌표. 오른쪽 90° 에 +Y 적분 1.5 rad, 고개 들기에 +X,
    ///     고개를 들면 중력이 +Z 로 옮겨 갔다. 쓴 상태에서 안경이 약 24° 기울어 있어 축별로 더하면 좌우가 섞인다 →
    ///     쿼터니언으로 합치고 중력으로 기울기를 잡는다(Mahony). 방위(좌우)는 자기센서가 없어 천천히 흐른다 → R 로 정면 재설정.
    ///   ★ 서버가 그리는 방식(Moonlight)에서는 쓰지 않는다 — 안경이 서버가 아니라 Beam Pro 에 꽂혀 있고, 되돌아오는 지연이 크다.</summary>
    [AddComponentMenu("AI-XR Crane/Flat Mode/XREAL Head Tracker")]
    [DisallowMultipleComponent]
    public sealed class XrealHeadTracker : MonoBehaviour
    {
        public static XrealHeadTracker Instance { get; private set; }

        const string Ip = "169.254.2.1";
        const int Port = 52998, MsgLen = 134, StampAt = 14, FloatsAt = 34, MarkAt = 78;
        static readonly byte[] Header = { 0x28, 0x36, 0x00, 0x00, 0x00, 0x80 };
        static readonly byte[] SensorMark = { 0x00, 0x40, 0x1F, 0x00, 0x00, 0x40 };

        [Tooltip("중력 보정 세기(Mahony Kp). 크면 기울기가 빨리 잡히지만 가속할 때 흔들린다.")]
        [SerializeField] float kp = 1.5f;
        [Tooltip("자이로 치우침을 잴 때 가만히 둔 것으로 볼 샘플 수(초당 1000).")]
        [SerializeField] int biasSamples = 500;
        [Tooltip("좌우가 거꾸로 돌면 켠다(안경 펌웨어·착용 방향이 다를 때).")]
        [SerializeField] bool invertYaw;
        [Tooltip("위아래가 거꾸로면 켠다.")]
        [SerializeField] bool invertPitch;

        /// <summary>안경에서 데이터가 오는 중인가(치우침 측정까지 끝나 방향을 믿을 수 있을 때).</summary>
        public bool Connected => Volatile.Read(ref ready) == 1;
        /// <summary>정면 기준 고개 좌우(도, 오른쪽 +) · 위아래(도, 위 +). 메인 스레드에서 읽는다.</summary>
        public float YawRightDeg { get; private set; }
        public float PitchUpDeg { get; private set; }

        Thread thread;
        volatile bool running;
        TcpClient client;
        int ready;                                   // 0/1 — Interlocked
        readonly object gate = new object();
        NQuat q = NQuat.Identity;                    // 몸(안경)→세계(Z 위). 스레드 전용, 읽을 땐 gate
        float yaw0, pitch0; bool zeroPending = true; // 정면 기준 — 첫 안정 자세, R 로 다시
        bool loggedMissing;

        void Awake() => Instance = this;

        void Start()
        {
            bool ok = SelfCheck(out string why);
            QaLog.Check("HEAD", "selfcheck", ok, why);
            running = true;
            thread = new Thread(Run) { IsBackground = true, Name = "XrealHeadTracker" };
            thread.Start();
        }

        void OnDestroy()
        {
            running = false;
            try { client?.Close(); } catch { }
            if (Instance == this) Instance = null;
        }

        /// <summary>지금 보는 쪽을 정면으로 — 방위가 흘렀을 때, 자세를 바꿔 앉았을 때.</summary>
        public void Recenter() { lock (gate) zeroPending = true; }

        void Update()
        {
            if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame) Recenter();
            if (!Connected) { YawRightDeg = PitchUpDeg = 0f; return; }
            NQuat cur; float y0, p0; bool zero;
            lock (gate) { cur = q; zero = zeroPending; if (zero) zeroPending = false; }
            HeadAngles(cur, out float yaw, out float pitch);
            if (zero) { lock (gate) { yaw0 = yaw; pitch0 = pitch; } }
            lock (gate) { y0 = yaw0; p0 = pitch0; }
            float yr = -Mathf.DeltaAngle(y0, yaw);   // 세계 Z 위에서 시계방향(오른쪽) = 방위각 감소
            float pu = pitch - p0;
            YawRightDeg = invertYaw ? -yr : yr;
            PitchUpDeg = invertPitch ? -pu : pu;
        }

        // ── 수신 스레드 ────────────────────────────────────────────────────────────
        void Run()
        {
            while (running)
            {
                try
                {
                    using (var c = new TcpClient())
                    {
                        client = c;
                        var ar = c.BeginConnect(Ip, Port, null, null);
                        if (!ar.AsyncWaitHandle.WaitOne(1000) || !c.Connected)
                        {
                            if (!loggedMissing) { loggedMissing = true; Debug.Log("[HeadTracker] 안경(169.254.2.1) 없음 — 3초마다 다시 봅니다."); }
                            Sleep3(); continue;
                        }
                        c.EndConnect(ar);
                        loggedMissing = false;
                        Debug.Log("[HeadTracker] 안경 IMU 연결 — 치우침을 재는 동안(0.5초) 가만히 두세요.");
                        ReadLoop(c.GetStream());
                    }
                }
                catch (Exception e)
                {
                    // 안경이 없는 기기(서버 등)는 연결 자체가 예외로 실패한다 — 3초마다 로그가 쌓이지 않게 '없음'은 한 번만.
                    if (!running) { }
                    else if (Connected) Debug.Log($"[HeadTracker] 끊김: {e.Message}");
                    else if (!loggedMissing) { loggedMissing = true; Debug.Log($"[HeadTracker] 안경(169.254.2.1) 없음 — 3초마다 다시 봅니다. ({e.Message})"); }
                }
                Interlocked.Exchange(ref ready, 0);
                Sleep3();
            }
        }

        void Sleep3() { for (int i = 0; i < 30 && running; i++) Thread.Sleep(100); }

        void ReadLoop(NetworkStream s)
        {
            var buf = new byte[1 << 16];
            int len = 0, calN = 0;
            NVec bias = default, calSum = default;
            long lastStamp = 0; bool haveQ = false;

            while (running)
            {
                int n = s.Read(buf, len, buf.Length - len);
                if (n <= 0) return;
                len += n;
                int i = 0;
                while (true)
                {
                    int h = IndexOf(buf, Header, i, len);
                    if (h < 0 || len - h < MsgLen) { i = h < 0 ? Math.Max(i, len - Header.Length + 1) : h; break; }
                    i = h + MsgLen;
                    if (!TryParse(buf, h, out NVec g, out NVec a)) continue;

                    if (calN < biasSamples)
                    {
                        // 움직이면 다시 잰다 — 쓰는 도중에 켜져도 치우침이 틀리지 않게.
                        if (g.Length() > 0.05f) { calN = 0; calSum = default; continue; }
                        calSum += g; calN++;
                        if (calN == biasSamples) bias = calSum / calN;
                        continue;
                    }
                    // 간격은 안경이 찍은 시각으로 — TCP 가 샘플을 묶어 보내 받는 쪽 시계로 재면 묶음 안은 0 이 된다.
                    long stamp = BitConverter.ToInt64(buf, h + StampAt);
                    float dt = lastStamp == 0 ? 0.001f : Mathf.Clamp((stamp - lastStamp) * 1e-9f, 0f, 0.05f);
                    lastStamp = stamp;
                    lock (gate)
                    {
                        if (!haveQ) { q = FromUp(a); haveQ = true; zeroPending = true; }
                        q = MahonyStep(q, g - bias, a, dt, kp);
                    }
                    if (haveQ) Interlocked.Exchange(ref ready, 1);
                }
                // 처리한 앞부분을 버리고 남은 조각을 앞으로
                int keep = len - i;
                if (keep > 0 && i > 0) Buffer.BlockCopy(buf, i, buf, 0, keep);
                len = Math.Max(0, keep);
                if (len >= buf.Length) len = 0;   // 표식을 못 찾는 쓰레기로 꽉 찼으면 비운다
            }
        }

        // ── 순수 함수(자체검사 대상) ─────────────────────────────────────────────────
        /// <summary>buf[off] 가 메시지 시작이면 자이로(rad/s)·가속도(m/s²)를 꺼낸다. 센서 메시지가 아니면 false.</summary>
        public static bool TryParse(byte[] buf, int off, out NVec gyro, out NVec accel)
        {
            gyro = accel = default;
            if (off < 0 || off + MsgLen > buf.Length) return false;
            for (int k = 0; k < SensorMark.Length; k++) if (buf[off + MarkAt + k] != SensorMark[k]) return false;
            gyro = new NVec(F(buf, off, 0), F(buf, off, 1), F(buf, off, 2));
            accel = new NVec(F(buf, off, 3), F(buf, off, 4), F(buf, off, 5));
            return true;
        }

        static float F(byte[] b, int off, int k) => BitConverter.ToSingle(b, off + FloatsAt + 4 * k);   // 리틀엔디언(맥·PC·안드로이드 공통)

        static int IndexOf(byte[] b, byte[] pat, int from, int len)
        {
            for (int i = Math.Max(0, from); i <= len - pat.Length; i++)
            {
                int k = 0;
                while (k < pat.Length && b[i + k] == pat[k]) k++;
                if (k == pat.Length) return i;
            }
            return -1;
        }

        /// <summary>가속도(=몸 좌표의 세계 위쪽)를 세계 Z 로 돌리는 자세 — 필터를 첫 샘플부터 수렴한 상태로 시작한다.</summary>
        static NQuat FromUp(NVec accel)
        {
            var a = NVec.Normalize(accel); var z = NVec.UnitZ;
            float d = NVec.Dot(a, z);
            if (d > 0.9999f) return NQuat.Identity;
            if (d < -0.9999f) return NQuat.CreateFromAxisAngle(NVec.UnitX, Mathf.PI);
            return NQuat.CreateFromAxisAngle(NVec.Normalize(NVec.Cross(a, z)), Mathf.Acos(d));
        }

        /// <summary>Mahony 6축 — 자이로를 적분하고, 잰 중력(가속도)과 추정 중력의 어긋남으로 기울기를 당긴다.
        ///   q 는 몸→세계. 추정 중력 v = q⁻¹·Z(세계 위를 몸 좌표로). 오차 e = a×v.</summary>
        public static NQuat MahonyStep(NQuat q, NVec gyro, NVec accel, float dt, float kp)
        {
            float an = accel.Length();
            if (an > 1e-3f)
            {
                var a = accel / an;
                var v = NVec.Transform(NVec.UnitZ, NQuat.Conjugate(q));
                gyro += kp * NVec.Cross(a, v);
            }
            var dq = q * new NQuat(gyro.X, gyro.Y, gyro.Z, 0f);
            q = new NQuat(q.X + 0.5f * dt * dq.X, q.Y + 0.5f * dt * dq.Y, q.Z + 0.5f * dt * dq.Z, q.W + 0.5f * dt * dq.W);
            return NQuat.Normalize(q);
        }

        /// <summary>안경 앞(+Z)이 세계에서 가리키는 방위각(도, 반시계 +)과 올려본 각(도, 위 +).</summary>
        public static void HeadAngles(NQuat q, out float yawDeg, out float pitchDeg)
        {
            var f = NVec.Transform(NVec.UnitZ, q);
            yawDeg = Mathf.Atan2(f.Y, f.X) * Mathf.Rad2Deg;
            pitchDeg = Mathf.Asin(Mathf.Clamp(f.Z, -1f, 1f)) * Mathf.Rad2Deg;
        }

        /// <summary>자체검사 — ① 합성 메시지 해석 ② 오른쪽 90° ③ 고개 들기 30°. 실측한 축 약속(X 오른쪽·Y 아래·Z 앞)을 그대로 넣는다.</summary>
        public static bool SelfCheck(out string why)
        {
            var m = new byte[MsgLen];
            Array.Copy(Header, m, Header.Length);
            Array.Copy(SensorMark, 0, m, MarkAt, SensorMark.Length);
            for (int k = 0; k < 6; k++) Array.Copy(BitConverter.GetBytes(k + 1f), 0, m, FloatsAt + 4 * k, 4);
            if (!TryParse(m, 0, out var pg, out var pa) || pg != new NVec(1, 2, 3) || pa != new NVec(4, 5, 6))
            { why = "메시지 해석 실패"; return false; }

            var up = new NVec(0f, -9.8f, 0f);           // 똑바로 섰을 때 중력(위)은 −Y
            // 오른쪽 90°: +Y(아래축) 둘레로 1초에 π/2 — 중력은 안 변한다.
            var q = FromUp(up); HeadAngles(q, out float y0, out _);
            for (int i = 0; i < 1000; i++) q = MahonyStep(q, new NVec(0f, Mathf.PI / 2f, 0f), up, 0.001f, 1.5f);
            HeadAngles(q, out float y1, out float p1);
            float right = -Mathf.DeltaAngle(y0, y1);
            if (Mathf.Abs(right - 90f) > 3f || Mathf.Abs(p1) > 3f) { why = $"오른쪽 90° → {right:0.0}° (위아래 {p1:0.0}°)"; return false; }

            // 고개 들기 30°: +X 둘레로 1초에 π/6 — 중력은 몸 좌표에서 (0, −cosθ, +sinθ) 로 옮겨 간다(실측과 같은 방향).
            q = FromUp(up); HeadAngles(q, out _, out float pA);
            for (int i = 1; i <= 1000; i++)
            {
                float th = Mathf.PI / 6f * i / 1000f;
                q = MahonyStep(q, new NVec(Mathf.PI / 6f, 0f, 0f), new NVec(0f, -9.8f * Mathf.Cos(th), 9.8f * Mathf.Sin(th)), 0.001f, 1.5f);
            }
            HeadAngles(q, out _, out float pB);
            if (Mathf.Abs((pB - pA) - 30f) > 3f) { why = $"고개 들기 30° → {pB - pA:0.0}°"; return false; }

            why = $"해석·오른쪽 {right:0.0}°·위 {pB - pA:0.0}° 통과";
            return true;
        }
    }
}
