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
        [Tooltip("바닥 월드 Y(부두 바닥 윗면). 기본 0 — 부두가 다른 높이면 맞춰 설정.")]
        [SerializeField] float floorWorldY = 0f;
        [Tooltip("바닥 위로 최소 이 높이(m)까지만 내려감 — 바닥 밑으로는 절대 안 내려가게.")]
        [SerializeField] float minEyeAboveFloor = 0.1f;

        /// <summary>지금 오른손 트리거를 임계 이상 당겨 '높이 조절 중'인지.
        /// StsCraneVRController가 입력 양보(걷기 정지·왼손 스틱 무효화) 판단에 참조.</summary>
        public bool HeightHold { get; private set; }

        Transform camT;               // 카메라 Transform(바닥 클램프용 월드 Y 읽기)
        Transform cameraOffset;       // 카메라의 부모(XR Origin > Camera Offset) — 여기를 조절
        Vector3 baseCameraOffsetLocal;// 시작 시 카메라 오프셋 '로컬' 위치(기준 — 리그가 걸어 이동해도 안 흔들림)
        bool cameraOffsetCached;
        float viewHeightOffset;       // 기준 대비 현재 눈높이 오프셋(m, 월드 수직)

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoSpawn() => CraneHud.EnsureSpawned<CraneViewHeightAdjuster>("ViewHeightAdjuster");

        void Update()
        {
            var right = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            float rTrig = 0f;
            if (right.isValid) right.TryGetFeatureValue(CommonUsages.trigger, out rTrig);
            HeightHold = rTrig > triggerHoldThreshold;
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

            // 바닥 밑으로는 안 내려가게 — 단 리그 스케일 인지로. (안 그러면 1/24에선 minEyeAboveFloor(0.1 월드)이
            //   미니어처 플레이어의 낮은 눈높이(월드 ~0.057m)보다 높아, 억지로 눈을 끌어올려 max에 고정·1:1처럼 떠 보임.)
            //   floorMinY는 월드 기준값을 스케일로 줄이고, below(월드)는 로컬 오프셋으로 환산해 더한다.
            float rigScale = cameraOffset.parent != null ? cameraOffset.parent.lossyScale.y : 1f;
            float floorMinY = floorWorldY + minEyeAboveFloor * rigScale;
            float below = floorMinY - camT.position.y;
            if (below > 0f) { newOffset += below / Mathf.Max(rigScale, 1e-4f); ApplyCameraOffsetY(newOffset); }

            viewHeightOffset = newOffset;
        }

        void ApplyCameraOffsetY(float offset)
        {
            // 부모(리그) 공간에서 '월드 수직(up)'에 해당하는 방향으로 '로컬' 위치를 옮긴다.
            //   - 로컬 기준이라 리그가 걸어 이동해도 기준점(base)이 안 흔들린다 → '호스트에서 안 내려감' 해결.
            //   - 월드 up 방향이라 오프셋 노드가 기울어 있어도 뒤로 새지 않고 곧게 위아래 → '뒤로 내려옴' 해결.
            Vector3 localUp = cameraOffset.parent != null
                ? cameraOffset.parent.InverseTransformDirection(Vector3.up)
                : Vector3.up;
            cameraOffset.localPosition = baseCameraOffsetLocal + localUp * offset;
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
        }
    }
}
