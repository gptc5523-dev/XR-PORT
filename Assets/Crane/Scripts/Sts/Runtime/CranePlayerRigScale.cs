using UnityEngine;

namespace Container.Crane.Sts
{
    /// <summary>
    /// 플레이어(XR Origin 리그)를 크레인과 같은 1/24로 축소 → 미니어처 크레인이 HMD엔 실물 크기로 보인다.
    ///
    /// ★ 리그를 S=1/24로 줄이면 "1:1 월드 단위를 가정한 값"이 전부 어긋난다. 여기서 그걸 한꺼번에 재계산한다:
    ///   1) rig.localScale = S
    ///   2) 카메라 near clip ↓ (작아진 손/HUD가 근접평면에 잘리지 않게)
    ///   3) CharacterController 우회 (XRBodyTransformer.constrainedBodyManipulator=null) + 중력off
    ///      → 1/24로 축소된 충돌 캡슐이 부두에 '껴서' 전진이 막히던 '걷기 안 됨'의 진짜 원인 해소(XR Origin 직접 이동)
    ///   4) 걷기 moveSpeed = walkSpeed 그대로 (ApplyWalkSpeed가 재적용). ※ rigScale을 곱하지 않는다 —
    ///      XRI ContinuousMoveProvider가 이동량에 originTransform.localScale.x를 이미 곱하므로(중복 곱하면 1/576로
    ///      걷기가 거의 0이 됨), moveSpeed엔 '체감' 속도를 그대로 넣어야 1:1 체감이 된다.
    ///  ※ HUD 손 위 높이(CraneHud.FaceCameraAbove)·컨트롤러 탐색(FindController)·컨테이너 배치(VRTestMenu 부두 고정)는
    ///    각 위치에서 스케일 인지로 처리됨. 크레인/그랩은 크레인 공간(이미 1/24)이라 리그 스케일과 무관.
    ///  ※ 남은 헤드셋 튜닝(걷기 차단 원인 아님): 중력 체감(9.81이 24배), near clip 적정값, 텔레포트 거리, 컴포트 비네팅.
    ///
    /// enableScaling=false 면 실척 원복. [RuntimeInitializeOnLoadMethod]로 자동 스폰. 위치 배치와 독립(순서 무관).
    /// </summary>
    [AddComponentMenu("Container/STS Crane/Crane Player Rig Scale")]
    [DisallowMultipleComponent]
    public sealed class CranePlayerRigScale : MonoBehaviour
    {
        [Tooltip("끄면 리그를 실척(1)으로 둔다 — 즉시 원복용.")]
        [SerializeField] bool enableScaling = true;
        [Tooltip("리그 스케일. 크레인과 동일한 1/24가 기본. 씬에 StsCrane이 있으면 그 ModelScale을 우선 사용.")]
        [SerializeField] float scale = 1f / 24f;
        [SerializeField] bool debugLog = true;
        bool configured;   // 1회성 설정(near clip·CC 우회·걷기속도) 완료 여부
        Transform cachedRig;   // 한 번 찾은 리그(XR Origin) 재사용 — 파괴되면 다시 탐색
        Camera cachedCam;      // 리그의 카메라(near clip 조정용)
        int noRigFrames;       // 리그 못 찾은 동안 경고 스로틀용 프레임 카운터
        int driftFrames;       // 스케일이 자꾸 되돌려질 때 경고 스로틀용 카운터

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoSpawn() => CraneHud.EnsureSpawned<CranePlayerRigScale>("PlayerRigScale");

        void Update()
        {
            if (!enableScaling || scale <= 0f) { enabled = false; return; }   // 원복/무효 — 실척 유지

            // ★ 리그 탐색을 XR Origin 컴포넌트 직접 탐색으로 — 기존 Camera.main.transform.root는 다음 두 경우에
            //   1/24를 영영 못 먹였다(=세상이 커 보임/플레이어 거대):
            //     ① Main Camera 태그가 프리팹에만 있어 런타임에 누락/교체(MR 패스스루) 시 Camera.main=null → rig=null
            //     ② XR Origin이 다른 부모 밑에 중첩되면 root가 엉뚱한 오브젝트가 됨
            //   XROrigin 컴포넌트는 태그·계층 깊이와 무관하게 '스케일 대상' 그 자체라 둘 다 회피된다.
            //   한 번 찾으면 캐시(리그 GO는 플레이 세션 내내 유지). 파괴/미발견 시에만 재탐색.
            if (cachedRig == null) cachedRig = FindRig(out cachedCam);
            if (cachedRig == null)
            {
                // 못 찾아도 영구 비활성화하지 않고 다음 프레임에 무한 재시도. 가끔만 경고(원인 진단용).
                if (debugLog && (noRigFrames++ % 120) == 0)
                    Debug.LogWarning("[PlayerRigScale] XR Origin 리그를 못 찾음 — 재시도 중. " +
                                     "씬에 XR Origin(XR Rig) 프리팹이 있는지 확인.");
                return;
            }
            noRigFrames = 0;
            Transform rig = cachedRig;

            // 크레인 ModelScale을 단일 소스로 우선 사용(있으면) — 크레인과 항상 같은 비율 보장.
            var crane = FindAnyObjectByType<StsCrane>();
            float s = crane != null ? crane.ModelScale : scale;

            // ★ 매 프레임 재확인 — XR 시스템/드라이버가 리그 localScale을 1로 되돌려 '1:1로 보이는' 것을 방지.
            //   (이미 맞으면 아무 것도 안 함 → 비용 거의 0.)
            if (Mathf.Abs(rig.localScale.x - s) > 1e-6f ||
                Mathf.Abs(rig.localScale.y - s) > 1e-6f ||
                Mathf.Abs(rig.localScale.z - s) > 1e-6f)
            {
                // 최초 설정 이후에도 매 프레임 드리프트가 잡히면 = 누군가 1로 되돌리며 싸우는 중(원인 진단).
                if (configured && debugLog && (driftFrames++ % 120) == 0)
                    Debug.LogWarning($"[PlayerRigScale] 리그 스케일이 {rig.localScale.x:0.####}로 되돌려져 재적용 중 " +
                                     $"— 다른 컴포넌트가 매 프레임 리셋하는지 의심(XROrigin/CharacterController 등).");
                rig.localScale = Vector3.one * s;
            }

            if (configured) return;   // 아래는 1회만
            configured = true;

            // near clip ↓ — 작아진 손/HUD가 근접평면에 잘리지 않게.
            if (cachedCam != null && cachedCam.nearClipPlane > 0.01f) cachedCam.nearClipPlane = 0.01f;

            // CharacterController(충돌 캡슐) 우회 + 중력off — 1/24 캡슐이 부두에 '껴서' 전진 막히던 것 해소.
            BypassCharacterControllerLocomotion(rig);

            // 걷기 속도 1:1 체감으로 재적용(리그 스케일 반영).
            FindAnyObjectByType<StsCraneVRController>()?.ReapplyWalkSpeed();

            if (debugLog)
            {
                float eyeY = cachedCam != null ? cachedCam.transform.position.y : float.NaN;
                float camScale = cachedCam != null ? cachedCam.transform.lossyScale.y : float.NaN;
                bool camUnderRig = cachedCam != null && cachedCam.transform.IsChildOf(rig);
                Debug.Log($"[PlayerRigScale] 리그 '{rig.name}' lossyScale → {rig.lossyScale.x:0.####} (목표 {s:0.####}). " +
                          $"카메라 '{(cachedCam != null ? cachedCam.name : "null")}' lossyScale={camScale:0.####}, " +
                          $"카메라가 리그 자식? {camUnderRig}, 눈높이(월드Y)={eyeY:0.###}m. " +
                          $"※ 정상이면 카메라 lossyScale~0.0417·눈높이~0.05m. " +
                          $"카메라 lossyScale=1인데 리그는 0.04면 → 카메라가 이 리그 밖(엉뚱한 리그를 줄인 것).");
            }
        }

        /// <summary>
        /// 스케일 대상 리그를 찾는다. ★ 핵심: '카메라를 실제로 자식으로 가진 리그'를 줄여야 눈높이가 바뀐다.
        /// XROrigin을 찾되, 그게 Main Camera의 조상이 아니면(=다른 리그/카메라 reparent) 카메라의 실제 루트를 쓴다.
        /// 둘 다 없으면 카메라 루트로 폴백. XRI 어셈블리 직접참조 없이 리플렉션.
        /// </summary>
        Transform FindRig(out Camera cam)
        {
            cam = Camera.main;
            Transform camRoot = cam != null ? cam.transform.root : null;

            var t = GetXROriginType();
            if (t != null && FindAnyObjectByType(t) is Component xrOrigin)
            {
                Transform origin = xrOrigin.transform;
                if (cam == null) cam = xrOrigin.GetComponentInChildren<Camera>(true);

                // 카메라가 이 XROrigin 하위일 때만 신뢰. 아니면 줄여도 카메라엔 안 먹어 '눈높이 1.3m 그대로' 증상이 난다.
                if (cam == null || cam.transform.IsChildOf(origin)) return origin;

                if (debugLog)
                    Debug.LogWarning($"[PlayerRigScale] ★ XROrigin '{origin.name}'이 Main Camera('{cam.name}')의 " +
                                     $"조상이 아님 → 엉뚱한 리그를 줄이고 있었음. 카메라 실제 루트 '{camRoot?.name}'로 전환.");
                return camRoot;
            }
            return camRoot;
        }

        static bool xrTypeResolved;
        static System.Type xrOriginType;
        static System.Type GetXROriginType()
        {
            if (!xrTypeResolved)
            {
                xrOriginType = System.Type.GetType("Unity.XR.CoreUtils.XROrigin, Unity.XR.CoreUtils");
                xrTypeResolved = true;
            }
            return xrOriginType;
        }

        // XRI 이동을 CharacterController(충돌 캡슐) 우회 → XR Origin 직접 이동으로 전환 + 중력 끔.
        //   1/24 축소 캡슐이 부두에 껴 전진이 막히는 문제 해결. XRI 어셈블리 직접참조 없이 리플렉션으로 안전하게.
        static void BypassCharacterControllerLocomotion(Transform rig)
        {
            // XRBodyTransformer.constrainedBodyManipulator = null  → 이동이 CC 매니퓰레이터 대신 트랜스폼 직접 적용
            var btType = System.Type.GetType(
                "UnityEngine.XR.Interaction.Toolkit.Locomotion.XRBodyTransformer, Unity.XR.Interaction.Toolkit");
            if (btType != null)
            {
                var bt = rig.GetComponentInChildren(btType, true);
                if (bt != null)
                {
                    var prop = btType.GetProperty("constrainedBodyManipulator");
                    if (prop != null && prop.CanWrite) prop.SetValue(bt, null);
                }
            }

            // GravityProvider 끔 → CC 충돌이 없으면 중력이 바닥을 뚫으므로(평평한 부두라 고정 높이 유지로 충분).
            var gpType = System.Type.GetType(
                "UnityEngine.XR.Interaction.Toolkit.Locomotion.Gravity.GravityProvider, Unity.XR.Interaction.Toolkit");
            if (gpType != null && rig.GetComponentInChildren(gpType, true) is Behaviour gp)
                gp.enabled = false;
        }
    }
}
