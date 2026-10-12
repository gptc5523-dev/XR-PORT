using UnityEngine;

namespace AIXRCrane.Crane.Sts
{
    public sealed partial class SpreaderGrabber
    {
        // 클램프·푸셔는 축 이동(VRController)과 같은 FixedUpdate 박자 — 프레임 의존·터널링 방지.
        void FixedUpdate()
        {
            if (Time.time >= nextRefresh) Refresh();
            IsLanded = false;

            UpdatePusherBox();

            var attach = crane != null ? crane.Attach : null;
            // Anti-Collision 은 접점(NotifyContact) 방식 — OverlapBox 는 일찍 뜨고 바닥을 놓쳐 폐기.
            LoadCollision = Time.time - lastAntiCollTime <= loadCollisionHold;
            // QA S-PASS-5: 경보 상태 변화만.
            if (LoadCollision != qaPrevLoadColl)
            {
                QaLog.Info("SIDE", "anticoll",
                    $"hold={QaLog.F(loadCollisionHold)} LoadCollision={LoadCollision}");
                qaPrevLoadColl = LoadCollision;
            }

            // HUD '잠금' 라인용 안착 정렬 상태 — 잡기와 같은 기준(FindNearest). 잡고 있으면 둘 다 false.
            ReadyToLock = false; NearButUnseated = false;
            if (requireSeatedRegistration && attach != null && !attach.HasContainer
                && twistlocks != null && twistlocks.Length > 0)
            {
                Vector3 gp = GrabPoint();
                var cand = FindNearest(gp, out _);
                if (cand != null)
                {
                    bool seated = IsSeatedOver(cand, gp, out _, out _);
                    ReadyToLock = seated;
                    NearButUnseated = !seated;
                }
            }

            if (!blockPassThrough || crane == null) return;

            var hoist = crane.Spreader;
            Transform ap = AttachPoint;
            if (attach == null || hoist == null || ap == null) return;

            // 하한 기준면·판정 중심: 들고 있으면 컨테이너 밑면·중심, 비었으면 콘 바닥·부착점.
            float refBottomY;
            Vector3 refCenter;
            Bounds heldB = default;
            bool holding = attach.HasContainer;
            if (holding)
            {
                if (!SceneUtil.TryBounds(attach.AttachedContainer, out heldB)) return;
                refBottomY = heldB.min.y;
                refCenter = heldB.center;
            }
            else
            {
                // 부착점 기준이면 RTG 는 콘이 300mm 넘게 잠기고 STS 는 체결 자세가 안 나온다 → 콘 바닥 실측.
                refBottomY = ConeBottomY();
                refCenter = ap.position;   // 수평 판정은 부착점 XZ
            }

            // 받침 선정: 든 상태는 footprint 겹침 ≥ landingOverlapFrac, 빈 스프레더는 부착점이 footprint(±overMargin) 안.
            float overMargin = Mathf.Min(passXZmargin, EmptyPassMarginCap);
            float top = float.MinValue;
            float topMinY = 0f;   // 받침 밑면 — 측면 침투 깊이 판정용
            bool over = false;
            float selOverlap = 0f;        // 받침 겹침 비율(QA)
            float maxOverlapSeen = 0f;    // 본 최대 겹침(QA '옆 나란히' 근거)
            for (int i = 0; i < bodies.Count; i++)
            {
                var rb = bodies[i];
                if (rb == null) continue;
                if (rb.transform.IsChildOf(transform)) continue;   // 크레인 자식 제외
                if (!BodyBounds(i, out Bounds b)) continue;

                bool below;
                float overlapFrac = 0f;
                if (holding)
                {
                    float ox = Mathf.Min(heldB.max.x, b.max.x) - Mathf.Max(heldB.min.x, b.min.x);
                    float oz = Mathf.Min(heldB.max.z, b.max.z) - Mathf.Max(heldB.min.z, b.min.z);
                    float heldArea = heldB.size.x * heldB.size.z;
                    overlapFrac = (ox > 0f && oz > 0f) ? (ox * oz) / Mathf.Max(heldArea, 1e-6f) : 0f;
                    if (overlapFrac > maxOverlapSeen) maxOverlapSeen = overlapFrac;
                    below = overlapFrac >= landingOverlapFrac;
                }
                else
                {
                    below = refCenter.x >= b.min.x - overMargin && refCenter.x <= b.max.x + overMargin
                         && refCenter.z >= b.min.z - overMargin && refCenter.z <= b.max.z + overMargin;
                }
                if (!below) continue;
                if (b.max.y > top) { top = b.max.y; topMinY = b.min.y; over = true; selOverlap = overlapFrac; }
            }

            // 받침 없으면 바닥이 받침. 빈 스프레더는 컨테이너에 InsertU 만큼 박히게 내려간다.
            //   ★ 바닥에서는 삽입을 빼지 않는다 — 코너캐스팅 구멍은 컨테이너에만 있다.
            bool onFloor = !over;
            if (onFloor) top = floorTopY;
            float limit = top + topClearance - (holding || onFloor ? 0f : InsertU);
            // 옆면 깊숙이(높이 25% 이상 아래) 들어오면 측면 충돌 → 클램프 대신 푸셔로 민다. 바닥은 제외.
            bool sideHit = !onFloor && !holding && (top - refBottomY) > (top - topMinY) * SideHitDepthFrac;
            float corr = 0f;
            PassThrough = refBottomY < limit && !sideHit && hoist is AxisMoverBase hm && hm.FollowOnly;
            if (refBottomY < limit && !sideHit && !PassThrough)
            {
                corr = limit - refBottomY;
                hoist.MoveTo(hoist.Current + corr);   // 받침 윗면에서 하강 정지
            }
            IsLanded = !sideHit && refBottomY <= limit + topClearance;   // Landing 센서

            QaPassState(holding, over, sideHit, corr, limit, top, topMinY, refBottomY, selOverlap, maxOverlapSeen);
        }

        // QA 판정(그룹 B·C) — 엣지와 보정량 변화만 찍는다.
        void QaPassState(bool holding, bool over, bool sideHit, float corr,
                         float limit, float top, float topMinY, float refBottomY, float selOverlap, float maxOverlapSeen)
        {
            // S-PASS-2 측면충돌: sideHit 상승 엣지.
            if (sideHit && !qaPrevSideHit)
            {
                float depth = top - refBottomY;
                float threshold = (top - topMinY) * SideHitDepthFrac;   // ≈ 컨테이너높이×0.25
                QaLog.Check("SIDE", "hit", depth > threshold,
                    $"holding={holding} depth={QaLog.F(depth)} threshold={QaLog.F(threshold)} sideHit=true clampApplied=false");
            }

            // S-PASS-3 적층 안착: IsLanded 상승 엣지에서만. ★ 바닥 안착은 겹침 0 이 정상이라 제외.
            bool landedStack = holding && IsLanded && top > floorTopY + 1e-4f;
            if (landedStack && !qaPrevLanded)
                QaLog.Check("LAND", "stack", selOverlap >= landingOverlapFrac,
                    $"holding=true overlapFrac={QaLog.F(selOverlap)} threshold={QaLog.F(landingOverlapFrac)} " +
                    $"top={QaLog.F(top)} refBottomY={QaLog.F(refBottomY)} landed=true");

            // S-PASS-1 통과방지: 윗면 근처 클램프 상승 엣지만. FAIL = 아래인데 corr≈0(관통).
            bool clampEmpty = over && !holding && !sideHit && refBottomY <= limit + 0.012f && refBottomY > limit - 0.06f;
            if (clampEmpty && !qaPrevClampEmpty)
                QaLog.Check("PASS", "clamp", corr > 1e-4f || refBottomY >= limit - 0.012f,
                    $"holding=false refBottomY={QaLog.F(refBottomY)} top={QaLog.F(top)} limit={QaLog.F(limit)} corr={QaLog.F(corr)}");

            // S-PASS-4 옆 나란히(적층 오인 금지): 든 채 over가 풀린 순간.
            if (holding && !over && qaPrevOver)
                QaLog.Info("LAND", "side",
                    $"holding=true over=false maxOverlap={QaLog.F(maxOverlapSeen)} threshold={QaLog.F(landingOverlapFrac)} below=false");

            // S-PHYS-2 클램프 보정량 수렴: corr가 변할 때만.
            if (corr > 0f && (qaCorrPrev < 0f || Mathf.Abs(corr - qaCorrPrev) > 1e-4f))
            {
                float prev = Mathf.Max(qaCorrPrev, 0f);
                QaLog.Info("PASS", "order",
                    $"moveOrder=0 clampOrder=50 corr={QaLog.F(corr)} corrPrev={QaLog.F(prev)} dCorr={QaLog.F(corr - prev)}");
            }
            qaCorrPrev = corr;
            qaPrevOver = over;
            qaPrevSideHit = sideHit;
            qaPrevLanded = landedStack;
            qaPrevClampEmpty = clampEmpty;
        }

        // 푸셔 박스 X = 2×CurrentHalf — 스프레더가 줄면 박스도 줄어 옆 컨테이너를 안 민다.
        void UpdatePusherBox()
        {
            if (pusherBox == null || telescope == null) return;
            float want = Mathf.Max(telescope.CurrentHalf * 2f, 0.001f);
            var sz = pusherBox.size;
            if (!Mathf.Approximately(sz.x, want)) { sz.x = want; pusherBox.size = sz; }
        }

        // 스프레더에 kinematic 푸셔 콜라이더를 단다 — 별도 자식(강체 중첩 방지), 바닥은 부착점까지(콘 비움).
        void CreateSpreaderPusher()
        {
            if (!spreaderCollider) return;
            var sp = (crane != null ? crane.Spreader as Component : null)?.transform;
            if (sp == null || sp.Find("SpreaderPusher") != null) return;
            if (!TryLocalBounds(sp, out Bounds lb)) return;

            var go = new GameObject("SpreaderPusher");
            go.transform.SetParent(sp, worldPositionStays: false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;   // 이동 빠를 때 관통 줄임

            // 박스: X=2×CurrentHalf, Z=스프레더 폭, Y=프레임 윗면~그랩 평면(콘 비움), 중심 XZ=0.
            float grabPlaneY = AttachPoint != null ? sp.InverseTransformPoint(AttachPoint.position).y : lb.min.y;
            float topY = lb.max.y;
            float botY = Mathf.Min(grabPlaneY, topY - 0.001f);
            float lenX = telescope != null ? telescope.CurrentHalf * 2f : lb.size.x;
            var box = go.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, (topY + botY) * 0.5f, 0f);
            box.size   = new Vector3(Mathf.Max(lenX, 0.001f), Mathf.Max(topY - botY, 0.001f), Mathf.Max(lb.size.z, 0.001f));
            // 컨테이너 contactOffset(0.001)과 맞춤 — 기본 0.01이면 Anti-Collision skin이 못 닿는다.
            box.contactOffset = 0.001f;
            pusherBox = box;

            go.AddComponent<LoadCollisionRelay>().owner = this;   // 빈 스프레더 접점 충돌 보고(옆/아래)

            if (debugLog) Debug.Log($"[Crane] SpreaderPusher 콜라이더 생성 — size {box.size:F3}, center {box.center:F3}");
        }

        // 자식 렌더러들의 스프레더 로컬 AABB(월드 AABB 8꼭짓점 변환).
        static bool TryLocalBounds(Transform root, out Bounds local)
        {
            local = default;
            var rends = root.GetComponentsInChildren<Renderer>();
            if (rends == null || rends.Length == 0) return false;
            bool has = false;
            foreach (var r in rends)
            {
                var wb = r.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = wb.center + Vector3.Scale(wb.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 lc = root.InverseTransformPoint(c);
                    if (!has) { local = new Bounds(lc, Vector3.zero); has = true; }
                    else local.Encapsulate(lc);
                }
            }
            return has;
        }
    }
}
