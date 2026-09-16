using System.Collections.Generic;
using UnityEngine;

namespace ContainerProject
{
    /// <summary>
    /// 컨테이너 적층 안정화(방식 A) — 1/24 미니어처라 PhysX 기본값(접촉 오프셋·솔버·마찰)이 실척 기준이라
    /// 안 맞아 3단부터 기우는 문제를, 전역 물리 설정은 안 건드리고 '컨테이너에만' 값을 박아 해결.
    ///
    /// 두 경로로 전부 적용:
    ///   ① 생성 시: VRTestMenu.BuildOne 이 ContainerPhysics.Apply 호출 → 새로 만드는 컨테이너에 즉시 적용
    ///   ② 기존 씬: ContainerPhysicsStabilizer 가 플레이 시작 시 씬의 모든 'Container' 강체를 찾아 일괄 적용
    /// </summary>
    public static class ContainerPhysics
    {
        // 1/24 컨테이너(폭 ≈0.10, 높이 ≈0.11 units) 기준으로 잡은 값.
        public const float ContactOffset             = 0.001f;  // 폭의 ~1% (기본 0.01은 ~10%라 떨림 유발)
        public const int   SolverIterations          = 30;      // 적층 강성(기본 6)
        public const int   SolverVelocityIterations  = 10;      // (기본 1)
        public const float MaxDepenetrationVelocity  = 1.0f;    // 겹침 해소 시 폭발적으로 안 튕기게
        public const float Friction                  = 0.7f;    // 미끄럼 방지

        static PhysicsMaterial _mat;
        public static PhysicsMaterial Material
        {
            get
            {
                if (_mat == null)
                {
                    _mat = new PhysicsMaterial("ContainerStack")
                    {
                        dynamicFriction = Friction,
                        staticFriction  = Friction,
                        bounciness      = 0f,
                        frictionCombine = PhysicsMaterialCombine.Maximum,
                        bounceCombine   = PhysicsMaterialCombine.Minimum,
                    };
                }
                return _mat;
            }
        }

        /// <summary>컨테이너 1개의 강체·콜라이더에 적층 안정화 설정을 박는다(멱등 — 여러 번 호출해도 동일).</summary>
        public static void Apply(Rigidbody rb, Collider col)
        {
            if (rb != null)
            {
                rb.solverIterations         = SolverIterations;
                rb.solverVelocityIterations = SolverVelocityIterations;
                rb.maxDepenetrationVelocity = MaxDepenetrationVelocity;
                rb.collisionDetectionMode   = CollisionDetectionMode.ContinuousSpeculative; // 작은 물체 관통/터널링 방지
            }
            if (col != null)
            {
                col.contactOffset = ContactOffset;
                col.sharedMaterial = Material;
            }
        }
    }

    /// <summary>
    /// 씬에 이미 구워진 컨테이너 전부에 적층 안정화를 일괄 적용 — 플레이 시작 시 1회.
    /// 이름에 "Container"가 들어가고 Rigidbody를 가진 오브젝트를 대상으로 한다(Yard_Container_*, Container_Procedural_* 등).
    /// 씬에 안 붙여도 [RuntimeInitializeOnLoadMethod]로 자동 스폰.
    /// </summary>
    [AddComponentMenu("Container/Container Physics Stabilizer")]
    [DisallowMultipleComponent]
    public sealed class ContainerPhysicsStabilizer : MonoBehaviour
    {
        [SerializeField] bool debugLog = true;

        // 바닥 관통 방지(하드 클램프) — 스프레더가 잡은(kinematic·무한질량) 컨테이너로 바닥의 동적 컨테이너를
        //   '강제로 눌러도' 바닥 콜라이더(부두 슬래브 Asphalt)를 뚫고 빠지지 않게, 매 FixedUpdate에서
        //   컨테이너 '콜라이더 밑면'을 바닥 윗면으로 되돌린다.  ▸ 크레인에 매달린 것만 제외(공중 이송 정상).
        //   ▸ 토플/적층은 X·Z·회전이라 무관.
        //   2026-09-16 구멍 2개를 막음(오너 "컨테이너가 바닥을 뚫는다"): ①kinematic 이면 무조건 건너뛰어
        //   놓은 뒤 남은 컨테이너·야드 배치가 무방비였다 ②Start 목록만 봐서 런타임에 강체가 붙는 것을 못 봤다.
        const float FloorGuardSkin = 0.004f;   // 정착 시 자연 침투(≈ContactOffset 0.001)보다 크게 — 떨림 없이 깊은 관통만 교정
        // 런타임에 강체가 붙는 컨테이너(PortDemoDirector.MakeGrabbable·PlcCargoReplay.MakeBox)는 Start 목록에 없다 →
        //   주기적으로 다시 모은다. CargoSleepManager 의 rescanInterval 과 같은 5초.
        const float RescanInterval = 5f;
        readonly List<Rigidbody> _bodies = new List<Rigidbody>();
        readonly List<Collider>  _cols   = new List<Collider>();   // _bodies와 1:1 평행 — 밑면(bounds) 측정용
        float _floorTopY, _nextRescan;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoSpawn()
        {
            if (FindAnyObjectByType<ContainerPhysicsStabilizer>() != null) return;
            new GameObject("ContainerPhysicsStabilizer (auto)").AddComponent<ContainerPhysicsStabilizer>();
        }

        void Start()
        {
            _floorTopY = FindFloorTopY(out bool hasFloor);
            Rescan();
            if (debugLog) Debug.Log($"[ContainerPhysics] 적층 안정화 일괄 적용 — 컨테이너 {_bodies.Count}개, " +
                                    $"바닥 윗면 y={_floorTopY:F3}({(hasFloor ? "레거시 VirtualFloor" : "데크 y=0 규약")})");
        }

        // 씬의 컨테이너 강체를 다시 모은다(멱등 — Apply 는 같은 값을 다시 박을 뿐).
        //   _bodies와 1:1로 콜라이더를 캐시(밑면 측정용). 콜라이더 없으면 null → 피봇 폴백.
        void Rescan()
        {
            _nextRescan = Time.time + RescanInterval;
            _bodies.Clear();
            _cols.Clear();
            TuneAllInScene(_bodies);
            foreach (var rb in _bodies) _cols.Add(rb != null ? rb.GetComponent<Collider>() : null);
        }

        // 바닥 윗면 아래로 내려간 동적 컨테이너의 '밑면'을 되돌려, 강제 누름에도 바닥을 못 뚫게 한다.
        // QA 컨테이너 높이(1/24) ≈ 0.108m, 반높이 ≈ 0.054m — 잔여 관통이 반높이에 이르면 구버전(피봇 기준) 회귀.
        const float QaHalfHeight = 0.054f;
        bool qaGuardActive;   // QA: 바닥가드 보정 진행 상태(엣지에서만 콘솔 출력)

        void FixedUpdate()
        {
            if (Time.time >= _nextRescan) Rescan();   // 런타임에 강체가 붙은 컨테이너를 목록에 들인다

            float worstPen = 0f; bool anyCorr = false; string worstName = null;
            for (int i = _bodies.Count - 1; i >= 0; i--)
            {
                var rb = _bodies[i];
                if (rb == null) { _bodies.RemoveAt(i); _cols.RemoveAt(i); continue; }

                // 콜라이더 월드 AABB 최저점 = 컨테이너 실제 밑면. 피봇(중심)이 아니라 이걸로 침투를 잰다.
                var col = _cols[i];
                float bottomY     = (col != null) ? col.bounds.min.y : rb.position.y;
                float penetration = _floorTopY - bottomY;
                if (penetration > FloorGuardSkin)
                {
                    // 매달려 이송 중인 것만 제외 — 하강 한계는 스프레더 통과방지 클램프 소관이고, 여기서 같이
                    //   끌어올리면 스프레더와 서로 밀며 떤다. 매달리지 않았으면 kinematic 이어도 교정한다.
                    if (rb.GetComponentInParent<Container.Crane.Sts.StsCrane>() != null) continue;

                    var p = rb.position; p.y += penetration; rb.position = p;   // 밑면을 바닥 윗면까지 끌어올림
                    if (!rb.isKinematic) { var v = rb.linearVelocity; if (v.y < 0f) { v.y = 0f; rb.linearVelocity = v; } }
                    anyCorr = true;
                    if (penetration > worstPen) { worstPen = penetration; worstName = rb.name; }
                }
            }

            // QA S-PHYS-3: 바닥가드 보정이 시작/종료된 순간만 한 줄(매틱 폭주 방지).
            //   PASS = 보정 시점 침투가 반높이(0.054) 미만 — 즉 매틱 잡아 깊은 관통을 안 허용(구버전 회귀 아님).
            if (Container.Crane.Sts.QaLog.Enabled && anyCorr != qaGuardActive)
            {
                qaGuardActive = anyCorr;
                if (anyCorr)
                    Container.Crane.Sts.QaLog.Check("FLOOR", "guard", worstPen < QaHalfHeight,
                        $"body={worstName} floorTopY={_floorTopY:F3} penetration={worstPen:F3} " +
                        $"skin={FloorGuardSkin:F3} halfHeight={QaHalfHeight:F3} corrected=true");
                else
                    Container.Crane.Sts.QaLog.Info("FLOOR", "settle", "penetration<=skin corrected=false (정착)");
            }
        }

        // 바닥 윗면의 월드 y. 레거시 'VirtualFloor'가 남아 있으면 그것을, 없으면 데크 y=0(=부두
        // 슬래브 Asphalt 윗면) 규약을 쓴다. 두 값은 같다 — 데크 y=0 은 StsConfig 의 못 움직이는 기준.
        //   ★ 바닥 높이의 단일 출처(SSOT) — 통과방지 클램프(SpreaderGrabber)·배치 검사도 이걸 쓴다.
        //     GameObject.Find 를 타므로 호출자는 Start 에서 한 번 받아 캐시할 것(매 틱 호출 금지).
        public static float FindFloorTopY(out bool found)
        {
            found = false;
            var vf = GameObject.Find("VirtualFloor");
            if (vf != null && vf.TryGetComponent<BoxCollider>(out var bc))
            {
                found = true;
                return bc.transform.TransformPoint(bc.center + new Vector3(0f, bc.size.y * 0.5f, 0f)).y;
            }
            return 0f;
        }

        // 컨테이너 이름 규약 — 절차/씬 생성분은 "…Container…"(ShipContainer_*, Container_Procedural_*),
        //   야드 FBX 배치분은 "Cont40_00"·"Cont20_03". 야드 규약이 빠져 있어 바닥가드가 야드 컨테이너
        //   20개를 통째로 못 보고 있었다(2026-09-16). PortDemoDirector·StsGrabProbe 와 같은 규약.
        static bool IsContainerName(string n) =>
            n.IndexOf("Container", System.StringComparison.OrdinalIgnoreCase) >= 0
            || n.StartsWith("Cont20_") || n.StartsWith("Cont40_");

        /// <summary>씬의 모든 'Container' 강체에 ContainerPhysics.Apply 적용. 적용한 강체를 collect 리스트에 모은다(바닥 가드 추적용). 적용 개수 반환.</summary>
        public static int TuneAllInScene(List<Rigidbody> collect)
        {
            int n = 0;
            foreach (var rb in FindObjectsByType<Rigidbody>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!IsContainerName(rb.name)) continue;
                ContainerPhysics.Apply(rb, rb.GetComponent<Collider>());
                collect?.Add(rb);
                n++;
            }
            return n;
        }
    }
}
