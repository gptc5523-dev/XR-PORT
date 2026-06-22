using UnityEngine;
using Unity.Netcode;   // 접속 완료 후 1회 재배치(클라이언트가 원점에 방치되는 것 방지). Assembly-CSharp가 Netcode를 참조하므로 사용 가능.

namespace Container.Crane.Sts
{
    /// <summary>
    /// 시작 위치 배치 — 씬 진입 시(그리고 네트워크 접속 직후) 로컬 플레이어 리그(XR Origin)를 시작 지점으로 옮긴다.
    ///
    ///   ★ 목표: 호스트가 어디에 있든, 호스트·참가자 '모두' 항상 Quay_Ground(부두 걷는 면) '안'에서 시작한다.
    ///     - 시작 마커(CranePlayerStartPoint)가 있으면 그 위치·방향을 쓴다(디자이너 지정).
    ///     - 마커가 없거나 부두 밖이어도 forceInsideQuay가 켜져 있으면 걷는 면 XZ 범위로 끌어들인다(클램프).
    ///     - 마커가 아예 없으면 부두 중앙에 놓고 크레인을 바라보게 한다.
    ///
    ///   ★ 네트워크 보정: 클라이언트는 접속 동기화 과정에서 리그가 원점(0,0,0)에 방치되는 경우가 있다
    ///     (= 관전자가 '호스트 자리까지 걸어가야 크레인이 보이던' 증상). 그래서 로컬 접속이 완료되면
    ///     한 번 더 부두 안으로 재배치한다.
    ///
    ///   ※ 위치만 만진다 — 리그 스케일(CranePlayerRigScale)·카메라 오프셋(CraneViewHeightAdjuster)과 독립.
    /// 마커는 'Container > Create Player Start Point' 메뉴로 만들어 씬에 두면 된다.
    /// 씬에 안 붙여도 [RuntimeInitializeOnLoadMethod]로 자동 스폰.
    /// </summary>
    [AddComponentMenu("Container/STS Crane/Crane Player Start Placer")]
    [DisallowMultipleComponent]
    public sealed class CranePlayerStartPlacer : MonoBehaviour
    {
        [Tooltip("바닥 위로 띄울 여유(m). 0이면 부두 바닥에 딱 붙음.")]
        [SerializeField] float floorClearance = 0f;
        [Tooltip("리그/마커/부두가 늦게 떠도 될 때까지 재시도할 최대 프레임 수.")]
        [SerializeField] int maxAttempts = 300;
        [Tooltip("켜면 호스트/참가자 모두 항상 Quay_Ground 걷는 면 '안'에서 시작(마커가 없거나 부두 밖이어도 안으로 끌어들임). 끄면 마커 있을 때만 배치.")]
        [SerializeField] bool forceInsideQuay = true;
        [Tooltip("부두 가장자리에서 안쪽으로 들이는 여유(m). 가장자리에 딱 붙어 떨어지는 것 방지.")]
        [SerializeField] float quayEdgeInset = 0.1f;
        [SerializeField] bool debugLog = true;

        const string QuayName = StsPartNames.QuayGround;

        bool pending = true;        // 처리할 배치 요청이 남았는가(시작 시 1건).
        int attempts;               // 현재 요청에 대한 재시도 프레임 수.
        bool warned;

        bool netHooked;             // NetworkManager 접속 콜백 구독 완료.
        bool replacedAfterConnect;  // 접속 후 재배치 1회 완료(중복 방지).
        bool placingAfterConnect;   // 다음 배치가 '접속 후 재배치'인지(QA START/replace 라인 구분용).

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoSpawn() => CraneHud.EnsureSpawned<CranePlayerStartPlacer>("PlayerStartPlacer");

        void Update()
        {
            EnsureNetHook();   // NetworkManager가 뜨면 접속 콜백 구독(접속 후 재배치용). 콜백이 늦게 올 수 있어 Update는 끄지 않는다.

            if (!pending) return;   // 처리할 배치 없음 — 사실상 무비용.

            if (TryPlaceRig()) { pending = false; attempts = 0; }
            else if (++attempts > maxAttempts)
            {
                pending = false; attempts = 0;
                if (debugLog && !warned)
                {
                    Debug.LogWarning("[PlayerStartPlacer] 시작 지점을 못 잡음 — 마커(CranePlayerStartPoint)도 " +
                        $"'{QuayName}'도 못 찾았습니다. 씬에 배치된 위치 그대로 시작합니다.");
                    warned = true;
                }
            }
        }

        // ───────── 네트워크 접속 후 1회 재배치 ─────────
        void EnsureNetHook()
        {
            if (netHooked) return;
            var nm = NetworkManager.Singleton;
            if (nm == null) return;                       // 아직 NetworkManager 미생성(단독 플레이 씬이면 영영 null) — 매 프레임 싼 널체크.
            nm.OnClientConnectedCallback += OnClientConnected;
            netHooked = true;
        }

        // 내(로컬) 접속이 완료되면 한 번 더 부두 안으로 보낸다 — 클라이언트가 접속 중 원점에 방치되는 경우 복구.
        void OnClientConnected(ulong clientId)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || replacedAfterConnect) return;
            if (clientId != nm.LocalClientId) return;     // 남의 접속은 무시 — 내 리그만 재배치.
            replacedAfterConnect = true;
            placingAfterConnect = true;   // 다음 TryPlaceRig가 '접속 후 재배치'임을 표시(QA START/replace 판정용)
            pending = true; attempts = 0;
            if (debugLog) Debug.Log("[PlayerStartPlacer] 네트워크 접속 완료 — 시작 위치를 부두 안으로 재배치합니다.");
        }

        void OnDestroy()
        {
            var nm = NetworkManager.Singleton;
            if (nm != null) nm.OnClientConnectedCallback -= OnClientConnected;
        }

        // ───────── 실제 배치 ─────────
        // 성공 시 true, 아직 준비 안 됨(다음 프레임 재시도)이면 false.
        bool TryPlaceRig()
        {
            var cam = Camera.main;
            if (cam == null) return false;                // 카메라 아직 — 다음 프레임.
            Transform rig = cam.transform.root;           // XR Origin 리그 루트.
            if (rig == null) return false;

            float rigYBefore = rig.position.y;            // QA: 재배치 전 높이(접속 후 원점 방치 복구 확인용)
            var marker = FindMarker();
            Renderer quay = GetQuaySurface();

            // 시작 XZ·바라보는 방향 결정.
            Vector3 xz; Vector3 faceDir;
            if (marker != null)
            {
                Vector3 mp = marker.transform.position;
                xz = new Vector3(mp.x, 0f, mp.z);
                Vector3 f = marker.transform.forward; f.y = 0f;
                faceDir = f.sqrMagnitude > 1e-4f ? f.normalized : rig.forward;
            }
            else if (quay != null)
            {
                Vector3 c = quay.bounds.center;           // 마커 없음 → 부두 중앙.
                xz = new Vector3(c.x, 0f, c.z);
                faceDir = FaceTowardCrane(xz, rig.forward);
            }
            else
            {
                return false;                             // 마커도 부두도 아직 없음 — 재시도(없으면 maxAttempts에서 포기).
            }

            // 항상 부두 '안'으로 — 마커가 없거나 부두 밖이어도 걷는 면 XZ 범위로 클램프.
            Vector3 xzBeforeClamp = xz;
            bool clampApplied = false;
            if (forceInsideQuay && quay != null)
            {
                xz = ClampToBounds(xz, quay.bounds, quayEdgeInset);
                clampApplied = (xz - xzBeforeClamp).sqrMagnitude > 1e-8f;
                if (clampApplied)   // S-START-4: 부두 밖 → 안으로 끌어들였을 때만
                    QaLog.Info("START", "clamp",
                        $"xz_before={QaLog.V(xzBeforeClamp)} xz_after={QaLog.V(xz)} inset={QaLog.F(quayEdgeInset)} clamped=true");
            }

            // Y(높이): 마커 값 무시하고 항상 걷는 면 윗면에 발이 닿게. 부두를 못 찾으면 레이캐스트→0 폴백.
            float floorY = quay != null ? quay.bounds.max.y : ResolveFloorYRaycast(xz);
            Vector3 pos = new Vector3(xz.x, floorY + floorClearance, xz.z);

            rig.SetPositionAndRotation(pos, Quaternion.LookRotation(faceDir, Vector3.up));
            if (debugLog)
                Debug.Log($"[PlayerStartPlacer] 시작 배치 — pos {pos}, facing {faceDir}, " +
                          $"기준={(marker != null ? "마커" : "부두중앙")}, 부두클램프={(forceInsideQuay && quay != null)}.");

            // ───── QA 콘솔 판정(문서/QA_테스트시나리오.md 그룹 A) ─────
            //   S-START-2: 걷는 면(최대 수평면적 렌더러) 선택 — 부두 '구조물 꼭대기'(레일 등)와 대비해 보고.
            //   S-START-1: 발 높이(rigY)가 걷는 면 윗면(floorY)에 닿고, 거대증상 기준(구조물 꼭대기) 위가 아님.
            float structureTop = QuayStructureTopY();
            bool flatQuay = (structureTop - floorY) < 0.1f;        // 레일/구조물이 없거나 낮은 평탄 부두 — 거대 가드 완화
            if (quay != null)
                QaLog.Info("START", "surface",
                    $"chosen={quay.name} chosenMaxY={QaLog.F(floorY)} structureTopY={QaLog.F(structureTop)} giantGap={QaLog.F(structureTop - floorY)}");
            bool onFloor = Mathf.Abs(pos.y - floorY) <= floorClearance + 1e-3f;
            bool notGiant = flatQuay || pos.y < structureTop - 0.1f;   // 구조물 꼭대기(≈0.22)에 서면 거대증상 재발 → FAIL
            QaLog.Check("START", "place", onFloor && notGiant,
                $"basis={(marker != null ? "marker" : "quayCenter")} floorY={QaLog.F(floorY)} rigY={QaLog.F(pos.y)} " +
                $"structureTopY={QaLog.F(structureTop)} clearance={QaLog.F(floorClearance)} onFloor={onFloor} notGiant={notGiant}");

            // S-START-3: 네트워크 접속 후 재배치였다면 — 원점(0,0,0) 방치에서 부두 안으로 복귀했는지.
            if (placingAfterConnect)
            {
                placingAfterConnect = false;
                QaLog.Check("START", "replace", onFloor && notGiant,
                    $"reason=connected rigY_before={QaLog.F(rigYBefore)} rigY_after={QaLog.F(pos.y)} floorY={QaLog.F(floorY)}");
            }
            return true;
        }

        static CranePlayerStartPoint FindMarker()
        {
            // 비활성 객체까지 포함(꺼둔 마커도 인식). 타입으로 못 찾으면 이름("PlayerStartPoint")으로도 시도.
            var marker = FindAnyObjectByType<CranePlayerStartPoint>(FindObjectsInactive.Include);
            if (marker == null)
            {
                var byName = GameObject.Find(StsPartNames.PlayerStartPoint);
                if (byName != null) marker = byName.GetComponent<CranePlayerStartPoint>();
            }
            return marker;
        }

        // 부두에서 '걷는 면'(수평 면적이 가장 큰 렌더러 = 아스팔트 슬래브)을 고른다. 레일/차선 같은 가는 것은 제외.
        //   ★ 부두 '전체' bounds.max.y는 레일/구조물 꼭대기(≈0.22m)라, 거기에 세우면 컨테이너를 5m 위에서 내려다보는
        //     '거대' 증상이 났다. 걷는 면 렌더러만 골라 그 윗면(아스팔트 ≈0)을 쓴다.
        static Renderer GetQuaySurface()
        {
            var quay = GameObject.Find(QuayName);
            if (quay == null) return null;
            Renderer ground = null; float bestArea = 0f;
            foreach (var r in quay.GetComponentsInChildren<Renderer>())
            {
                Vector3 e = r.bounds.size;
                float area = e.x * e.z;                    // 수평 면적 — 아스팔트 슬래브가 압도적으로 큼.
                if (area > bestArea) { bestArea = area; ground = r; }
            }
            return ground;
        }

        // 부두 '구조물 전체'의 최고 윗면 y(레일·차선 등 포함) — 걷는 면(아스팔트 ≈0)과 대비.
        //   QA 전용: 시작 높이가 이 구조물 꼭대기(≈0.22)에 서면 '거대증상' 재발이므로 가드 기준으로 쓴다.
        //   (배치 로직 자체는 GetQuaySurface의 '걷는 면'만 사용 — 이 값은 판정에만 쓰고 배치엔 안 쓴다.)
        static float QuayStructureTopY()
        {
            var quay = GameObject.Find(QuayName);
            if (quay == null) return 0f;
            float top = float.MinValue;
            foreach (var r in quay.GetComponentsInChildren<Renderer>())
                if (r.bounds.max.y > top) top = r.bounds.max.y;
            return top > float.MinValue ? top : 0f;
        }

        // 마커가 없을 때 크레인 쪽을 바라보게(수평). 크레인을 못 찾으면 기존 방향 유지.
        static Vector3 FaceTowardCrane(Vector3 fromXZ, Vector3 fallback)
        {
            var crane = FindAnyObjectByType<StsCrane>();
            if (crane == null) return fallback;
            Vector3 d = crane.transform.position - fromXZ; d.y = 0f;
            return d.sqrMagnitude > 1e-4f ? d.normalized : fallback;
        }

        static Vector3 ClampToBounds(Vector3 p, Bounds b, float inset)
        {
            float minX = b.min.x + inset, maxX = b.max.x - inset;
            float minZ = b.min.z + inset, maxZ = b.max.z - inset;
            if (minX > maxX) minX = maxX = b.center.x;     // 인셋이 폭보다 크면 중앙.
            if (minZ > maxZ) minZ = maxZ = b.center.z;
            return new Vector3(Mathf.Clamp(p.x, minX, maxX), p.y, Mathf.Clamp(p.z, minZ, maxZ));
        }

        static float ResolveFloorYRaycast(Vector3 at)
        {
            Vector3 from = new Vector3(at.x, at.y + 5f, at.z);
            if (Physics.Raycast(from, Vector3.down, out RaycastHit hit, 50f, ~0, QueryTriggerInteraction.Ignore))
                return hit.point.y;
            return 0f;
        }
    }
}
