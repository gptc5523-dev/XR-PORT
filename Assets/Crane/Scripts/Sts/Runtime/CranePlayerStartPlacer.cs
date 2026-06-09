using UnityEngine;

namespace Container.Crane.Sts
{
    /// <summary>
    /// 시작 위치 배치 — 씬에 시작 마커(CranePlayerStartPoint)가 있으면, 씬 진입 시 로컬 플레이어 리그(XR Origin)를
    /// 그 마커의 '위치'와 '바라보는 방향(파란 Z축)'으로 한 번 옮긴다.
    ///
    ///   ★ 마커가 없으면 아무것도 안 한다 — 씬에 배치된 XR Origin 위치를 그대로 존중(중앙으로 강제하지 않음).
    ///     (예전엔 마커 없을 때 '레일 사이 중앙'을 자동 계산해 거기로 끌고 갔는데, 그게 '왜 중앙에서 시작?'의 원인.)
    ///
    /// 마커는 'Container > Create Player Start Point' 메뉴로 만들고, 씬에서 원하는 레인 끝으로 끌어다 두면 된다.
    /// 호스트(조종자)·관전자 모두 각자 로컬 카메라 리그(Camera.main.root)에 적용 — 네트워크 동기화 불필요.
    /// 씬에 안 붙여도 [RuntimeInitializeOnLoadMethod]로 자동 스폰(마커 없으면 무동작).
    /// </summary>
    [AddComponentMenu("Container/STS Crane/Crane Player Start Placer")]
    [DisallowMultipleComponent]
    public sealed class CranePlayerStartPlacer : MonoBehaviour
    {
        [Tooltip("바닥 위로 띄울 여유(m). 0이면 부두 바닥에 딱 붙음.")]
        [SerializeField] float floorClearance = 0f;
        [Tooltip("리그/마커가 늦게 떠도 될 때까지 재시도할 최대 프레임 수.")]
        [SerializeField] int maxAttempts = 300;
        [SerializeField] bool debugLog = true;

        const string QuayName = "Quay_Ground";

        bool done;
        bool warned;
        int attempts;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoSpawn() => CraneHud.EnsureSpawned<CranePlayerStartPlacer>("PlayerStartPlacer");

        void Update()
        {
            if (done) { enabled = false; return; }

            // 비활성 객체까지 포함해 검색(꺼둔 마커도 인식). 타입으로 못 찾으면 이름("PlayerStartPoint")으로도 시도.
            var marker = FindAnyObjectByType<CranePlayerStartPoint>(FindObjectsInactive.Include);
            if (marker == null)
            {
                var byName = GameObject.Find("PlayerStartPoint");
                if (byName != null) marker = byName.GetComponent<CranePlayerStartPoint>();
            }
            if (marker == null)
            {
                // 마커 없음 → 시작 위치를 건드리지 않는다(씬 배치 존중). 마커가 늦게 생길 수도 있어 잠깐 기다렸다 포기.
                if (++attempts > maxAttempts)
                {
                    if (debugLog && !warned)
                    {
                        Debug.LogWarning("[PlayerStartPlacer] 마커(CranePlayerStartPoint)를 못 찾음 — " +
                            "'Container > Create Player Start Point'로 만들어 씬(하이어라키)에 두세요. " +
                            "(마커 없으면 씬의 XR Origin 위치 그대로 시작합니다)");
                        warned = true;
                    }
                    enabled = false;
                }
                return;
            }

            var cam = Camera.main;
            if (cam == null) return;                 // 카메라 아직 — 다음 프레임
            Transform rig = cam.transform.root;       // XR Origin 리그 루트
            if (rig == null) return;

            // 위치: 마커 X/Z만 사용. Y(높이)는 마커 값 무시하고 항상 '바닥'을 찾아 맞춘다(공중에 안 뜨게).
            Vector3 mp = marker.transform.position;
            Vector3 pos = new Vector3(mp.x, ResolveFloorY(mp) + floorClearance, mp.z);

            // 방향: 마커 forward(파란 Z축)를 수평으로 평탄화. 거의 수직이면 리그 기존 방향 유지.
            Vector3 f = marker.transform.forward; f.y = 0f;
            Vector3 faceDir = f.sqrMagnitude > 1e-4f ? f.normalized : rig.forward;

            rig.SetPositionAndRotation(pos, Quaternion.LookRotation(faceDir, Vector3.up));
            done = true;
            enabled = false;
            if (debugLog) Debug.Log($"[PlayerStartPlacer] 마커 위치에서 시작 — pos {pos}, facing {faceDir}.");
        }

        // 해당 X/Z 위치의 '바닥' 월드 Y를 찾는다 — 마커 높이와 무관하게 항상 '걷는 면'에 발이 닿게.
        //   ★ 핵심 수정: 부두(Quay_Ground) '전체' bounds.max.y는 레일/구조물 꼭대기(≈0.22m)라, 거기에 플레이어를
        //     세우면 컨테이너(Y≈0)를 5m 위에서 내려다보는 '거대' 증상이 났다. 걷는 면=아스팔트 슬래브(수평 면적이
        //     가장 큰 렌더러)만 골라 그 윗면을 쓴다. 못 찾으면 레이캐스트→0 폴백.
        static float ResolveFloorY(Vector3 at)
        {
            var quay = GameObject.Find(QuayName);
            if (quay != null)
            {
                Renderer ground = null; float bestArea = 0f;
                foreach (var r in quay.GetComponentsInChildren<Renderer>())
                {
                    Vector3 e = r.bounds.size;
                    float area = e.x * e.z;                 // 수평 면적 — 아스팔트 슬래브가 압도적으로 큼(레일/차선은 가늘다)
                    if (area > bestArea) { bestArea = area; ground = r; }
                }
                if (ground != null)
                {
                    float y = ground.bounds.max.y;          // 걷는 면 윗면(아스팔트 ≈0) — 레일 꼭대기 무시
                    Debug.Log($"[PlayerStartPlacer] 바닥 Y={y:0.###} (걷는 면 '{ground.name}' 윗면). " +
                              $"※ 0.22 같은 값이면 아직 구조물 꼭대기를 잡은 것.");
                    return y;
                }
            }

            Vector3 from = new Vector3(at.x, at.y + 5f, at.z);
            if (Physics.Raycast(from, Vector3.down, out RaycastHit hit, 50f, ~0, QueryTriggerInteraction.Ignore))
                return hit.point.y;

            return 0f;
        }
    }
}
