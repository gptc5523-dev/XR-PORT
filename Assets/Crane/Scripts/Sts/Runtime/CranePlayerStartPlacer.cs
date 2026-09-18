using UnityEngine;
using Unity.Netcode;   // 접속 완료 후 재배치용(Netcode 참조 가능)

namespace AIXRCrane.Crane.Sts
{
    /// <summary>시작 위치 배치 — 씬 진입·네트워크 접속 직후 로컬 리그(XR Origin)를 부두 안 시작 지점으로 옮긴다.
    /// 마커(CranePlayerStartPoint) 우선, 없으면 저장 좌표. forceInsideQuay 로 항상 부두 안으로 클램프.</summary>
    [AddComponentMenu("AI-XR Crane/STS Crane/Crane Player Start Placer")]
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
        [Tooltip("켜면 걷는 중에도 항상 부두 안에 머문다(안벽 밖·바다 위·허공 진입 차단). 끄면 자유 비행 점검 가능.")]
        [SerializeField] bool keepOnQuay = true;
        [SerializeField] bool debugLog = true;

        const string QuayName = StsPartNames.QuayGround;

        bool pending = true;        // 처리할 배치 요청이 남았는가(시작 시 1건).
        int attempts;               // 현재 요청에 대한 재시도 프레임 수.
        bool warned;

        // 이탈 차단(LateUpdate) 캐시 — 매 프레임 GameObject.Find/FindAnyObjectByType 을 돌지 않게 보관.
        Bounds landCache; bool haveLand;   // 땅은 런타임에 안 움직인다 — 한 번 구하면 끝
        AIXRCrane.Crane.Flat.FlatPlayerRig flatCache;
        float nextRefind;           // 캐시가 비었을 때만 이 시각 이후 재탐색.
        bool clampActive;           // 이탈 차단 중 — 엣지에서만 로그(매 프레임 스팸 방지).

        bool netHooked;             // NetworkManager 접속 콜백 구독 완료.
        bool replacedAfterConnect;  // 접속 후 재배치 1회 완료(중복 방지).
        bool placingAfterConnect;   // 다음 배치가 '접속 후 재배치'인지(QA START/replace 라인 구분용).

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoSpawn() => CraneHud.EnsureSpawned<CranePlayerStartPlacer>("PlayerStartPlacer");

        // 부두 밖 이탈 차단 — 스폰 때만 걸던 클램프를 매 프레임 적용. 콜라이더가 아니라 이 경계로 막는다
        // (리그는 CharacterController/콜라이더를 안 쓴다). 운전실 시점(트롤리가 바다 위로 나감)은 제외.
        void LateUpdate()
        {
            if (!keepOnQuay) return;

            var cam = Camera.main; if (cam == null) return;
            Transform rig = cam.transform.root; if (rig == null) return;

            RefreshCaches();
            if (InCabView()) { clampActive = false; return; }
            if (!haveLand) return;

            Bounds b = landCache;
            Vector3 p = rig.position;
            Vector3 c = ClampToBounds(p, b, quayEdgeInset);
            // 데크 아래(안벽 속·물속)로도 못 내려간다. 위로는 자유 — 운전실·점검 시점 상승을 막지 않는다.
            c.y = Mathf.Max(p.y, b.max.y + floorClearance);
            if ((c - p).sqrMagnitude <= 1e-10f) { clampActive = false; return; }

            rig.position = c;
            if (debugLog && !clampActive)
                QaLog.Info("BOUNDS", "clamp", $"rig={QaLog.V(p)} -> {QaLog.V(c)} " +
                    $"quay=x({b.min.x:F2}..{b.max.x:F2}) z({b.min.z:F2}..{b.max.z:F2}) inset={QaLog.F(quayEdgeInset)}");
            clampActive = true;
        }

        // 운전실 시점(평면·VR)인가 — 그동안은 시점이 트롤리를 따라 바다 위로 나가므로 클램프를 쉰다.
        //   VR 은 켜진 컨트롤러(Active)를 본다 — 크레인이 여러 대면 조종기를 받는 한 대만 켜져 있다.
        bool InCabView()
            => (StsCraneVRController.Active != null && StsCraneVRController.Active.CabView) || (flatCache != null && flatCache.MovementLocked);

        // 비어 있는 캐시만 1초에 한 번 다시 찾는다(리그·크레인·부두가 늦게 떠도 붙는다).
        void RefreshCaches()
        {
            if (haveLand && flatCache != null) return;
            if (Time.unscaledTime < nextRefind) return;
            nextRefind = Time.unscaledTime + 1f;
            if (!haveLand) haveLand = TryGetLand(out landCache);
            if (flatCache == null) flatCache = FindAnyObjectByType<AIXRCrane.Crane.Flat.FlatPlayerRig>();
        }

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

        // 네트워크 접속 후 1회 재배치
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

        // 실제 배치
        // 성공 시 true, 아직 준비 안 됨(다음 프레임 재시도)이면 false.
        bool TryPlaceRig()
        {
            var cam = Camera.main;
            if (cam == null) return false;                // 카메라 아직 — 다음 프레임.
            Transform rig = cam.transform.root;           // XR Origin 리그 루트.
            if (rig == null) return false;

            float rigYBefore = rig.position.y;            // QA: 재배치 전 높이(접속 후 원점 방치 복구 확인용)
            // 시작 XZ·바라보는 방향 결정 — 계산은 TryComputeSpawn() 한 곳에만 둔다.
            // 에디터 표식(QuayPartsPlacer.PlaceSpawnPawn)도 같은 식을 읽어야 자리가 일치한다.
            if (!TryComputeSpawn(out Vector3 xz, out Vector3 faceDir, out bool hasLand, out Bounds land,
                                 out var marker, forceInsideQuay, quayEdgeInset, rig.forward))
                return false;                             // 마커도 부두도 아직 없음 — 재시도(없으면 maxAttempts에서 포기).

            // Y(높이): 마커 값 무시하고 항상 걷는 면 윗면에 발이 닿게. 부두를 못 찾으면 레이캐스트→0 폴백.
            float floorY = hasLand ? land.max.y : ResolveFloorYRaycast(xz);
            Vector3 pos = new Vector3(xz.x, floorY + floorClearance, xz.z);

            rig.SetPositionAndRotation(pos, Quaternion.LookRotation(faceDir, Vector3.up));
            if (debugLog)
            {
                // 좌표는 실척(m)을 앞에 찍는다 — 모델 단위(1u=24m)는 숫자가 눌려 사람이 못 읽는다.
                Vector3 real = pos * StsConfig.InvModelScale;
                Debug.Log($"[PlayerStartPlacer] 시작 배치 — 실척 X {real.x:F2}m · Z {real.z:F2}m (모델 {pos.x:F4}, {pos.y:F4}, {pos.z:F4}), " +
                          $"facing {faceDir}, 기준={(marker != null ? "마커" : "저장좌표")}, 부두클램프={(forceInsideQuay && hasLand)}.");
            }

            // QA 콘솔 판정(문서/QA_테스트시나리오.md 그룹 A) — 걷는 면 선택과 발 높이가 구조물 꼭대기 위가 아닌지 확인.
            float structureTop = QuayStructureTopY();
            bool flatQuay = (structureTop - floorY) < 0.1f;        // 레일/구조물이 없거나 낮은 평탄 부두 — 거대 가드 완화
            if (hasLand)
                QaLog.Info("START", "surface",
                    $"chosen=land x({land.min.x:F2}..{land.max.x:F2}) z({land.min.z:F2}..{land.max.z:F2}) chosenMaxY={QaLog.F(floorY)} structureTopY={QaLog.F(structureTop)} giantGap={QaLog.F(structureTop - floorY)}");
            bool onFloor = Mathf.Abs(pos.y - floorY) <= floorClearance + 1e-3f;
            bool notGiant = flatQuay || pos.y < structureTop - 0.1f;   // 구조물 꼭대기(≈0.22)에 서면 거대증상 재발 → FAIL
            QaLog.Check("START", "place", onFloor && notGiant,
                $"basis={(marker != null ? "marker" : "portStart")} floorY={QaLog.F(floorY)} rigY={QaLog.F(pos.y)} " +
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

        /// <summary>기본 부두 가장자리 인셋(m·모델 단위) — 인스펙터 기본값과 에디터 표식이 같은 값을 쓴다.</summary>
        public const float DefaultQuayEdgeInset = 0.1f;

        /// <summary>시작 XZ·바라보는 방향 — 런타임과 에디터 표식이 공유하는 단 하나의 계산.
        /// 순서: 마커 → 저장 좌표(PortConfig) → 클램프. 반환은 클램프까지 끝난 최종 좌표, Y 는 안 정한다.</summary>
        public static bool TryComputeSpawn(out Vector3 xz, out Vector3 faceDir, out bool hasLand, out Bounds land,
                                           out CranePlayerStartPoint marker, bool forceInsideQuay = true,
                                           float inset = DefaultQuayEdgeInset, Vector3 fallbackForward = default)
        {
            xz = default; faceDir = Vector3.right;
            marker = FindMarker();
            hasLand = TryGetLand(out land);

            if (marker != null)
            {
                Vector3 mp = marker.transform.position;
                xz = new Vector3(mp.x, 0f, mp.z);
                Vector3 f = marker.transform.forward; f.y = 0f;
                faceDir = f.sqrMagnitude > 1e-4f ? f.normalized
                        : (fallbackForward.sqrMagnitude > 1e-4f ? fallbackForward.normalized : Vector3.right);
            }
            else if (hasLand)
            {
                // 마커 없음 → 저장된 시작점(PortConfig) — STS 레일 사이 · 선석 중앙 · 바다 쪽.
                xz = new Vector3(PortConfig.PlayerStartXMeters, 0f, PortConfig.PlayerStartZMeters) * StsConfig.ModelScale;
                faceDir = Vector3.right;
            }
            else
            {
                return false;                             // 마커도 부두도 아직 없음.
            }

            // 항상 부두 '안'으로 — 마커가 없거나 부두 밖이어도 걷는 면 XZ 범위로 클램프.
            Vector3 xzBeforeClamp = xz;
            if (forceInsideQuay && hasLand)
            {
                xz = ClampToBounds(xz, land, inset);
                if ((xz - xzBeforeClamp).sqrMagnitude > 1e-8f)   // S-START-4: 부두 밖 → 안으로 끌어들였을 때만
                    QaLog.Info("START", "clamp",
                        $"xz_before={QaLog.V(xzBeforeClamp)} xz_after={QaLog.V(xz)} inset={QaLog.F(inset)} clamped=true");
            }
            return true;
        }

        /// <summary>에디터 표식용 간편 진입점 — 최종 시작 XZ 만 필요할 때.</summary>
        public static bool TryComputeSpawnXZ(out Vector3 xz) =>
            TryComputeSpawn(out xz, out _, out _, out _, out _);

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

        // 걷는 땅 = 데크 아래로 1m 넘게 뻗고 짧은 변 ≥2m 인 렌더러의 합집합(케이슨+야드 포장, 윗면 y=0).
        // 레일·연석·컨테이너처럼 얹힌 것은 탈락. 바다는 조상 이름으로 제외(물 위 걷기 방지).
        internal static bool TryGetLand(out Bounds land)
        {
            land = default;
            var quay = GameObject.Find(QuayName);
            if (quay == null) return false;
            float belowDeck = quay.transform.position.y - 1f * StsConfig.ModelScale, minWide = 2f * StsConfig.ModelScale;
            bool any = false;
            foreach (var r in quay.GetComponentsInChildren<Renderer>())
            {
                Bounds rb = r.bounds;
                if (rb.min.y > belowDeck || Mathf.Min(rb.size.x, rb.size.z) < minWide || UnderSea(r.transform, quay.transform)) continue;
                if (any) land.Encapsulate(r.bounds); else { land = r.bounds; any = true; }
            }
            return any;
        }

        static bool UnderSea(Transform t, Transform stop)
        {
            for (; t != null && t != stop; t = t.parent)
                if (StsPartNames.IsSeaName(t.name)) return true;
            return false;
        }

        // 부두 구조물 전체의 최고 윗면 y(레일 등 포함) — QA 전용 거대증상 가드 기준. 배치엔 안 쓴다.
        static float QuayStructureTopY()
        {
            var quay = GameObject.Find(QuayName);
            if (quay == null) return 0f;
            float top = float.MinValue;
            foreach (var r in quay.GetComponentsInChildren<Renderer>())
                if (r.bounds.max.y > top) top = r.bounds.max.y;
            return top > float.MinValue ? top : 0f;
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
