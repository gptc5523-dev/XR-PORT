using System.Collections.Generic;
using UnityEngine;

namespace AIXRCrane.Crane.Sts
{
    /// <summary>스프레더에서 먼 화물은 kinematic 으로 재우고 가까운 것만 깨워(동적 + ContainerPhysics) 물리 부하를 줄인다.
    /// '원래 동적'인 컨테이너만 관리 — 야드 배경(kinematic)·크레인 자식·배 화물은 제외.</summary>
    [AddComponentMenu("AI-XR Crane/STS Crane/Cargo Sleep Manager")]
    [DisallowMultipleComponent]
    public sealed class CargoSleepManager : MonoBehaviour
    {
        [Header("근접 반경(모델 units · 1/24)")]
        // 격자(1/24): 행 피치 0.102 · 40ft 반길이 0.254 — 반경이 크면 X 로 수십 행이 깨어난다(1.0 → 180개).
        //   40ft 반길이 + 이동 여유를 덮되 격자보다 작게: wake 0.35 / sleep 0.50.
        [Tooltip("스프레더가 이 거리 안에 오면 깨운다(동적). 40ft 반길이(0.254)+여유. 기본 0.35.")]
        [SerializeField] float wakeRadius = 0.35f;
        [Tooltip("이 거리 밖으로 멀어지면 다시 재운다(kinematic). flip-flop 방지 히스테리시스라 wakeRadius보다 커야 함. 기본 0.50.")]
        [SerializeField] float sleepRadius = 0.50f;

        [Header("주기(초)")]
        [Tooltip("재우기/깨우기 거리 판정 주기. 770개 거리검사라도 매우 가벼움(무할당).")]
        [SerializeField] float scanInterval = 0.15f;
        [Tooltip("관리 목록 재구성 주기 — 새로 스폰된 화물 편입·파괴된 것 정리.")]
        [SerializeField] float rescanInterval = 5f;

        [SerializeField] bool debugLog = true;

        // 관리 1건 — 강체+콜라이더 캐시와 현재 깨움 상태.
        sealed class Entry { public Rigidbody rb; public Collider col; public bool awake; }
        readonly List<Entry> managed = new List<Entry>(1024);
        readonly HashSet<Rigidbody> managedSet = new HashSet<Rigidbody>();

        StsCrane crane;
        Transform spreaderT;
        Transform craneRoot;
        float nextScan, nextRescan;
        int lastAwakeCount = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoSpawn() => CraneHud.EnsureSpawned<CargoSleepManager>("Cargo");

        void Start()
        {
            if (sleepRadius < wakeRadius) sleepRadius = wakeRadius * 1.4f;   // 히스테리시스 보장
            crane = FindAnyObjectByType<StsCrane>();
            craneRoot = crane != null ? crane.transform : null;
            spreaderT = (crane != null ? crane.Spreader as Component : null)?.transform;

            BuildManagedList();
            // 시작 시 전부 재움 — 스프레더 근처 것만 첫 스캔에서 깨어난다.
            foreach (var e in managed) Sleep(e);
            if (debugLog)
                Debug.Log($"[Cargo] 근접 활성화 시작 — 관리 화물 {managed.Count}개 모두 재움(kinematic). " +
                          $"wake<{wakeRadius} / sleep>{sleepRadius}, 스프레더={(spreaderT != null ? spreaderT.name : "없음")}");
        }

        // 관리 대상 = 이름에 'Container' + Rigidbody + 크레인 자식 아님 + 원래 동적.
        //   이미 관리 중인 것은 (내가 재워 kinematic 이어도) 유지.
        void BuildManagedList()
        {
            // 파괴된 것 정리
            for (int i = managed.Count - 1; i >= 0; i--)
                if (managed[i].rb == null) { managedSet.Remove(managed[i].rb); managed.RemoveAt(i); }

            foreach (var rb in FindObjectsByType<Rigidbody>(FindObjectsInactive.Exclude))
            {
                if (rb == null || managedSet.Contains(rb)) continue;
                if (rb.name.IndexOf("Container", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (craneRoot != null && rb.transform.IsChildOf(craneRoot)) continue;   // 크레인/스프레더 자식 제외
                // 배 화물은 재우지 않는다 — 재우면 놓을 때 SpreaderAttach 복원과 겹쳐 공중에 뜬다.
                //   정착한 강체는 PhysX 가 자동 sleep 하므로 항상 동적이어도 부하는 거의 0.
                if (rb.name.StartsWith(StsPartNames.ShipContainer, System.StringComparison.OrdinalIgnoreCase)) continue;
                if (rb.isKinematic) continue;   // '원래 동적'만 — 야드 배경(kinematic) 보존
                var e = new Entry { rb = rb, col = rb.GetComponent<Collider>(), awake = true };
                managed.Add(e);
                managedSet.Add(rb);
            }
            nextRescan = Time.time + rescanInterval;
        }

        void Update()
        {
            if (Time.time >= nextRescan) BuildManagedList();
            if (Time.time < nextScan) return;
            nextScan = Time.time + scanInterval;

            // 스프레더를 못 찾으면(비크레인 씬 등) 전부 재운 채 안전 유지.
            if (spreaderT == null)
            {
                spreaderT = (crane != null ? crane.Spreader as Component : null)?.transform;
                if (spreaderT == null) return;
            }

            Vector3 sp = spreaderT.position;
            float wake2 = wakeRadius * wakeRadius;
            float sleep2 = sleepRadius * sleepRadius;
            int awake = 0;

            for (int i = managed.Count - 1; i >= 0; i--)
            {
                var e = managed[i];
                if (e.rb == null) { managedSet.Remove(e.rb); managed.RemoveAt(i); continue; }

                // 잡혀서 크레인 자식이 된 화물은 부착 로직이 제어 — 건드리지 않는다.
                if (craneRoot != null && e.rb.transform.IsChildOf(craneRoot)) continue;

                // 히스테리시스로 원하는 상태를 정하고, 실제 isKinematic 과 다를 때만 토글(부착 로직이 바꿔 놓아도 자가 보정).
                float d2 = (e.rb.position - sp).sqrMagnitude;
                bool wantAwake = e.awake ? (d2 <= sleep2) : (d2 < wake2);
                if (wantAwake)
                {
                    if (e.rb.isKinematic) Wake(e); else e.awake = true;
                    awake++;
                }
                else
                {
                    if (!e.rb.isKinematic) Sleep(e); else e.awake = false;
                }
            }

            if (debugLog && awake != lastAwakeCount)
            {
                lastAwakeCount = awake;
                Debug.Log($"[Cargo] 깨어있는 화물 {awake}/{managed.Count}개 (나머지는 재움 — 솔버·연속충돌·바닥가드 생략)");
            }
        }

        // ② 재움: kinematic + Discrete(연속충돌 끔). 솔버는 kinematic이라 무의미. 잔여 속도 제거.
        void Sleep(Entry e)
        {
            if (e.rb == null) return;
            if (!e.rb.isKinematic) { e.rb.linearVelocity = Vector3.zero; e.rb.angularVelocity = Vector3.zero; }
            e.rb.collisionDetectionMode = CollisionDetectionMode.Discrete;   // Continuous보다 broadphase 저렴
            e.rb.isKinematic = true;
            e.awake = false;
        }

        // ② 깨움: 동적 + 풀 ContainerPhysics(Solver 30·ContinuousSpeculative) — 밀림/토플/적층 안정성 그대로.
        void Wake(Entry e)
        {
            if (e.rb == null) return;
            e.rb.isKinematic = false;
            ContainerPhysics.Apply(e.rb, e.col);   // Solver 30 / Continuous / 접촉오프셋 / 마찰 복원
            e.awake = true;
        }
    }
}
