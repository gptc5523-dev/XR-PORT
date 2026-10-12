using System.Collections.Generic;
using UnityEngine;

namespace AIXRCrane.Crane.Sts
{
    public sealed partial class CraneDemoRunner
    {
        // STS: 위가 빈 배 컨테이너 — 가까운 베이(주행 위치)부터, 한 베이 안에선 땅 쪽 열부터(트롤리를 덜 움직이게).
        List<Transform> ShipCandidates(Site site)
        {
            Vector3 gd = GantryDir(), td = TrolleyDir(), a = Anchor();
            float l0 = Vector3.Dot(site.land.min, td), l1 = Vector3.Dot(site.land.max, td);
            var list = new List<(Transform t, int bay, float toLand)>();
            foreach (var t in site.ship)
            {
                if (site.claimed.Contains(t) || !SceneUtil.TryBounds(t, out var b) || !Uncovered(b, site.occupied) || !Reach(Seat(b))) continue;
                int bay = Mathf.RoundToInt(Mathf.Abs(Vector3.Dot(b.center - a, gd)) / Mathf.Max(Extent(b.size, gd), 1e-5f));
                float c = Vector3.Dot(b.center, td);
                list.Add((t, bay, Mathf.Abs(c - Mathf.Clamp(c, Mathf.Min(l0, l1), Mathf.Max(l0, l1)))));
            }
            list.Sort((x, y) => x.bay != y.bay ? x.bay.CompareTo(y.bay) : x.toLand.CompareTo(y.toLand));
            return list.ConvertAll(x => x.t);
        }

        // RTG: 다른 RTG 보다 나에게 가까운 야드 컨테이너 — 가까운 것부터.
        List<Transform> YardCandidates(Site site)
        {
            Vector3 me = MoverPos(crane);
            var list = new List<(Transform t, float d)>();
            foreach (var t in site.yard)
            {
                if (site.claimed.Contains(t) || !SceneUtil.TryBounds(t, out var b) || !Uncovered(b, site.occupied) || !Reach(Seat(b))) continue;
                float d = HDist(b.center, me);
                bool mine = true;
                foreach (var o in site.rtgs) if (o != crane && HDist(b.center, MoverPos(o)) < d) { mine = false; break; }
                if (mine) list.Add((t, d));
            }
            list.Sort((x, y) => x.d.CompareTo(y.d));
            return list.ConvertAll(x => x.t);
        }

        // 내려놓을 자리 — 같은 주행 위치에서 트롤리 방향으로 가까운 빈자리부터(기준: RTG=집는 자리, STS=두 다리 정중앙).
        //   빈자리 = 땅 안 · 컨테이너·예약 자리와 안 겹침 · 콜라이더 없음 · 세 축으로 닿음.
        bool FindSlot(Site site, Job j, float groundY)
        {
            Vector3 td = TrolleyDir();
            float step = Extent(j.size, td) + slotGapMeters * StsConfig.ModelScale;
            float skin = OverlapSkinM * StsConfig.ModelScale;
            TrolleyReach(out float lo, out float hi);
            float home = Vector3.Dot(j.home, td);
            bool rtg = steer != null;
            float origin = rtg ? home
                : Vector3.Dot(transform.TransformPoint(StsConfig.LegGaugeXMeters * StsConfig.ModelScale * 0.5f, 0f, 0f), td);
            int n = Mathf.CeilToInt((hi - lo) / step) + 1;
            for (int k = rtg ? 1 : 0; k <= n; k++)
                foreach (int sgn in Signs)
                {
                    float s = origin + sgn * k * step;
                    if (s < lo || s > hi) continue;
                    Vector3 bottom = j.home + td * (s - home);
                    bottom.y = groundY;
                    // 야드 안이면 칸(라인) 중심으로 라운딩 — 수동 놓기(Release)와 같은 식(YardGrid). 없으면 자동 시연이 선을 안 지킨다.
                    //   야드 밖은 격자가 없어 자유 스텝. 같은 칸으로 겹치면 site.occupied 게이트가 걸러 이웃 칸으로 넘어간다.
                    if (YardGrid.TrySnapXZ(bottom, Mathf.Max(j.size.x, j.size.z), out var cell))
                    { bottom.x = cell.x; bottom.z = cell.z; }
                    var slot = new Bounds(bottom + Vector3.up * (j.size.y * 0.5f), j.size);
                    if (site.haveLand && !InsideXZ(slot, site.land)) continue;
                    if (AnyOverlap(slot, site.occupied, skin)) continue;
                    if (Physics.CheckBox(slot.center + Vector3.up * (j.size.y * 0.05f), j.size * 0.45f,
                                         Quaternion.identity, ~0, QueryTriggerInteraction.Ignore)) continue;
                    if (!Reach(bottom + Vector3.up * (j.size.y + Lift + drop + SeatGapU))) continue;   // 놓기 직전 부착점
                    site.occupied.Add(slot);
                    j.away = bottom;
                    return true;
                }
            return false;
        }

        static bool IsFt40(Bounds b) => Mathf.Max(b.size.x, b.size.z) > YardGrid.Is40ThresholdU;

        static List<Transform> FortyFirst(List<Transform> cands)
        {
            var forty = cands.FindAll(t => SceneUtil.TryBounds(t, out var b) && IsFt40(b));
            forty.AddRange(cands.FindAll(t => !forty.Contains(t)));
            return forty;
        }

        // RTG — 같은 계열 더미에 한 단 올린다. 더미가 다 찼으면 새 더미 자리를 찾는다.
        bool StackSlot(Site site, Job j)
        {
            var st = stacks.Find(x => x.ft40 == j.ft40 && x.tiers < StackTiers);
            if (st == null)
            {
                if (!FindStackBase(site, j, out var b)) return false;
                st = new Stack { bottom = b, topY = b.y, ft40 = j.ft40 };
                stacks.Add(st);
            }
            var bottom = new Vector3(st.bottom.x, st.topY, st.bottom.z);
            if (!Reach(bottom + Vector3.up * (j.size.y + Lift + drop + SeatGapU))) return false;
            site.occupied.Add(new Bounds(bottom + Vector3.up * (j.size.y * 0.5f), j.size));
            st.topY += j.size.y;
            st.tiers++;
            j.away = bottom;
            return true;
        }

        // 새 더미 자리 — 트롤리 Min 쪽 끝 열부터, 집는 자리 주행 위치에서 가까운 베이부터. 맨 위 단까지 비어 있고 닿아야 한다.
        bool FindStackBase(Site site, Job j, out Vector3 bottom)
        {
            Vector3 td = TrolleyDir(), gd = GantryDir();
            float gap = slotGapMeters * StsConfig.ModelScale, skin = OverlapSkinM * StsConfig.ModelScale;
            float across = Extent(j.size, td), along = Extent(j.size, gd), tall = StackTiers * j.size.y;
            TrolleyReach(out float lo, out float hi);
            float home = Vector3.Dot(j.home, td);
            for (float s = lo + across * 0.5f; s <= hi; s += across + gap)
                for (int g = 0; g <= PortConfig.YardBays; g++)
                    foreach (int sgn in Signs)
                    {
                        if (g == 0 && sgn > 0) continue;
                        bottom = j.home + td * (s - home) + gd * (sgn * g * (along + gap));
                        bottom.y = j.home.y;
                        if (YardGrid.TrySnapXZ(bottom, Mathf.Max(j.size.x, j.size.z), out var cell)) { bottom.x = cell.x; bottom.z = cell.z; }
                        var column = new Bounds(bottom + Vector3.up * (tall * 0.5f), new Vector3(j.size.x, tall, j.size.z));
                        if (site.haveLand && !InsideXZ(column, site.land)) continue;
                        if (AnyOverlap(column, site.occupied, skin)) continue;
                        if (Physics.CheckBox(column.center, column.extents * 0.9f, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore)) continue;
                        if (!Reach(bottom + Vector3.up * (tall + Lift + drop + SeatGapU))) continue;
                        return true;
                    }
            bottom = default;
            return false;
        }

        // 위에 다른 컨테이너가 얹혀 있지 않은가 — 겹침은 폭의 1/4 안쪽만 친다(옆에 붙은 이웃은 제외)
        static bool Uncovered(Bounds b, List<Bounds> all)
        {
            float mx = b.extents.x * 0.5f, mz = b.extents.z * 0.5f;
            foreach (var o in all)
                if (o.min.y > b.center.y && o.min.x < b.max.x - mx && o.max.x > b.min.x + mx
                                         && o.min.z < b.max.z - mz && o.max.z > b.min.z + mz)
                    return false;
            return true;
        }

        static bool AnyOverlap(Bounds a, List<Bounds> all, float skin)
        {
            foreach (var o in all)
                if (a.min.x < o.max.x - skin && a.max.x > o.min.x + skin && a.min.y < o.max.y - skin
                 && a.max.y > o.min.y + skin && a.min.z < o.max.z - skin && a.max.z > o.min.z + skin)
                    return true;
            return false;
        }

        static bool InsideXZ(Bounds a, Bounds land) =>
            a.min.x >= land.min.x && a.max.x <= land.max.x && a.min.z >= land.min.z && a.max.z <= land.max.z;
    }
}
