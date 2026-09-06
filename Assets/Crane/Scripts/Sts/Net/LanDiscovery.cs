using System.Net;
using System.Net.Sockets;
using System.Text;
using Unity.Netcode;
using UnityEngine;

namespace Container.Crane.Sts.Net
{
    /// <summary>
    /// (선택) 같은 와이파이에서 호스트를 자동으로 찾는 UDP 브로드캐스트 비콘.
    ///   - 호스트: 주기적으로 "STSCRANE|port" 비콘을 브로드캐스트.
    ///   - 관전자: 비콘을 받으면 NetLanUI에 호스트 IP를 자동 입력(타이핑 불필요).
    /// 실패해도(특히 Quest/Android 브로드캐스트 제약) 게임에 영향 없도록 전부 try/catch. 안 되면 수동 IP 사용.
    /// </summary>
    [AddComponentMenu("Container/Net/LAN Discovery")]
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

        // Android(Quest) 멀티캐스트/브로드캐스트 수신 락
        //   안드로이드는 전력 절약을 위해 기본적으로 자신 앞으로 온 유니캐스트만 올려보내고
        //   브로드캐스트/멀티캐스트 패킷은 버린다. WifiManager.MulticastLock을 잡아야 수신된다.
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
                Broadcast($"{Tag}|{Port(nm)}");
            }

            // 미접속 상태면 비콘 수신 대기 → 호스트 IP 자동 입력
            bool idle = nm == null || (!nm.IsClient && !nm.IsServer);
            try
            {
                while (udp.Available > 0)
                {
                    IPEndPoint from = new IPEndPoint(IPAddress.Any, 0);
                    byte[] data = udp.Receive(ref from);
                    string msg = Encoding.UTF8.GetString(data);
                    if (idle && msg.StartsWith(Tag) && ui != null)
                        ui.SetDiscoveredHost(from.Address.ToString());
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
