using System.Collections.Generic;
using UnityEngine;

namespace AIXRCrane.Crane.Sts
{
    public sealed partial class CraneDemoRunner
    {
        void Fail(string what)
        {
            if (++Violations <= 40) Debug.LogWarning($"[PortDemo] 검증 실패 — {name}: {what}");
        }

        // ④ 이번 틱 실제 속도·가속(실척) — 한계를 넘으면 그 틱에서 실패
        void Kin(IAxisMover a, ref float prev, ref float vPrev, float vMax, float acc, float dt, string axis)
        {
            if (a == null || dt <= 0f) return;
            float toM = a.MetersPerUnit();
            float cur = a.Current, v = (cur - prev) * toM / dt, am = (v - vPrev) / dt;
            float sr = Mathf.Abs(v) / (vMax * speedScale), ar = Mathf.Abs(am) / (acc * speedScale * CraneAxisProfile.TripMargin);
            MaxSpeedRatio = Mathf.Max(MaxSpeedRatio, sr);
            MaxAccelRatio = Mathf.Max(MaxAccelRatio, ar);
            if (sr > 1.001f || ar > AccelTripRatioMax)
                Fail($"{axis} 한 틱 v {vPrev:F3}→{v:F3}m/s(정격비 {sr:F3}) a {am:F2}m/s²(트립비 {ar:F2} > {AccelTripRatioMax:F2}), 축 {prev:F6}→{cur:F6}");
            prev = cur; vPrev = v;
        }

        // ① 집기 직전 — 트위스트락 중심·콘 바닥을 러너 식과 따로 재서 컨테이너 윗면에 맞춘다.
        void CheckPick(Transform box)
        {
            if (grabber == null || !SceneUtil.TryBounds(box, out var b)) return;
            Vector3 gp = grabber.GrabPoint();
            float dxz = new Vector2(gp.x - b.center.x, gp.z - b.center.z).magnitude / StsConfig.ModelScale;
            float gap = (SpreaderBottomY() - b.max.y) / StsConfig.ModelScale;
            MaxPickErrM = Mathf.Max(MaxPickErrM, dxz);
            if (dxz > PickTolM || Mathf.Abs(gap - SeatGapM) > SeatTolM)
                Fail($"집기 정렬 {box.name} — 트위스트락↔윗면 중심 {dxz:F3}m(≤{PickTolM}), 콘 바닥−윗면 {gap:F3}m(목표 {SeatGapM:F3}±{SeatTolM})");
        }

        // ②③ 놓은 직후 — 옮긴 자리는 받침(땅·발밑 컨테이너 윗면)에 닿았나, 어디서든 다른 컨테이너를 파고들지 않았나.
        //   되돌린 자리는 원래 자세(home)라 받침이 배 해치·아래 단이다 — 겹침만 본다.
        void CheckPlaced(Transform c, bool toAway)
        {
            if (!SceneUtil.TryBounds(c, out var b)) return;
            var others = OtherBounds(c);
            if (toAway)
            {
                float support = site != null && site.haveLand ? site.land.max.y : 0f;
                float reach = b.min.y + SupportTolM * StsConfig.ModelScale;
                foreach (var o in others)
                    if (o.max.y <= reach && o.min.x < b.max.x && o.max.x > b.min.x && o.min.z < b.max.z && o.max.z > b.min.z)
                        support = Mathf.Max(support, o.max.y);
                float err = Mathf.Abs(b.min.y - support) / StsConfig.ModelScale;
                MaxSupportErrM = Mathf.Max(MaxSupportErrM, err);
                if (err > SupportTolM) Fail($"안착 {c.name} — 밑면 y={b.min.y:F4}u, 받침 y={support:F4}u, 오차 {err:F3}m(≤{SupportTolM})");
            }
            if (AnyOverlap(b, others, OverlapSkinM * StsConfig.ModelScale)) Fail($"놓은 자리 {c.name} 이(가) 다른 컨테이너를 파고듦");
        }

        void CheckClear(Bounds path, Transform self, string what)
        {
            if (AnyOverlap(path, OtherBounds(self), OverlapSkinM * StsConfig.ModelScale)) Fail($"{what} {self.name} 이(가) 다른 컨테이너를 파고듦");
        }

        List<Bounds> OtherBounds(Transform self)
        {
            var list = new List<Bounds>();
            if (site == null) return list;
            foreach (var t in site.ship) if (t != null && t != self && SceneUtil.TryBounds(t, out var ob)) list.Add(ob);
            foreach (var t in site.yard) if (t != null && t != self && SceneUtil.TryBounds(t, out var ob)) list.Add(ob);
            return list;
        }
    }
}
