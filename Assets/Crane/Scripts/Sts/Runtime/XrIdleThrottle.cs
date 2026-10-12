using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace AIXRCrane.Crane.Sts
{
    /// <summary>VR 대기 — 헤드셋이 없거나 끊겼거나 벗어 둔 동안 프레임을 IdleFps 로 낮춰 서버 GPU·CPU 를 쉬게 하고, 다시 쓰면 바로 원래대로.</summary>
    public sealed class XrIdleThrottle : MonoBehaviour
    {
        public const int IdleFps = 5;
        const float StartDelayS = 10f;   // 평면/VR 판정(FlatModeBootstrap)이 끝난 뒤부터 본다

        static readonly List<XRDisplaySubsystem> displays = new List<XRDisplaySubsystem>();
        bool idle;
        float next;

#if !UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoSpawn() => CraneHud.EnsureSpawned<XrIdleThrottle>("XrIdle");
#endif

        void Update()
        {
            if (Time.realtimeSinceStartup < StartDelayS || Time.unscaledTime < next) return;
            next = Time.unscaledTime + 1f;
            if (CraneLink.FlatMode) { if (idle) Set(false); return; }
            var nm = Unity.Netcode.NetworkManager.Singleton;
            bool hosting = nm != null && nm.IsServer && nm.ConnectedClientsIds.Count > 1;   // 방장이 쉬면 다른 사람 동기화가 끊긴다
            bool want = !hosting && !HeadsetInUse();
            if (want != idle) Set(want);
        }

        void Set(bool on)
        {
            idle = on;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = on ? IdleFps : -1;
            Debug.Log(on ? $"[XrIdle] 헤드셋 없음 — {IdleFps} fps 대기" : "[XrIdle] 헤드셋 사용 — 대기 해제");
        }

        /// <summary>XR 디스플레이가 돌고, 머리 장치가 있고, (지원하면) 착용 중인가.</summary>
        public static bool HeadsetInUse()
        {
            SubsystemManager.GetSubsystems(displays);
            bool running = false;
            foreach (var d in displays) running |= d.running;
            if (!running) return false;
            var head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            if (!head.isValid) return false;
            return !head.TryGetFeatureValue(CommonUsages.userPresence, out bool worn) || worn;
        }
    }
}
