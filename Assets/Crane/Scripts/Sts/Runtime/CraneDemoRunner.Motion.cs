using System.Collections;
using UnityEngine;

namespace AIXRCrane.Crane.Sts
{
    public sealed partial class CraneDemoRunner
    {
        IEnumerator Move(Job j, Vector3 dest, bool toAway)
        {
            var attach = crane.Attach;
            result = Result.Abort;
            if (attach.HasContainer && attach.AttachedContainer != j.box)
            {
                // 조종하던 사람이 딴 컨테이너를 매단 채 떠났거나 앞 작업이 막혔다 — 그 자리에 놓고 시작
                ShowColliders();
                if (grabber != null) grabber.Release(); else attach.Detach();
            }
            if (!attach.HasContainer)
            {
                if (!SceneUtil.TryBounds(j.box, out var b)) { result = Result.Skip; yield break; }
                if ((new Vector3(b.center.x, b.min.y, b.center.z) - dest).sqrMagnitude < Lift * Lift) { result = Result.Done; yield break; }   // 이미 거기
                Vector3 seat = Seat(b);
                if (!Reach(seat))
                {
                    Debug.LogWarning($"[PortDemo] {name}: {j.box.name} 이(가) 닿지 않는 자리에 있어 건너뜀");
                    result = Result.Skip;
                    yield break;
                }
                yield return Hoist(clearTopY + drop);   if (Cut()) yield break;
                yield return Travel(seat);              if (Cut()) yield break;
                yield return Align(seat);               if (Cut()) yield break;   // 흔들림을 잡고 내린다 — 이웃을 치지 않게
                yield return Hoist(seat.y);             if (Cut()) yield break;
                yield return Align(seat);               if (Cut()) yield break;
                Pick(j);
            }
            float hang = drop + SeatGapU + j.size.y;   // 부착점 → 든 컨테이너 밑면(집은 자세 그대로 매달림)
            yield return Hoist(clearTopY + hang);       if (Cut()) yield break;
            bool haveFrom = SceneUtil.TryBounds(j.box, out var path);
            yield return Travel(dest);                  if (Cut()) yield break;
            if (haveFrom && SceneUtil.TryBounds(j.box, out var to)) { path.Encapsulate(to); CheckClear(path, j.box, "운반 경로"); }
            yield return Align(dest);                   if (Cut()) yield break;
            yield return Hoist(dest.y + Lift + hang);   if (Cut()) yield break;
            yield return Align(dest);                   if (Cut()) yield break;   // 내리며 로프가 길어진 만큼 바람 편향(∝L)도 커졌다
            Place(j, dest, toAway);
            result = Result.Done;
        }

        // 흔들림(CraneSway) 대응 — Settle: 진동 진폭이 SettleM 아래로 가라앉을 때까지 기다림(못 가라앉으면 SettleTimeoutS 뒤 진행).
        //   Align: 가라앉은 뒤 실제 부착점(바람 편향 포함)을 목표에 수평으로 맞춘다 — 편향 d = L·F/(m·g) 만큼 비켜 댄다.
        const float SettleM = 0.1f, SettleTimeoutS = 30f;

        IEnumerator Settle()
        {
            var s = CraneSway.Of(crane);
            float waited = 0f;
            while (s != null && s.isActiveAndEnabled && !s.Settled(SettleM) && waited < SettleTimeoutS)
            {
                if (PortDemoDirector.Holds(crane)) { interrupted = true; yield break; }
                waited += Time.fixedDeltaTime;
                yield return new WaitForFixedUpdate();
            }
        }

        IEnumerator Align(Vector3 p)
        {
            yield return Settle();                      if (Cut()) yield break;
            Vector3 d = p - crane.Attach.AttachAnchor.position; d.y = 0f;
            if (d.magnitude / StsConfig.ModelScale < SettleM) yield break;
            yield return Drive(crane.Gantry != null ? AxisFor(crane.Gantry, d) : float.NaN, AxisFor(crane.Trolley, d), float.NaN);
            if (Cut()) yield break;
            yield return Settle();
        }

        // 이 단계에서 끊어야 하나 — 멈춤이면 Abort(다시 시도), 막힘이면 Skip(다음 작업).
        //   interrupted: 기다리던 단계가 멈춤으로 빠졌다 — 그 사이 사람이 떠나 Holds 가 풀렸어도 목표에 못 간 채라 처음부터 다시 한다.
        bool interrupted;
        bool Cut()
        {
            if (interrupted || PortDemoDirector.Holds(crane)) { interrupted = false; result = Result.Abort; return true; }
            if (stalled) { result = Result.Skip; return true; }
            return false;
        }

        IEnumerator Hoist(float anchorY) =>
            Drive(float.NaN, float.NaN, AxisFor(crane.Spreader, Vector3.up * (anchorY - Anchor().y)));

        IEnumerator Travel(Vector3 p)
        {
            stalled = false;
            if (steer != null && steer.Current != RtgBogieSteering.Mode.Travel) steer.SetMode(RtgBogieSteering.Mode.Travel);
            while (steer != null && steer.IsTurning)   // 보기를 꺾는 동안은 GantryMover 가 주행을 잠근다
            {
                if (PortDemoDirector.Holds(crane)) { interrupted = true; yield break; }
                yield return null;
            }
            Vector3 d = p - Anchor(); d.y = 0f;
            yield return Drive(crane.Gantry != null ? AxisFor(crane.Gantry, d) : float.NaN, AxisFor(crane.Trolley, d), float.NaN);
        }

        // 세 축을 목표로 — 남은 거리가 StallSeconds 동안 안 줄면 stalled('안 움직임'이 아니라 '안 다가감').
        //   멈춤 시 그 틱에 순정지하지 않고 정지거리 s=v²/(2a) 앞을 새 목표로 둔다 — 감속이 튀어 화물이 흔들리지 않게.
        IEnumerator Drive(float g, float t, float h)
        {
            stalled = false;
            vG = vT = vH = 0f;
            float best = float.MaxValue, since = 0f;
            bool stopping = false;
            // 검증 ④ — 축 값을 틱마다 다시 읽어 속도·가속을 잰다(Step 이 믿는 v 가 아니라 실제로 옮겨진 양).
            float pG = Cur(crane.Gantry), pT = Cur(crane.Trolley), pH = Cur(crane.Spreader), mG = 0f, mT = 0f, mH = 0f;
            while (true)
            {
                if (!stopping && PortDemoDirector.Holds(crane))
                {
                    stopping = interrupted = true;
                    g = StopAt(crane.Gantry, g, vG, CraneAxisProfile.GantryRatedAccel);
                    t = StopAt(crane.Trolley, t, vT, CraneAxisProfile.TrolleyRatedAccel);
                    h = StopAt(crane.Spreader, h, vH, CraneAxisProfile.HoistRatedAccel);
                }
                float dt = Time.fixedDeltaTime, left = 0f;
                bool done = Step(crane.Gantry, g, CraneAxisProfile.GantryMaxSpeed, CraneAxisProfile.GantryRatedAccel, ref vG, dt, ref left)
                          & Step(crane.Trolley, t, CraneAxisProfile.TrolleyMaxSpeed, CraneAxisProfile.TrolleyRatedAccel, ref vT, dt, ref left)
                          & Step(crane.Spreader, h, CraneAxisProfile.HoistMaxSpeed, CraneAxisProfile.HoistRatedAccel, ref vH, dt, ref left);
                Kin(crane.Gantry, ref pG, ref mG, CraneAxisProfile.GantryMaxSpeed, CraneAxisProfile.GantryRatedAccel, dt, "갠트리");
                Kin(crane.Trolley, ref pT, ref mT, CraneAxisProfile.TrolleyMaxSpeed, CraneAxisProfile.TrolleyRatedAccel, dt, "트롤리");
                Kin(crane.Spreader, ref pH, ref mH, CraneAxisProfile.HoistMaxSpeed, CraneAxisProfile.HoistRatedAccel, dt, "권상");
                if (done) yield break;
                if (stopping) { yield return new WaitForFixedUpdate(); continue; }
                if (left < best - 0.001f) { best = left; since = 0f; }
                else if ((since += dt) > StallSeconds)
                {
                    stalled = true;
                    Debug.LogWarning($"[PortDemo] {name}: {StallSeconds:F0}초 넘게 목표에 못 다가감(남은 {left:F2}m — {Why(g, t, h)}) — 막힘, 이번 작업 건너뜀");
                    yield break;
                }
                yield return new WaitForFixedUpdate();
            }
        }

        // 막힘 진단 — 축마다 남은 거리(실척 m)와 무버의 장애물 정지(IsBlocked) 여부, 흔들림 진폭
        string Why(float g, float t, float h)
        {
            var s = CraneSway.Of(crane);
            return $"갠트리 {ErrM(crane.Gantry, g):F3}{Blk(crane.Gantry)} · 트롤리 {ErrM(crane.Trolley, t):F3}{Blk(crane.Trolley)} · " +
                   $"권상 {ErrM(crane.Spreader, h):F3}{Blk(crane.Spreader)} · 흔들림 {(s != null ? s.AmplitudeM : 0f):F2}m" +
                   $"{(steer != null && steer.IsTurning ? " · 보기 조향 중" : "")}";
        }
        static float ErrM(IAxisMover a, float target) =>
            a == null || float.IsNaN(target) ? 0f : (Mathf.Clamp(target, a.Min, a.Max) - a.Current) * a.MetersPerUnit();
        static string Blk(IAxisMover a) => a is AxisMoverBase m && m.IsBlocked ? "(장애물 정지)" : "";

        // 한 축 한 틱 — 정격 가속으로 붙고, 멈출 거리를 남겨 감속한다(사다리꼴). 목표에 닿았으면 true.
        bool Step(IAxisMover a, float target, float vMax, float acc, ref float v, float dt, ref float left)
        {
            if (a == null || float.IsNaN(target)) return true;
            float toM = a.MetersPerUnit();
            if (toM <= 0f) return true;
            float err = (Mathf.Clamp(target, a.Min, a.Max) - a.Current) * toM;
            left += Mathf.Abs(err);
            vMax *= speedScale; acc *= speedScale;
            // 도착 = 1mm 안 '그리고' |v| ≤ a·dt(PlcSim 정착 규칙과 같음) — 위치만 보면 마지막 감속이 튄다.
            if (Mathf.Abs(err) < 0.001f && Mathf.Abs(v) <= acc * dt) { v = 0f; return true; }
            float speed = Mathf.Min(Mathf.Min(Mathf.Abs(v) + acc * dt, vMax), Mathf.Sqrt(2f * acc * Mathf.Abs(err)));
            float move = Mathf.Min(speed * dt, Mathf.Abs(err));
            v = Mathf.Sign(err) * move / dt;
            a.MoveTo(a.Current + Mathf.Sign(err) * move / toM);
            return false;
        }

        // ── 검증 헬퍼(위 ①~④) ──
        static float Cur(IAxisMover a) => a != null ? a.Current : 0f;

        /// <summary>축의 지금 위치를 PLC 좌표(실척 m, 0 = 무버 Min)로 — PlcSim 시작 자세를 씬과 맞출 때 쓰는 측정값.
        ///   RTG 는 무버 위치가 FBX 기본값이라 씬 파일에 안 남는다(프리팹 오버라이드 없음) → 로그로만 잴 수 있다.</summary>
        static float PlcM(IAxisMover a) => a == null ? 0f : a.PlcMeters();

        // 지금 속도 v(실척 m/s — Step 이 실제로 옮긴 양)로 정격 감속해 서는 축 값. 서 있으면 NaN(안 움직임).
        float StopAt(IAxisMover a, float target, float v, float acc)
        {
            if (a == null || float.IsNaN(target) || Mathf.Abs(v) < 1e-4f) return float.NaN;
            float toM = a.MetersPerUnit();
            return a.Current + Mathf.Sign(v) * v * v / (2f * acc * speedScale) / toM;
        }

        // 부착점을 p 로 보내는 세 축 값이 모두 가동범위 안인가
        bool Reach(Vector3 p)
        {
            Vector3 d = p - Anchor();
            return InRange(crane.Trolley, d) && InRange(crane.Spreader, d) && (crane.Gantry == null || InRange(crane.Gantry, d));
        }

        // 트롤리 가동범위의 월드 양 끝(트롤리 방향 좌표) — 지금 부착점 기준
        void TrolleyReach(out float lo, out float hi)
        {
            var a = crane.Trolley;
            float s = Vector3.Dot(Anchor(), TrolleyDir());
            float p = s + (a.Min - a.Current) * a.WorldPerUnit, q = s + (a.Max - a.Current) * a.WorldPerUnit;
            lo = Mathf.Min(p, q); hi = Mathf.Max(p, q);
        }

        // 집을 때 부착점이 갈 곳 — 윗면 중심에서 콘 바닥이 SeatGap 만큼(= 삽입깊이만큼 아래로) 오게
        Vector3 Seat(Bounds b) => new Vector3(b.center.x, b.max.y + SeatGapU + drop, b.center.z);

        // 흔들림을 뺀 부착점 — 계획·주행 목표는 흔들리지 않은 크레인 기하로 잡는다(실제 화물 맞춤은 Align 이 한다).
        Vector3 Anchor()
        {
            var s = CraneSway.Of(crane);
            return crane.Attach.AttachAnchor.position - (s != null ? s.Offset : Vector3.zero);
        }
        Vector3 GantryDir() => crane.Gantry != null ? crane.Gantry.WorldAxis.normalized : Vector3.forward;
        Vector3 TrolleyDir() => crane.Trolley.WorldAxis.normalized;

        // 스프레더 최저점(콘 바닥) — 매단 컨테이너는 뺀다
        float SpreaderBottomY()
        {
            var sp = ((Component)crane.Spreader).transform;
            var held = crane.Attach.AttachedContainer;
            float y = sp.position.y;
            foreach (var r in sp.GetComponentsInChildren<Renderer>())
                if (held == null || !r.transform.IsChildOf(held)) y = Mathf.Min(y, r.bounds.min.y);
            return y;
        }

        // 부착점을 d 만큼 옮기는 축 값 — 축은 전부 평행이동이라 WorldAxis 성분만 본다(PlcBridge.WorldAtPose 와 같은 식)
        static float AxisFor(IAxisMover a, Vector3 d)
        {
            Vector3 w = a.WorldAxis;
            return a.Current + Vector3.Dot(d, w) / w.sqrMagnitude;
        }

        static bool InRange(IAxisMover a, Vector3 d)
        {
            float v = AxisFor(a, d), eps = (a.Max - a.Min) * 1e-3f;
            return v >= a.Min - eps && v <= a.Max + eps;
        }

        static float Extent(Vector3 size, Vector3 dir) =>
            Mathf.Abs(dir.x) * size.x + Mathf.Abs(dir.y) * size.y + Mathf.Abs(dir.z) * size.z;

        static Vector3 MoverPos(StsCrane c) => c.Gantry is Component g ? g.transform.position : c.transform.position;

        static float HDist(Vector3 a, Vector3 b) { a.y = b.y = 0f; return Vector3.Distance(a, b); }
    }
}
