using System.Collections;
using UnityEngine;
using UnityEngine.XR;
using AIXRCrane.Crane.Sts;

namespace AIXRCrane.Crane.Flat
{
    /// <summary>평면(비-VR) 모드 부팅 분기 — XR 헤드셋이 감지되면 그대로 두고, 안 뜨면 평면 리그를 띄운다.
    /// 리그 카메라에 MainCamera 태그를 달아야 CranePlayerStartPlacer·RigScale 이 Camera.main 으로 인식한다.</summary>
    [AddComponentMenu("AI-XR Crane/Flat Mode/Flat Mode Bootstrap")]
    [DisallowMultipleComponent]
    public sealed class FlatModeBootstrap : MonoBehaviour
    {
        /// <summary>평면 모드로 부팅됐는가. HUD·컨트롤러가 자기 활성 여부 판단에 참조.</summary>
        public static bool Active { get; private set; }

        /// <summary>평면 모드 안에서 '모바일 관전 화면'으로 떴는가 — 안드로이드 기기·에디터 휴대폰 시뮬레이터·`-mobile` 인자.
        ///   오너 2026-09-18 "VR 이랑 모바일이랑 화면이 다르게". VR·PC 평면 모드(모니터+패드)는 그대로다.</summary>
        public static bool Mobile { get; private set; }

        /// <summary>강제 모드 PlayerPrefs 키 — 0=자동, 1=평면 강제, 2=VR 강제. 에디터 메뉴가 이 값을 쓴다.</summary>
        public const string ForcePrefKey = "AIXRCrane.FlatMode.Force";

        [Tooltip("XR(헤드셋)이 늦게 올라올 수 있어, 이 프레임 수만큼 기다렸다가 없으면 평면으로 판정한다.")]
        [SerializeField] int xrWaitFrames = 60;
        /// <summary>평면 모드 프레임 상한. 안경 스트리밍(Moonlight 기본 60)·일반 모니터 기준.</summary>
        const int FlatTargetFps = 60;
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

            // 프레임 상한 — VR 은 OpenXR 이 프레임을 맞추지만 평면 모드엔 박자가 없고 vSyncCount 도 전 품질 0 이다.
            //   2026-09-18 서버 관전 인스턴스(안경 스트리밍용)가 1300 FPS 로 돌며 GPU 47%·140 W 를 썼다 —
            //   스트림은 60 이라 거의 전부 버리는 일이고, 같은 GPU 의 VR 5대 몫을 뺏는다. 노트북 평면 모드도 같다.
            Application.targetFrameRate = FlatTargetFps;

            // ① 씬의 XR Origin 리그를 끈다 — 카메라가 둘이면 Camera.main 이 엉키고 GPU도 낭비된다.
            //    비활성화하면 FindAnyObjectByType 에서도 빠져 CranePlayerRigScale 이 평면 리그를 잡는다.
            int disabled = DisableXrRigs();

            // ②-a 씬에 이미 있는 MainCamera 도 끈다 — 남겨두면 Camera.main 이 어느 쪽을 잡을지 불확정.
            //     GameObject 전체가 아니라 Camera·AudioListener 컴포넌트만 끈다(다른 스크립트는 유지).
            int camsOff = DisableExistingMainCameras();

            // ② 평면 리그 + 크레인 조작 + HUD 생성. 각자 자기 GameObject를 갖는다(끄고 켜기 쉽게).
            var rigGo = new GameObject("FlatPlayerRig");
            var rig = rigGo.AddComponent<FlatPlayerRig>();

            // 안경이 이 기기에 꽂혀 있으면 고개로 둘러본다(없으면 조용히 기다림) — 평면·모바일 공통.
            new GameObject("XrealHeadTracker").AddComponent<XrealHeadTracker>();

            Mobile = ResolveMobile();
            if (Mobile)
            {
                // 모바일 = 관전 먼저. 패드 조종기·PC HUD 는 안 만들고, 공간 마우스(커서+누르기)로 돌아다닌다.
                rig.PointerNav = true;
                new GameObject("MobileSpectatorHud").AddComponent<MobileSpectatorHud>();
            }
            else
            {
                var ctrlGo = new GameObject("FlatCraneController");
                ctrlGo.AddComponent<FlatCraneController>();

                var hudGo = new GameObject("FlatHud");
                hudGo.AddComponent<FlatHud>();
            }

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

        /// <summary>씬의 기존 MainCamera 태그 카메라를 끄고 개수를 반환(Camera·AudioListener만, 다른 스크립트는 유지).
        /// 반드시 평면 리그를 만들기 전에 호출할 것 — 나중에 부르면 방금 만든 카메라까지 꺼진다.</summary>
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

        // 모바일 관전 화면인가. ★ UnityEngine.Device 를 쓴다 — 에디터 휴대폰 시뮬레이터(Device Simulator)는
        //   이쪽 값만 바꾼다(UnityEngine.Application 은 맥을 본다). 서버 관전 인스턴스(윈도 빌드)는 `-mobile` 로 켠다.
        static bool ResolveMobile()
        {
            foreach (var a in System.Environment.GetCommandLineArgs())
                if (string.Equals(a, "-mobile", System.StringComparison.OrdinalIgnoreCase)) return true;
            return UnityEngine.Device.Application.isMobilePlatform;
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
