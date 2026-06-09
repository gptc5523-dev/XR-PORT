using System.Collections.Generic;
using UnityEngine;

namespace Container.Crane.Sts
{
    /// <summary>
    /// 크레인 외부의 Rigidbody(=집을 수 있는 화물/컨테이너)를 트위스트락 콘 위치 기준으로 잡고/놓고,
    /// 빈 스프레더가 컨테이너 윗면을 통과하지 못하게 호이스트를 클램프한다(게임 동작).
    /// 컨테이너 식별은 특정 컴포넌트(ContainerInstance 등)에 의존하지 않는다 — Rigidbody가 달린
    /// 자유 강체면 절차적 스폰(VRTestMenu)이든 프리팹이든 모두 잡힌다. VR 컨트롤러가 ToggleGrab()을 호출한다.
    /// </summary>
    [AddComponentMenu("Container/STS Crane/Spreader Grabber")]
    [RequireComponent(typeof(StsCrane))]
    [DisallowMultipleComponent]
    public sealed class SpreaderGrabber : MonoBehaviour
    {
        [Header("잡기")]
        [Tooltip("트위스트락(콘)에서 이 거리 안의 가장 가까운 컨테이너를 잡는다(m). 단 maxGrabRange로 상한이 걸린다.")]
        [SerializeField] float grabRange = 0.35f;
        [Tooltip("잡기 인식 반경의 상한(m). 1/24 미니어처라 grabRange가 커도 실제 반경은 이 값으로 제한된다. " +
                 "잡기 범위를 실제로 넓히려면 grabRange가 아니라 이 값을 올릴 것(기존 코드 상한 0.1을 인스펙터로 노출).")]
        [SerializeField] float maxGrabRange = 0.1f;
        [Tooltip("잡은 컨테이너 긴 축이 이 길이를 넘으면 40ft로 자동 신축(20ft≈0.25 / 40ft≈0.51, m)")]
        [SerializeField] float sizeThreshold = 0.38f;
        [Tooltip("Console에 집기 진단 로그 출력")]
        [SerializeField] bool debugLog = true;

        [Header("통과 방지")]
        [SerializeField] bool blockPassThrough = true;
        [Tooltip("컨테이너 윗면 위로 둘 여유(m)")]
        [SerializeField] float topClearance = 0f;
        [Tooltip("footprint 바깥으로 이 수평 거리까지는 '위'로 보고 멈춤(m)")]
        [SerializeField] float passXZmargin = 0.1f;

        [Header("성능")]
        [SerializeField] float refreshInterval = 0.5f;

        StsCrane crane;
        SpreaderLockAnimator lockAnim;
        SpreaderTelescope telescope;   // 잡은 컨테이너 크기에 맞춰 20/40ft 신축
        SpreaderHoist spreaderHoist;   // 잡은 컨테이너 밑면 기준으로 하강 바닥 한계 설정
        Transform[] twistlocks;   // Twistlock_Cone들 — 잡기 기준점
        readonly List<Rigidbody> bodies = new List<Rigidbody>();   // 크레인 외부의 집을 수 있는 강체들(재사용 버퍼 — 매 갱신 새 할당 방지)
        float nextRefresh;

        // 화물은 씬 배치라 런타임에 거의 안 변함 → 무거운 전수 스캔(FindObjectsByType)의 최소 간격.
        // 직렬화된 refreshInterval(기존 0.5s)이 작아도 이 값 이하로는 안 내려가 스캔/GC 빈도를 낮춘다.
        // (잡힌/놓인 상태는 매 프레임 IsChildOf로 거르므로 목록을 자주 다시 만들 필요가 없다.)
        const float MinRescanInterval = 3f;

        Transform AttachPoint => (crane != null && crane.Attach != null) ? crane.Attach.transform : null;

        void Awake()
        {
            crane = GetComponent<StsCrane>();
            lockAnim = GetComponentInChildren<SpreaderLockAnimator>(true);
            telescope = GetComponentInChildren<SpreaderTelescope>(true);
            spreaderHoist = crane != null ? crane.Spreader as SpreaderHoist : null;

            var list = new List<Transform>();
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (CraneHud.BaseName(t.name) == "Twistlock_Cone") list.Add(t);
            twistlocks = list.ToArray();

            Refresh();
        }

        void Start()
        {
            // 이 줄이 Play 시 Console에 안 보이면 = SpreaderGrabber가 안 돌고 있는 것(컴파일/재생성 문제)
            if (debugLog)
                Debug.Log($"[Crane] SpreaderGrabber 활성 — 트위스트락 {twistlocks.Length}개, " +
                          $"집을수있는강체 {bodies.Count}개, 통과방지 {blockPassThrough}");
        }

        void Refresh()
        {
            // 크레인 자신(스프레더/부착된 화물 포함) 아래의 강체는 제외 — 외부 자유 강체만 후보.
            // 버퍼(bodies)를 비우고 다시 채워 매 갱신 새 List/배열 할당을 피한다(주기적 GC 절감).
            var all = FindObjectsByType<Rigidbody>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            bodies.Clear();
            foreach (var rb in all)
                if (rb != null && !rb.transform.IsChildOf(transform)) bodies.Add(rb);
            nextRefresh = Time.time + Mathf.Max(refreshInterval, MinRescanInterval);
        }

        // 트위스트락 콘들의 중심(없으면 부착점) — 잡기 기준점
        Vector3 GrabPoint()
        {
            if (twistlocks != null && twistlocks.Length > 0)
            {
                Vector3 sum = Vector3.zero; int n = 0;
                foreach (var t in twistlocks) if (t != null) { sum += t.position; n++; }
                if (n > 0) return sum / n;
            }
            return AttachPoint != null ? AttachPoint.position : transform.position;
        }

        /// <summary>VR 컨트롤러 Y 버튼 — 비어 있으면 트위스트락 근처 컨테이너를 잡는다(이미 잡았으면 무시).</summary>
        public void Grab()
        {
            var attach = crane != null ? crane.Attach : null;
            if (attach == null || attach.HasContainer) return;

            Vector3 gp = GrabPoint();
            var c = FindNearest(gp, out float dist);
            if (debugLog)
                Debug.Log($"[Crane] 집기 시도 — 기준점(트위스트락) {gp}, 후보 {bodies.Count}개, " +
                          $"최근접 {(c != null ? $"{c.name} (거리 {dist:F3} / 허용 {grabRange})" : "없음")}");
            if (c == null) return;

            // 컨테이너 윗면을 부착점(스프레더 밑)에 맞춰 매달기 — 중앙 관통 방지
            bool hasBounds = TryBounds(c, out Bounds b);
            float pivotToTop = hasBounds ? (b.max.y - c.position.y) : 0f;

            // 잡은 컨테이너 긴 축 길이로 스프레더 텔레스코픽 자동 신축(20/40ft). 놓아도 유지.
            if (telescope != null && hasBounds)
            {
                float longSide = Mathf.Max(b.size.x, b.size.z);
                bool is40 = longSide > sizeThreshold;
                telescope.Set40(is40);
                if (debugLog) Debug.Log($"[Crane] 컨테이너 긴축 {longSide:F3}m → {(is40 ? "40ft" : "20ft")} 신축");
            }

            attach.Attach(c);
            Vector3 lp = c.localPosition;
            lp.y -= pivotToTop;
            c.localPosition = lp;

            // 하강 바닥 한계를 '컨테이너 밑면' 기준으로 — 스프레더가 아니라 컨테이너가 바닥(y=0)에 닿고 멈추게.
            if (spreaderHoist != null && TryBounds(c, out Bounds held))
            {
                Transform sp = spreaderHoist.transform;                 // 스프레더 원점
                float dropToBottom = sp.position.y - held.min.y;        // 스프레더 원점 → 컨테이너 밑면(아래로)
                float parentY = sp.parent != null ? sp.parent.position.y : 0f;
                float floorMinY = dropToBottom - parentY;               // 컨테이너 밑면이 y=0에 오는 스프레더 로컬 Y
                spreaderHoist.SetFloorOffset(floorMinY - spreaderHoist.Min);
                if (debugLog) Debug.Log($"[Crane] 컨테이너 밑면 기준 바닥 — 하강한계 +{floorMinY - spreaderHoist.Min:F3} (높이 {held.size.y:F3})");
            }

            if (lockAnim != null) lockAnim.SetLocked(true);
        }

        /// <summary>VR 컨트롤러 X 버튼 — 잡고 있으면 놓는다(비어 있으면 무시).</summary>
        public void Release()
        {
            var attach = crane != null ? crane.Attach : null;
            if (attach == null || !attach.HasContainer) return;

            attach.Detach();
            if (spreaderHoist != null) spreaderHoist.SetFloorOffset(0f);   // 빈 스프레더 바닥 한계 복원
            if (lockAnim != null) lockAnim.SetLocked(false);
            if (debugLog) Debug.Log("[Crane] 놓기(Detach)");
        }

        /// <summary>잡고 있으면 놓고, 아니면 잡는다(토글). 단일 버튼 매핑용 — 현재 컨트롤러는 Grab/Release를 직접 호출.</summary>
        public void ToggleGrab()
        {
            var attach = crane != null ? crane.Attach : null;
            if (attach != null && attach.HasContainer) Release();
            else Grab();
        }

        void LateUpdate()
        {
            if (Time.time >= nextRefresh) Refresh();
            if (!blockPassThrough || crane == null) return;

            var attach = crane.Attach;
            var hoist = crane.Spreader;
            Transform ap = AttachPoint;
            if (attach == null || hoist == null || ap == null) return;

            // 아래로 내려가면 안 되는 기준면(refBottomY)과, '바로 위에 있는지' 판정에 쓰는 중심(refCenter)을 정한다.
            //  - 컨테이너를 들고 있으면: 들고 있는 컨테이너의 '밑면'과 그 XZ 중심
            //    → 바로 밑에 깔린 컨테이너 윗면 위에 밑면이 얹히고 멈춤(깔아뭉개기/땅속 관통 방지).
            //  - 빈 스프레더면: 부착점(점) — 기존 통과 방지 동작.
            float refBottomY;
            Vector3 refCenter;
            if (attach.HasContainer)
            {
                if (!TryBounds(attach.AttachedContainer, out Bounds held)) return;
                refBottomY = held.min.y;
                refCenter = held.center;
            }
            else
            {
                refBottomY = ap.position.y;
                refCenter = ap.position;
            }

            // refCenter가 '바로 위(XZ)'에 있는 외부 컨테이너 중 윗면이 가장 높은 것 → 그 위에서 멈춤.
            //   중심-위 판정이라 '옆에 나란히 놓인' 컨테이너는 제외된다(잡자마자 옆 컨테이너 높이로
            //   끌려 올라가던 문제 방지). bodies는 크레인 자식(스프레더/들고 있는 컨테이너)을 제외한다.
            //
            // 여유(overMargin):
            //   - 컨테이너를 들고 있을 땐 0 → '정확히 위'일 때만 받침으로 인정. passXZmargin(0.1m)은
            //     1/24 미니어처(폭 ~0.1m)에선 컨테이너 폭만큼 커서, 옆(넓은 옆면 쪽)에 내려놓으려 해도
            //     중심이 옆 컨테이너 footprint 안으로 들어가 '위'로 오인 → 투명 벽처럼 안 내려가던 문제 해결.
            //   - 빈 스프레더는 약간의 여유(부착점은 점이라 필요). 단 저장된 passXZmargin(0.1m)은 미니어처엔
            //     너무 커서 0.03m로 상한 — 빈 스프레더가 옆 컨테이너에 멀리서부터 걸리던 것 방지.
            float overMargin = attach.HasContainer ? 0f : Mathf.Min(passXZmargin, 0.03f);
            float top = float.MinValue;
            bool over = false;
            foreach (var rb in bodies)
            {
                if (rb == null) continue;
                // 크레인 자식(스프레더/방금 잡은 컨테이너)은 제외. bodies는 주기적으로만 갱신돼
                // 잡은 직후 자기 자신이 목록에 남아 '자기 윗면'을 받침으로 오인 → 끝까지 치솟던 버그 방지.
                if (rb.transform.IsChildOf(transform)) continue;
                if (!TryBounds(rb.transform, out Bounds b)) continue;
                // refCenter가 후보 footprint(±overMargin) 안에 들어와야 '바로 위'로 인정
                if (refCenter.x < b.min.x - overMargin || refCenter.x > b.max.x + overMargin) continue;
                if (refCenter.z < b.min.z - overMargin || refCenter.z > b.max.z + overMargin) continue;
                if (b.max.y > top) { top = b.max.y; over = true; }
            }
            if (!over) return;

            float limit = top + topClearance;
            if (refBottomY < limit) hoist.MoveTo(hoist.Current + (limit - refBottomY));   // 로컬 Y ≈ 월드 Y
        }

        // 기준점에서 (상한 적용된) grabRange 안의 가장 가까운 컨테이너(콜라이더 있으면 그것 우선)
        Transform FindNearest(Vector3 gp, out float dist)
        {
            dist = float.MaxValue;
            float range = Mathf.Min(grabRange, maxGrabRange);

            // 콜라이더 기반 우선 — range 안의 콜라이더 중 크레인 외부 Rigidbody가 달린 것들 중 '가장 가까운' 1개
            //   (예전: 첫 hit을 그대로 잡아 원치 않는 컨테이너가 짚히던 문제 → 최근접으로 선택)
            Transform nearestCol = null;
            float nearestColD = float.MaxValue;
            var hits = Physics.OverlapSphere(gp, range);
            foreach (var h in hits)
            {
                var rb = h.attachedRigidbody;
                if (rb == null || rb.transform.IsChildOf(transform)) continue;
                float d = Vector3.Distance(gp, h.ClosestPoint(gp));   // 콜라이더 표면까지 거리
                if (d < nearestColD) { nearestColD = d; nearestCol = rb.transform; }
            }
            if (nearestCol != null) { dist = nearestColD; return nearestCol; }

            // 렌더러 바운즈 최근접(콜라이더를 못 맞춘 경우)
            Transform best = null;
            foreach (var rb in bodies)
            {
                if (rb == null || rb.transform.IsChildOf(transform)) continue;
                if (!TryBounds(rb.transform, out Bounds b)) continue;
                float d = Vector3.Distance(gp, b.ClosestPoint(gp));   // 바운즈 표면까지 거리
                if (d <= range && d < dist) { dist = d; best = rb.transform; }
            }
            return best;
        }

        // 자식 Renderer들의 월드 AABB(콜라이더 없음 대응)
        static bool TryBounds(Transform t, out Bounds b)
        {
            b = default;
            var rends = t.GetComponentsInChildren<Renderer>();
            if (rends == null || rends.Length == 0) return false;
            b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            return true;
        }
    }
}
