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
        /// <summary>평면 모드(= VR 이 아닌 모든 실행 = 모바일 관전 화면)로 부팅됐는가.</summary>
        public static bool Active { get; private set; }

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

            // 프레임 상한 — 평면 모드엔 박자가 없고 vSyncCount도 0이라 1300 FPS로 GPU를 태웠다(스트림은 60, VR 몫을 뺏음).
            Application.targetFrameRate = FlatTargetFps;

            // ① 씬의 XR Origin 리그를 끈다 — 카메라가 둘이면 Camera.main이 엉킨다. 끄면 CranePlayerRigScale이 평면 리그를 잡는다.
            int disabled = DisableXrRigs();

            // ②-a 기존 MainCamera도 끈다(Camera.main 불확정 방지) — Camera·AudioListener 컴포넌트만.
            int camsOff = DisableExistingMainCameras();

            // ② 평면 리그 + HUD 생성 — 각자 자기 GameObject.
            var rigGo = new GameObject("FlatPlayerRig");
            var rig = rigGo.AddComponent<FlatPlayerRig>();

            // 안경이 이 기기에 꽂혀 있으면 고개로 둘러본다(없으면 조용히 기다림).
            new GameObject("XrealHeadTracker").AddComponent<XrealHeadTracker>();

            // VR 이 아니면 언제나 모바일 관전 화면 — PC 평면 화면(패드 조종기·LAN 패널)은 없다. 공간 마우스(커서+누르기)로 다닌다.
            Cursor.visible = false;   // 서버 관전 화면은 안경으로 스트리밍된다 — 포인터가 찍히지 않게(위치는 그대로 읽힌다)
            new GameObject("MobileSpectatorHud").AddComponent<MobileSpectatorHud>();

            if (debugLog)
                Debug.Log($"[FlatMode] 평면 모드 진입 — XR 리그 {disabled}개·기존 MainCamera {camsOff}개 비활성, " +
                          $"평면 리그·모바일 관전 화면 생성. 판정={(wasForced ? "강제" : "자동(XR 미검출)")}.");

            // QA: '카메라 1대 + MainCamera 태그'로 끝났는지 — 깨지면 StartPlacer·RigScale(1/24)이 평면 리그를 못 잡는다.
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

        /// <summary>기존 MainCamera 태그 카메라를 끄고 개수를 반환(Camera·AudioListener만).
        /// ★ 평면 리그를 만들기 전에 호출 — 나중에 부르면 방금 만든 카메라까지 꺼진다.</summary>
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
