using System.Collections;
using UnityEngine;
using UnityEngine.XR;
using Container.Crane.Sts;

namespace Container.Crane.Flat
{
    /// <summary>
    /// 평면(비-VR) 모드 부팅 분기 — XREAL One Pro 등 '대형 화면'으로 볼 때 쓰는 경로.
    ///
    /// ★ 이 폴더(Assets/Crane/Scripts/Flat)는 기존 VR 코드와 분리된 신규 계층이다.
    ///   Sts/ 아래 파일은 한 줄도 수정하지 않고, 여기서 '읽고 호출'만 한다.
    ///   (프로젝트에 asmdef가 없어 전부 Assembly-CSharp로 묶이므로 네임스페이스로 분리한다 —
    ///    신규 asmdef를 만들면 거꾸로 Sts/ 를 참조할 수 없다.)
    ///
    /// [판정] XR 로더가 실제로 물려 있으면(헤드셋 연결) 아무 것도 하지 않고 VR 경로를 그대로 둔다.
    ///   XR이 안 뜨면(PC·전용폰에서 안경만 연결) 평면 리그를 띄운다. 강제 지정도 가능:
    ///     실행 인자  -flat / -vr        (빌드·에디터 공통)
    ///     PlayerPrefs "Container.FlatMode.Force"  0=자동 1=평면강제 2=VR강제
    ///
    /// [기존 시스템과의 접합] 평면 리그의 카메라에 MainCamera 태그를 달고 리그 루트를 최상위에 둔다.
    ///   그러면 CranePlayerStartPlacer(부두 안 배치)·CranePlayerRigScale(1/24 축소)이
    ///   Camera.main.transform.root 를 통해 평면 리그를 '그냥' 인식한다 — 그쪽 코드 수정 불필요.
    /// </summary>
    [AddComponentMenu("Container/Flat Mode/Flat Mode Bootstrap")]
    [DisallowMultipleComponent]
    public sealed class FlatModeBootstrap : MonoBehaviour
    {
        /// <summary>평면 모드로 부팅됐는가. HUD·컨트롤러가 자기 활성 여부 판단에 참조.</summary>
        public static bool Active { get; private set; }

        /// <summary>강제 모드 PlayerPrefs 키 — 0=자동, 1=평면 강제, 2=VR 강제. 에디터 메뉴가 이 값을 쓴다.</summary>
        public const string ForcePrefKey = "Container.FlatMode.Force";

        [Tooltip("XR(헤드셋)이 늦게 올라올 수 있어, 이 프레임 수만큼 기다렸다가 없으면 평면으로 판정한다.")]
        [SerializeField] int xrWaitFrames = 60;
        [SerializeField] bool debugLog = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoSpawn()
        {
            if (FindAnyObjectByType<FlatModeBootstrap>() != null) return;
            var go = new GameObject("FlatModeBootstrap (auto)");
            go.AddComponent<FlatModeBootstrap>();
        }

        IEnumerator Start()
        {
            int forced = ResolveForce();   // -1=자동, 0=VR 강제, 1=평면 강제

            if (forced == 0)
            {
                if (debugLog) Debug.Log("[FlatMode] VR 강제 지정 — 평면 모드를 띄우지 않습니다.");
                yield break;
            }

            if (forced < 0)
            {
                // XR이 붙을 시간을 준다. 붙으면 VR 경로를 그대로 두고 종료.
                for (int i = 0; i < xrWaitFrames && !XRSettings.isDeviceActive; i++) yield return null;
                if (XRSettings.isDeviceActive)
                {
                    if (debugLog)
                        Debug.Log($"[FlatMode] XR 활성('{XRSettings.loadedDeviceName}') — VR 경로 유지, 평면 모드 미적용.");
                    QaLog.Info("FLAT", "boot", $"decision=vr xrActive=true device={XRSettings.loadedDeviceName}");
                    yield break;
                }
            }

            EnterFlatMode(forced == 1);
        }

        void EnterFlatMode(bool wasForced)
        {
            Active = true;

            // ① 씬의 XR Origin 리그를 끈다 — 카메라가 둘이면 Camera.main 이 엉키고 GPU도 낭비된다.
            //    GameObject 비활성이면 FindAnyObjectByType(기본 Exclude)에서도 빠지므로,
            //    CranePlayerRigScale.FindRig 가 XROrigin 대신 평면 리그(카메라 루트)를 잡는다.
            int disabled = DisableXrRigs();

            // ②-a 씬에 이미 있는 MainCamera(예: NEW/SampleScene 의 'Main Camera')도 끈다.
            //     Camera.main 은 '태그가 MainCamera 인 활성 카메라 중 하나'라, 남겨두면 어느 쪽이 잡힐지 불확정이고
            //     StartPlacer·RigScale 이 엉뚱한 오브젝트를 리그로 착각한다. GameObject 를 통째로 끄면 거기 붙은
            //     다른 스크립트까지 죽으므로 Camera·AudioListener 컴포넌트만 끈다(최소 개입).
            int camsOff = DisableExistingMainCameras();

            // ② 평면 리그 + 크레인 조작 + HUD 생성. 각자 자기 GameObject를 갖는다(끄고 켜기 쉽게).
            var rigGo = new GameObject("FlatPlayerRig");
            var rig = rigGo.AddComponent<FlatPlayerRig>();

            var ctrlGo = new GameObject("FlatCraneController");
            ctrlGo.AddComponent<FlatCraneController>();

            var hudGo = new GameObject("FlatHud");
            hudGo.AddComponent<FlatHud>();

            if (debugLog)
                Debug.Log($"[FlatMode] 평면 모드 진입 — XR 리그 {disabled}개·기존 MainCamera {camsOff}개 비활성, " +
                          $"평면 리그·조작·HUD 생성. 판정={(wasForced ? "강제" : "자동(XR 미검출)")}. 게임패드로 조작합니다.");

            // QA: 평면 모드 부팅이 '카메라 1대 + MainCamera 태그' 상태로 끝났는지. 여기가 깨지면
            //     StartPlacer(부두 배치)·RigScale(1/24)이 평면 리그를 못 잡아 시작 위치·크기가 어긋난다.
            var cam = rig.Cam;
            bool ok = cam != null && cam.CompareTag("MainCamera") && cam.transform.root == rigGo.transform;
            QaLog.Check("FLAT", "boot", ok,
                $"decision=flat forced={wasForced} xrRigsDisabled={disabled} " +
                $"cam={(cam != null ? cam.name : "null")} tagged={(cam != null && cam.CompareTag("MainCamera"))} " +
                $"rootIsRig={(cam != null && cam.transform.root == rigGo.transform)}");
        }

        /// <summary>씬의 XR Origin(있으면)을 비활성화하고 개수를 반환. XRI 어셈블리 직접참조 없이 리플렉션.</summary>
        static int DisableXrRigs()
        {
            var t = System.Type.GetType("Unity.XR.CoreUtils.XROrigin, Unity.XR.CoreUtils");
            if (t == null) return 0;
            int n = 0;
            foreach (var o in FindObjectsByType(t, FindObjectsInactive.Exclude))
            {
                if (o is not Component c || !c.gameObject.activeSelf) continue;
                c.gameObject.SetActive(false);
                n++;
            }
            return n;
        }

        /// <summary>
        /// 씬에 이미 있는 MainCamera 태그 카메라를 끄고 개수를 반환 — 평면 리그 카메라와 겹치지 않게.
        /// GameObject 가 아니라 Camera·AudioListener 컴포넌트만 끈다(그 오브젝트의 다른 스크립트는 살려둠).
        /// ※ 반드시 평면 리그를 만들기 '전'에 호출할 것 — 나중에 부르면 내가 만든 카메라까지 끈다.
        /// </summary>
        static int DisableExistingMainCameras()
        {
            int n = 0;
            foreach (var c in FindObjectsByType<Camera>(FindObjectsInactive.Exclude))
            {
                if (c == null || !c.enabled || !c.CompareTag("MainCamera")) continue;
                c.enabled = false;
                var al = c.GetComponent<AudioListener>();
                if (al != null) al.enabled = false;   // 리스너가 둘이면 Unity가 경고하고 소리가 한쪽만 난다
                n++;
            }
            return n;
        }

        // -1=자동, 0=VR 강제, 1=평면 강제. 실행 인자가 PlayerPrefs 보다 우선.
        static int ResolveForce()
        {
            foreach (var a in System.Environment.GetCommandLineArgs())
            {
                if (string.Equals(a, "-flat", System.StringComparison.OrdinalIgnoreCase)) return 1;
                if (string.Equals(a, "-vr", System.StringComparison.OrdinalIgnoreCase)) return 0;
            }
            return PlayerPrefs.GetInt(ForcePrefKey, 0) switch { 1 => 1, 2 => 0, _ => -1 };
        }
    }
}
