using UnityEngine;
using UnityEngine.XR;

namespace AIXRCrane.Crane.Sts.Net
{
    /// <summary>이 인스턴스가 어느 자리·기기인가 — 서버 감시가 넘긴 -slot N·-device MAC, 없으면 헤드셋 일련번호. 사용자 등록의 기기 ID.</summary>
    public sealed class DeviceIdentity : MonoBehaviour
    {
        /// <summary>VR 자리 번호(1~5), 모르면 0.</summary>
        public static int Slot { get; private set; }
        /// <summary>기기 ID — "MAC:aa:bb:…" 또는 "SN:일련번호", 모르면 빈 문자열.</summary>
        public static string DeviceId { get; private set; } = "";
        /// <summary>헤드셋이 알려 준 이름·제조사·일련번호(WiVRn 이 넘기는지 확인용).</summary>
        public static string Headset { get; private set; } = "";

        bool headLogged;
        float next;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoSpawn() => CraneHud.EnsureSpawned<DeviceIdentity>("Device");

        void Awake()
        {
            int.TryParse(Arg("-slot"), out int s);
            Slot = s;
            string mac = Arg("-device");
            DeviceId = string.IsNullOrEmpty(mac) ? "" : "MAC:" + mac.ToLowerInvariant();
            Debug.Log($"[Device] 자리 {(Slot > 0 ? "VR-" + Slot : "모름")} · 기기 {(DeviceId == "" ? "모름" : DeviceId)}");
        }

        void Update()
        {
            if (headLogged || Time.unscaledTime < next) return;
            next = Time.unscaledTime + 1f;
            var head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            if (!head.isValid) return;
            headLogged = true;
            Headset = $"{head.name} / {head.manufacturer} / {head.serialNumber}";
            if (DeviceId == "" && !string.IsNullOrEmpty(head.serialNumber)) DeviceId = "SN:" + head.serialNumber;
            Debug.Log($"[Device] 헤드셋 {Headset} → 기기 {(DeviceId == "" ? "모름" : DeviceId)}");
        }

        static string Arg(string name)
        {
            var a = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++)
                if (string.Equals(a[i], name, System.StringComparison.OrdinalIgnoreCase)) return a[i + 1];
            return null;
        }
    }
}
