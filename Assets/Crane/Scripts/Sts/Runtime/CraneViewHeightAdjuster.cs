using UnityEngine;
using UnityEngine.XR;   // InputDevices — Quest 컨트롤러 직접 읽기(조종 컨트롤러와 동일 방식)

namespace Container.Crane.Sts
{
    /// <summary>
    /// 시점 눈높이 조절(앉기/낮추기) — 오른손 검지 트리거를 누른 채 왼손 스틱 ↑↓.
    ///   카메라 오프셋(XR Origin > Camera Offset)의 Y만 움직여 중력/CharacterController와 안 싸운다
    ///   (리그 월드 위치가 아니라 오프셋이라 도로 끌어올려지지 않음).
    ///
    /// StsCraneVRController에서 분리한 '단일 소스' — 호스트(조종자)·관전자 모두 동일하게 사용한다.
    ///   - 관전자는 StsCraneVRController가 꺼져 있어도 이 컴포넌트로 높이를 조절할 수 있다.
    ///   - StsCraneVRController는 HeightHold(트리거 홀드 여부)만 참조해, 조절 중엔 왼손 스틱을
    ///     높이 전용으로 양보(호이스트/갠트리 입력 무효화)하고 걷기를 잠시 멈춘다.
    /// 씬에 안 붙여도 [RuntimeInitializeOnLoadMethod]로 자동 스폰.
    /// </summary>
    [AddComponentMenu("Container/STS Crane/Crane View Height Adjuster")]
    [DisallowMultipleComponent]
    public sealed class CraneViewHeightAdjuster : MonoBehaviour
    {
        [Tooltip("눈높이 변경 속도(체감 m/s). 스틱 최대일 때. 크레인 정상(~56m)까지 오르므로 빠르게 — 살짝 밀면 비례해 느려져 미세 조절도 됨. 리그 1/24라도 오프셋=체감 m라 그대로 적용.")]
        [SerializeField] float viewHeightSpeed = 8f;
        [Tooltip("기본 눈높이 대비 최저 오프셋(체감 m, 음수=앉기/낮추기). 바닥 클램프가 별도로 더 내려가는 걸 막음.")]
        [SerializeField] float viewHeightMin = -1.2f;
        [Tooltip("기본 눈높이 대비 최고 오프셋(체감 m). 크레인 최상단(피뢰침 ~56m) 위로 올라가 내려다볼 수 있게 +60m.")]
        [SerializeField] float viewHeightMax = 60f;
        [Tooltip("이 값 이상 당기면 트리거를 '누른 것'으로 인정(0~1).")]
        [SerializeField, Range(0.1f, 0.95f)] float triggerHoldThreshold = 0.6f;
        [Tooltip("바닥 월드 Y 폴백(부두 바닥 윗면). 부두(Quay_Ground)를 못 찾을 때만 사용 — 평소엔 부두 걷는면 윗면에서 동적 산출(CranePlayerStartPlacer와 동일 방식).")]
        [SerializeField] float floorWorldY = 0f;
        [Tooltip("바닥 위로 최소 이 높이(체감 m)까지만 내려감 — 바닥 밑으로/바닥 관통(니어클립) 방지. 0.8≈낮은 쪼그림.")]
        [SerializeField] float minEyeAboveFloor = 0.8f;

        /// <summary>지금 오른손 트리거를 임계 이상 당겨 '높이 조절 중'인지.
        /// StsCraneVRController가 입력 양보(걷기 정지·왼손 스틱 무효화) 판단에 참조.</summary>
        public bool HeightHold { get; private set; }

        Transform camT;               // 카메라 Transform(바닥 클램프용 월드 Y 읽기)
        Transform cameraOffset;       // 카메라의 부모(XR Origin > Camera Offset) — 여기를 조절
        Vector3 baseCameraOffsetLocal;// 시작 시 카메라 오프셋 '로컬' 위치(기준 — 리그가 걸어 이동해도 안 흔들림)
        bool cameraOffsetCached;
        float viewHeightOffset;       // 기준 대비 현재 눈높이 오프셋(m, 월드 수직)
        float resolvedFloorY;         // 부두 걷는면 윗면에서 동적 산출한 바닥 월드Y(StartPlacer와 동일 방식). 미해결이면 폴백 사용.
        bool floorYResolved;          // 위 값이 부두에서 확정됐는지(못 찾으면 floorWorldY 폴백 유지, 매 프레임 재시도)
        int clampLogFrames;           // 바닥 클램프 로그 스로틀용

        const string QuayName = StsPartNames.QuayGround;   // CranePlayerStartPlacer와 동일 — 부두 걷는면 탐색 기준
        readonly System.Collections.Generic.List<Behaviour> suppressedLoco = new System.Collections.Generic.List<Behaviour>();
        bool locoSuppressed;          // 관전자: 높이조절 중 XR 로코모션을 꺼둔 상태인지

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoSpawn() => CraneHud.EnsureSpawned<CraneViewHeightAdjuster>("ViewHeightAdjuster");

        void Update()
        {
            var right = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            float rTrig = 0f;
            if (right.isValid) right.TryGetFeatureValue(CommonUsages.trigger, out rTrig);
            HeightHold = rTrig > triggerHoldThreshold;

            // 관전자 '대각선 수직이동' 수정: 높이조절 중엔 XR 로코모션(ContinuousMove)을 꺼서 왼스틱이 '수직만' 움직이게.
            //   호스트는 StsCraneVRController.EnforceLocomotion이 이미 같은 일을 하지만(ViewHeightActive→로코 off),
            //   관전자는 그 컨트롤러가 꺼져 있어 로코모션이 살아 → 왼스틱이 수직(이 컴포넌트)+수평(XR 이동) 동시 적용 → 대각선.
            if (HeightHold || locoSuppressed) UpdateLocomotionSuppression(HeightHold);

            if (!HeightHold) return;

            var left = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            Vector2 ls = Vector2.zero;
            if (left.isValid) left.TryGetFeatureValue(CommonUsages.primary2DAxis, out ls);
            AdjustViewHeight(ls.y);
        }

        void AdjustViewHeight(float stickY)
        {
            EnsureCameraOffset();
            if (cameraOffset == null || camT == null) return;   // 못 찾음(다음 프레임 재시도/없으면 무동작)

            float newOffset = Mathf.Clamp(
                viewHeightOffset + stickY * viewHeightSpeed * Time.deltaTime, viewHeightMin, viewHeightMax);
            ApplyCameraOffsetY(newOffset);

            // 바닥 밑으로/바닥 관통 방지. 눈이 바닥 위로 다음 둘 중 '큰' 만큼 떨어져 있게 한다:
            //   (a) minEyeAboveFloor(체감 m) × rigScale  — 디자이너 지정 최저 눈높이
            //   (b) nearClip × 4 (월드)                  — 니어클립 안쪽에 바닥이 들면 바닥이 잘려 '뚫려' 보이므로
            //                                              그보다 확실히 위에서 멈춤. ((a)만 쓰면 1/24에서 ~0.004m로
            //                                              너무 낮아 바닥 관통하던 버그.)
            //   below(월드)는 ÷rigScale로 로컬/체감 오프셋으로 환산해 더한다.
            float rigScale = cameraOffset.parent != null ? cameraOffset.parent.lossyScale.y : 1f;
            float nearClip = camT.TryGetComponent(out Camera camC) ? camC.nearClipPlane : 0.01f;
            float worldClear = Mathf.Max(minEyeAboveFloor * rigScale, nearClip * 4f);
            float floorY = ResolveFloorY();   // 부두 걷는면 윗면에서 동적 산출(StartPlacer와 정합), 못 찾으면 floorWorldY 폴백.
            float floorMinY = floorY + worldClear;
            float below = floorMinY - camT.position.y;
            if (below > 0f)
            {
                newOffset += below / Mathf.Max(rigScale, 1e-4f); ApplyCameraOffsetY(newOffset);
                if ((clampLogFrames++ % 60) == 0)
                    Debug.Log($"[ViewHeight] 바닥 클램프 작동 — 눈 월드Y={camT.position.y:0.####} → 최소 {floorMinY:0.####} " +
                              $"(floorY={floorY:0.####}{(floorYResolved ? "(부두)" : "(폴백)")}, 여유 월드={worldClear:0.####}, nearClip={nearClip:0.###}, rigScale={rigScale:0.####}).");
            }

            viewHeightOffset = newOffset;
        }

        void ApplyCameraOffsetY(float offset)
        {
            Transform parent = cameraOffset.parent;
            if (parent == null) { cameraOffset.localPosition = baseCameraOffsetLocal + Vector3.up * offset; return; }

            // 카메라 오프셋을 '월드 수직'으로만 이동 — 리그가 기울었거나 비균일 스케일이어도 뒤/옆으로 안 샌다.
            //   ('관전자가 뒤로 가며 내려가던' 버그: 기존 InverseTransformDirection+로컬더하기가 리그 회전×스케일 조합에서
            //    수평 성분을 섞었음.) 기준 로컬을 월드로 환산 → 월드 up으로 offset×리그수직스케일 만큼 이동 → 다시 로컬로.
            //   기준(base)을 매번 parent에서 월드로 재계산하므로 리그가 걸어 이동해도 안 흔들린다.
            float vScale = Mathf.Abs(parent.lossyScale.y) > 1e-6f ? parent.lossyScale.y : 1f;
            Vector3 baseWorld = parent.TransformPoint(baseCameraOffsetLocal);
            Vector3 desiredWorld = baseWorld + Vector3.up * (offset * vScale);
            cameraOffset.localPosition = parent.InverseTransformPoint(desiredWorld);
        }

        void EnsureCameraOffset()
        {
            if (cameraOffsetCached) return;
            var cam = Camera.main;
            if (cam == null) return;                  // 카메라 아직 없음 — 다음 프레임 재시도
            camT = cam.transform;
            cameraOffset = camT.parent;               // XR Origin > Camera Offset > Main Camera
            if (cameraOffset == null) return;         // 부모(Camera Offset) 아직 없음 — 캐시하지 말고 다음 프레임 재시도
            baseCameraOffsetLocal = cameraOffset.localPosition;   // 로컬 기준점(고정)
            cameraOffsetCached = true;                // 제대로 찾았을 때만 캐시 — 관전자 등 리그가 늦게 뜨는 경우 대응

            // 진단: 리그(부모) 회전·스케일 — '뒤로 가며 내려감'의 원인(기울기/비균일 스케일) 확인용.
            Vector3 e = cameraOffset.parent != null ? cameraOffset.parent.rotation.eulerAngles : Vector3.zero;
            Vector3 ls = cameraOffset.parent != null ? cameraOffset.parent.lossyScale : Vector3.one;
            Debug.Log($"[ViewHeight] 카메라오프셋 캐시 — 부모 '{(cameraOffset.parent != null ? cameraOffset.parent.name : "null")}' " +
                      $"euler=({e.x:0.#},{e.y:0.#},{e.z:0.#}) lossyScale=({ls.x:0.####},{ls.y:0.####},{ls.z:0.####}). " +
                      $"※ euler x/z≠0(기울기) 또는 스케일 비균일이면 그게 뒤로밀림 원인.");
        }

        // 바닥 월드Y를 부두 걷는 땅 윗면에서 동적 산출 — CranePlayerStartPlacer.TryGetLand 한 곳을 같이 쓴다
        //   (예전엔 같은 휴리스틱을 복사해 두었다 — 한쪽만 고치면 시작 높이와 눈높이 기준이 갈라진다).
        //   한 번 부두에서 확정하면 캐시. 못 찾으면(부두 미생성/단독 씬) SerializeField floorWorldY를 폴백으로 두고 다음 프레임 재시도.
        float ResolveFloorY()
        {
            if (floorYResolved) return resolvedFloorY;
            if (!CranePlayerStartPlacer.TryGetLand(out Bounds land)) return floorWorldY;   // 부두 못 찾음 — 폴백(재시도).
            resolvedFloorY = land.max.y;
            floorYResolved = true;
            Debug.Log($"[ViewHeight] 바닥 월드Y 동적 산출 — 부두 '{QuayName}' 걷는면 윗면 y={resolvedFloorY:0.####} (floorWorldY 폴백={floorWorldY}).");
            return resolvedFloorY;
        }

        // 관전자에서만 높이조절 중 XR 로코모션을 끈다(호스트는 StsCraneVRController가 관리하므로 손대지 않음).
        void UpdateLocomotionSuppression(bool wantSuppress)
        {
            var ctrl = FindAnyObjectByType<StsCraneVRController>();
            bool hostManages = ctrl != null && ctrl.enabled;     // 호스트 → 그쪽이 로코모션 관리
            if (hostManages) { if (locoSuppressed) RestoreLocomotion(); return; }

            if (wantSuppress) { if (!locoSuppressed) SuppressLocomotion(); }
            else if (locoSuppressed) RestoreLocomotion();
        }

        void SuppressLocomotion()
        {
            locoSuppressed = true;
            var t = LocoProviderType;
            if (t == null) return;
            suppressedLoco.Clear();
            foreach (var o in FindObjectsByType(t, FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (o is Behaviour b && b.enabled) { b.enabled = false; suppressedLoco.Add(b); }
            if (suppressedLoco.Count > 0)
                Debug.Log($"[ViewHeight] 관전자 — 높이조절 중 XR 로코모션 {suppressedLoco.Count}개 끔(수평 이동 방지 → 수직만).");
        }

        void RestoreLocomotion()
        {
            foreach (var b in suppressedLoco) if (b != null) b.enabled = true;
            suppressedLoco.Clear();
            locoSuppressed = false;
        }

        static bool locoTypeResolved;
        static System.Type locoProviderType;
        static System.Type LocoProviderType
        {
            get
            {
                if (!locoTypeResolved)
                {
                    locoProviderType =
                        System.Type.GetType("UnityEngine.XR.Interaction.Toolkit.Locomotion.LocomotionProvider, Unity.XR.Interaction.Toolkit")
                        ?? System.Type.GetType("UnityEngine.XR.Interaction.Toolkit.LocomotionProvider, Unity.XR.Interaction.Toolkit");
                    locoTypeResolved = true;
                }
                return locoProviderType;
            }
        }
    }
}
