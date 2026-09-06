using System.Collections;
using UnityEngine;

namespace Container.Crane.Sts
{
    /// <summary>
    /// **컨테이너 배치 → 잡기 → 들어올리기 → 내리기** 자동 검증 시나리오 (VR 없이 에디터 Play로 동작 확인).
    ///
    /// Play 시: 스프레더 바로 아래 지면에 테스트 컨테이너를 놓고 → 실제 무버(SpreaderHoist, worldVertical)로
    /// 하강하며 매 프레임 SpreaderGrabber.Grab() 시도 → 코너 안착되면 잡힘 → 들어올림 → 원위치로 내림 → 놓기 → 반복.
    ///
    /// 콘솔 로그([컨테이너테스트]/[Crane])로 각 단계·잡힘 여부·스프레더 월드Y를 확인한다. 그랩버 debugLog가 집기 판정 로그도 찍음.
    /// 전제: 먼저 'RTG 크레인 생성' 실행(SpreaderHoist·Grabber·Attach를 자동 배선). 미배선 시 오류 로그 후 자동 비활성.
    /// </summary>
    [AddComponentMenu("Container/STS Crane/컨테이너 잡기 테스트 (FBX)")]
    [DisallowMultipleComponent]
    public sealed class ContainerLiftScenario : MonoBehaviour
    {
        [Tooltip("권상 속도 (m/min, 실척). 월드속도 = ×ModelScale.")]
        [SerializeField] float hoistMpm = 62f;
        [Tooltip("각 단계 사이 정지(초).")]
        [SerializeField] float holdTime = 0.8f;
        [Tooltip("들어올리는 높이 증가량(정규화, 0~1).")]
        [SerializeField] float liftAmount = 0.45f;
        [Tooltip("계속 반복.")]
        [SerializeField] bool loop = true;
        [Tooltip("테스트 컨테이너 크기(월드 m). 기본 20ft/24 ≈ (0.25, 0.108, 0.10). 프리팹은 이 긴변에 맞춰 스케일.")]
        [SerializeField] Vector3 containerSize = new Vector3(0.25f, 0.108f, 0.10f);
        [Tooltip("실제 컨테이너 프리팹(비우면 큐브). 부착 메뉴가 Container_20ft를 자동 지정.")]
        [SerializeField] GameObject containerPrefab;
        public void SetContainerPrefab(GameObject p) => containerPrefab = p;

        const float MpmToMps = 1f / 60f;
        StsCrane crane;
        SpreaderHoist hoist;
        SpreaderGrabber grabber;
        Transform spreadT;
        Rigidbody container;
        float wTop, wBottom;

        void Start()
        {
            if (!Wire()) { enabled = false; return; }
            SpawnContainer();
            StartCoroutine(Run());
        }

        bool Wire()
        {
            crane   = GetComponentInChildren<StsCrane>();
            grabber = GetComponentInChildren<SpreaderGrabber>();
            spreadT = FindDeep(transform, "Spreader");
            hoist   = spreadT != null ? spreadT.GetComponent<SpreaderHoist>() : null;
            if (crane == null || hoist == null || grabber == null || spreadT == null)
            {
                Debug.LogError($"[컨테이너테스트] 배선 필요 — StsCrane={crane}, Hoist={hoist}, Grabber={grabber}, Spreader={spreadT}. " +
                               "먼저 'Model ▸ FBX ▸ 크레인 ▸ RTG 크레인 생성' 실행하세요.");
                return false;
            }
            wTop = hoist.Max; wBottom = hoist.Min;   // worldVertical → 월드 Y 절대
            return true;
        }

        void SpawnContainer()
        {
            // 잡기 기준점 = 트위스트락 중심. 여기에 정렬해야 안착(IsSeatedOver가 컨테이너 중심 vs 기준점 비교).
            TwistlockFootprint(out Vector3 twCenter, out float twLong, out float twShort);
            float groundY = transform.position.y;

            GameObject go;
            if (containerPrefab != null)
            {
                go = Instantiate(containerPrefab);
                go.transform.rotation = spreadT.rotation;
            }
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.transform.localScale = containerSize;
                go.transform.rotation = spreadT.rotation;
                var mr = go.GetComponent<MeshRenderer>();
                var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                var m = new Material(sh) { name = "TestContainerMat" };
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", new Color(0.80f, 0.32f, 0.18f));
                else m.color = new Color(0.80f, 0.32f, 0.18f);
                mr.sharedMaterial = m;
            }
            go.name = "TestContainer";

            // 트위스트락 긴변 스팬에 맞춰 균일 스케일 → 스프레더에 맞는 크기(잘 보임)
            if (twLong > 0.02f) FitScale(go, twLong * 1.02f);

            // 밑면이 지면, 중심 X/Z = 트위스트락 중심(정렬)
            float halfH = WorldBounds(go, out _).extents.y;
            go.transform.position = new Vector3(twCenter.x, groundY + halfH, twCenter.z);

            // 잡을 강체 보장 — kinematic(안 떨어짐, FindNearest는 kinematic도 잡음) + 콜라이더
            var rb = go.GetComponent<Rigidbody>();
            if (rb == null) rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true; rb.useGravity = false;
            if (go.GetComponentInChildren<Collider>() == null)
            {
                var bc = go.AddComponent<BoxCollider>();
                var b = WorldBounds(go, out _);
                bc.center = go.transform.InverseTransformPoint(b.center);
                bc.size = new Vector3(b.size.x / go.transform.lossyScale.x,
                                      b.size.y / go.transform.lossyScale.y,
                                      b.size.z / go.transform.lossyScale.z);
            }
            container = rb;
            Debug.Log($"[컨테이너테스트] 컨테이너 배치 — {(containerPrefab ? "프리팹" : "큐브")} 트위스트락중심 {twCenter} pos {go.transform.position} 크기(긴변 {twLong:F3}, 높이 {halfH*2f:F3}).");
        }

        // 트위스트락(Spreader_Twistlock_*) 4개의 월드 중심 + X/Z 스팬(긴변/짧은변). = 그랩버 잡기 기준점.
        void TwistlockFootprint(out Vector3 center, out float longSpan, out float shortSpan)
        {
            var pts = new System.Collections.Generic.List<Vector3>();
            foreach (var t in spreadT.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith("Spreader_Twistlock_")) pts.Add(t.position);
            if (pts.Count >= 2)
            {
                Vector3 mn = pts[0], mx = pts[0];
                foreach (var p in pts) { mn = Vector3.Min(mn, p); mx = Vector3.Max(mx, p); }
                center = (mn + mx) * 0.5f;
                float sx = mx.x - mn.x, sz = mx.z - mn.z;
                longSpan = Mathf.Max(sx, sz); shortSpan = Mathf.Min(sx, sz);
            }
            else { center = spreadT.position; longSpan = containerSize.x; shortSpan = containerSize.z; }
        }

        // 긴변(X 또는 Z)을 target에 맞춰 균일 스케일(비율 보존).
        static void FitScale(GameObject go, float targetLong)
        {
            Bounds b = WorldBounds(go, out bool ok);
            if (!ok) return;
            float longNative = Mathf.Max(b.size.x, b.size.z);
            if (longNative < 1e-5f) return;
            go.transform.localScale *= targetLong / longNative;
        }

        static Bounds WorldBounds(GameObject go, out bool ok)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            ok = rs.Length > 0;
            if (!ok) return new Bounds(go.transform.position, Vector3.one * 0.1f);
            Bounds b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            return b;
        }

        bool Grabbed() => crane.Attach != null && crane.Attach.HasContainer;

        float Norm()
        {
            float r = wTop - wBottom;
            return r > 1e-6f ? Mathf.Clamp01((hoist.Current - wBottom) / r) : 0f;
        }
        float Nps()
        {
            float speed = hoistMpm * MpmToMps * StsConfig.ModelScale;
            float range = Mathf.Max(1e-4f, Mathf.Abs(wTop - wBottom));
            return speed / range;
        }

        IEnumerator Run()
        {
            do
            {
                // 1) 도킹(위)에서 시작
                yield return MoveHoist(1f, false);
                yield return new WaitForSeconds(0.5f);   // 그랩버 강체 스캔 여유

                // 2) 내리면서 매 프레임 잡기 시도
                Debug.Log("[컨테이너테스트] ① 하강하며 잡기 시도");
                float seatT = 0f;
                yield return LowerAndGrab(v => seatT = v);
                if (!Grabbed())
                {
                    Debug.LogWarning("[컨테이너테스트] 잡기 실패 — 스프레더가 컨테이너 위에 안착 못함(X/Z 정렬·높이 확인). 중단.");
                    yield break;
                }
                Debug.Log($"[컨테이너테스트] ② 잡음! (안착 t={seatT:F2}) 들어올림");
                yield return new WaitForSeconds(holdTime);

                // 3) 들어올림
                yield return MoveHoist(Mathf.Clamp01(seatT + liftAmount), true);
                yield return new WaitForSeconds(holdTime);

                // 4) 원위치로 내림
                Debug.Log("[컨테이너테스트] ③ 내림");
                yield return MoveHoist(seatT, true);
                yield return new WaitForSeconds(holdTime);

                // 5) 놓기
                grabber.Release();
                Debug.Log("[컨테이너테스트] ④ 놓음(Detach)");
                yield return new WaitForSeconds(holdTime);
            }
            while (loop);
        }

        // 하강하며 매 프레임 Grab() — 안착되면 잡히고 종료. 잡힌 정규화 위치를 콜백으로 반환.
        IEnumerator LowerAndGrab(System.Action<float> onSeat)
        {
            float nps = Nps();
            float t = Norm();
            float log = 0f;
            while (t > 0f && !Grabbed())
            {
                t = Mathf.MoveTowards(t, 0f, nps * Time.deltaTime);
                hoist.MoveToNormalized(t);
                grabber.Grab();
                log += Time.deltaTime;
                if (log >= 0.4f) { log = 0f; Debug.Log($"[컨테이너테스트] 하강 t={t:F2} 스프레더Y={spreadT.position.y:F3} grabbed={Grabbed()}"); }
                yield return null;
            }
            onSeat?.Invoke(Norm());
        }

        IEnumerator MoveHoist(float to, bool logY)
        {
            float nps = Nps();
            float t = Norm();
            float log = 0f;
            while (!Mathf.Approximately(t, to))
            {
                t = Mathf.MoveTowards(t, to, nps * Time.deltaTime);
                hoist.MoveToNormalized(t);
                if (logY) { log += Time.deltaTime; if (log >= 0.4f) { log = 0f;
                    Debug.Log($"[컨테이너테스트] 이동 t={t:F2} 스프레더Y={spreadT.position.y:F3} 컨테이너Y={(container ? container.position.y.ToString("F3") : "-")}"); } }
                yield return null;
            }
        }

        static Transform FindDeep(Transform r, string n)
        {
            if (r.name == n) return r;
            foreach (Transform c in r) { var x = FindDeep(c, n); if (x) return x; }
            return null;
        }
    }
}
