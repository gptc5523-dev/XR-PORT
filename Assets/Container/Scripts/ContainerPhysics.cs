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

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoSpawn()
        {
            if (FindAnyObjectByType<ContainerPhysicsStabilizer>() != null) return;
            new GameObject("ContainerPhysicsStabilizer (auto)").AddComponent<ContainerPhysicsStabilizer>();
        }

        void Start()
        {
            int n = TuneAllInScene();
            if (debugLog) Debug.Log($"[ContainerPhysics] 적층 안정화 일괄 적용 — 컨테이너 {n}개");
        }

        /// <summary>씬의 모든 'Container' 강체에 ContainerPhysics.Apply 적용. 적용 개수 반환.</summary>
        public static int TuneAllInScene()
        {
            int n = 0;
            foreach (var rb in FindObjectsByType<Rigidbody>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (rb.name.IndexOf("Container", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                ContainerPhysics.Apply(rb, rb.GetComponent<Collider>());
                n++;
            }
            return n;
        }
    }
}
