using System.Net;
using System.Net.Sockets;
using System.Text;
using Unity.Netcode;
using UnityEngine;

namespace AIXRCrane.Crane.Sts.Net
{
    /// <summary>같은 와이파이에서 호스트를 자동으로 찾는 UDP 브로드캐스트 비콘(선택).
    /// 호스트는 주기 비콘 송신, 관전자는 수신해 IP 자동 입력. 실패해도 게임엔 영향 없음(수동 IP 폴백).</summary>
    [AddComponentMenu("AI-XR Crane/Net/LAN Discovery")]
    [RequireComponent(typeof(NetLanUI))]
    [DisallowMultipleComponent]
    public sealed class LanDiscovery : MonoBehaviour
    {
        [SerializeField] int discoveryPort = NetConfig.DiscoveryPort;   // [H3] SSOT
        [SerializeField] float beaconInterval = 1.0f;

        UdpClient udp;
        NetLanUI ui;
        float nextBeacon;
        const string Tag = "STSCRANE";
        const float StickS = 3f;   // 고른 세션의 비콘이 이만큼 끊겨야 다른 호스트로 넘어간다
        string sessionId;          // 호스트일 때 내 세션 — 비콘에 싣는다
        string stuck; float stuckSeen = float.NegativeInfinity;   // 관전자가 고른 세션

        void Awake() => ui = GetComponent<NetLanUI>();

        void OnEnable()
        {
            AcquireMulticastLock();   // Quest/Android: 이 락이 없으면 OS가 브로드캐스트 '수신'을 차단 → 호스트 못 찾음
            try
            {
                udp = new UdpClient { EnableBroadcast = true };
                udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                udp.Client.Bind(new IPEndPoint(IPAddress.Any, discoveryPort));
            }
            catch { udp = null; }   // 디스커버리 불가 → 수동 IP로 폴백
        }

        void OnDisable()
        {
            try { udp?.Close(); } catch { }
            udp = null;
            ReleaseMulticastLock();
        }

        // Android(Quest) 멀티캐스트/브로드캐스트 수신 락 — 기본은 유니캐스트만 받고 나머지는 버린다.
        //   WifiManager.MulticastLock 을 잡아야 수신된다.
#if UNITY_ANDROID && !UNITY_EDITOR
        AndroidJavaObject multicastLock;
        void AcquireMulticastLock()
        {
            try
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                using var wifi = activity.Call<AndroidJavaObject>("getSystemService", "wifi");
                multicastLock = wifi.Call<AndroidJavaObject>("createMulticastLock", "STSCraneDiscovery");
                multicastLock.Call("setReferenceCounted", true);
                multicastLock.Call("acquire");
            }
            catch { multicastLock = null; }   // 실패해도 게임엔 영향 없음(수동 IP 폴백)
        }
        void ReleaseMulticastLock()
        {
            try { multicastLock?.Call("release"); multicastLock?.Dispose(); } catch { }
            multicastLock = null;
        }
#else
        void AcquireMulticastLock() { }
        void ReleaseMulticastLock() { }
#endif

        void Update()
        {
            if (udp == null) return;
            var nm = NetworkManager.Singleton;

            // 호스트면 비콘 송신
            if (nm != null && nm.IsServer && Time.unscaledTime >= nextBeacon)
            {
                nextBeacon = Time.unscaledTime + Mathf.Max(0.25f, beaconInterval);
                sessionId ??= System.DateTime.UtcNow.Ticks.ToString("x");
                Broadcast($"{Tag}|{Port(nm)}|{sessionId}|{DeviceIdentity.LineText(DeviceIdentity.Label).Replace('|', ' ')}");   // 형식: 태그|포트|세션|호스트 이름
            }
            else if (nm == null || !nm.IsServer) sessionId = null;

            // 미접속 상태면 비콘 수신 대기 → 호스트 IP 자동 입력
            bool idle = nm == null || (!nm.IsClient && !nm.IsServer);
            try
            {
                while (udp.Available > 0)
                {
                    IPEndPoint from = new IPEndPoint(IPAddress.Any, 0);
                    byte[] data = udp.Receive(ref from);
                    string msg = Encoding.UTF8.GetString(data);
                    if (!idle || !msg.StartsWith(Tag) || ui == null) continue;
                    var f = msg.Split('|');
                    string session = f.Length > 2 ? f[2] : from.Address.ToString();   // 옛 비콘(태그|포트)은 주소로 구분
                    // 호스트가 여럿이면 처음 고른 세션에 머문다 — 비콘마다 주소가 바뀌어 엉뚱한 호스트로 붙을 수 있었다(WBS 5.4).
                    if (stuck != null && session != stuck && Time.unscaledTime - stuckSeen < StickS) continue;
                    stuck = session; stuckSeen = Time.unscaledTime;
                    ui.SetDiscoveredHost(from.Address.ToString(), f.Length > 3 ? f[3] : "");
                }
            }
            catch { /* 수신 오류 무시 */ }
        }

        void Broadcast(string msg)
        {
            try
            {
                byte[] data = Encoding.UTF8.GetBytes(msg);
                udp.Send(data, data.Length, new IPEndPoint(IPAddress.Broadcast, discoveryPort));
            }
            catch { }
        }

        static ushort Port(NetworkManager nm)
        {
            var t = nm.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
            return t != null ? t.ConnectionData.Port : NetConfig.DefaultPort;   // [H3] 폴백 포트 SSOT
        }
    }
}
