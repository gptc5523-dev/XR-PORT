using System.Net;
using System.Net.Sockets;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace Container.Crane.Sts.Net
{
    /// <summary>
    /// 같은 와이파이(LAN) 멀티플레이용 간단 접속 UI(IMGUI).
    ///   - 호스트(조종자): "호스트 시작" → 자기 LAN IP를 크게 표시(참가자에게 불러줄 수 있게).
    ///   - 관전자: 호스트 IP 입력 후 "참가". LanDiscovery가 있으면 IP가 자동 채워진다.
    /// 최대 인원 maxPlayers(기본 5, 호스트 포함) 초과 접속은 거부한다.
    /// </summary>
    [AddComponentMenu("Container/Net/Net LAN UI")]
    [DisallowMultipleComponent]
    public sealed class NetLanUI : MonoBehaviour
    {
        [SerializeField] ushort port = 7777;
        [Tooltip("호스트 포함 최대 동시 인원")]
        [SerializeField] int maxPlayers = 5;
        [SerializeField] string joinIp = "192.168.0.10";

        string localIp = "...";
        bool hostDiscovered;

        // VR 시작 메뉴(CraneNetMenuHUD)가 읽는 정보/조작 진입점.
        public string LocalIp => localIp;
        public int MaxPlayers => maxPlayers;
        public string JoinIp { get => joinIp; set { if (!string.IsNullOrEmpty(value)) joinIp = value; } }
        /// <summary>LanDiscovery가 호스트 비콘을 받아 JoinIp를 자동 설정했는지(자동 접속 트리거용).</summary>
        public bool HostDiscovered => hostDiscovered;

        void Awake() => localIp = GetLocalIPv4();

        UnityTransport Transport =>
            NetworkManager.Singleton != null
                ? NetworkManager.Singleton.GetComponent<UnityTransport>()
                : null;

        /// <summary>LanDiscovery가 호스트를 찾으면 호출 — 참가 IP 자동 입력.</summary>
        public void SetDiscoveredHost(string ip)
        {
            if (string.IsNullOrEmpty(ip)) return;
            joinIp = ip;
            hostDiscovered = true;
        }

        public void BeginHost()  => StartHost();
        public void BeginClient() => StartClient();

        void StartHost()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || Transport == null) return;

            // 최대 인원 제한 — 승인 콜백(서버에서 실행). 정원 초과 시 거부.
            nm.NetworkConfig.ConnectionApproval = true;
            nm.ConnectionApprovalCallback = (req, resp) =>
            {
                resp.Approved = nm.ConnectedClientsIds.Count < maxPlayers;
                resp.CreatePlayerObject = true;
                resp.Pending = false;
            };

            Transport.SetConnectionData("0.0.0.0", port, "0.0.0.0");   // 모든 인터페이스에서 수신
            nm.StartHost();
        }

        void StartClient()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || Transport == null) return;
            nm.NetworkConfig.ConnectionApproval = true;   // 호스트와 설정 일치
            Transport.SetConnectionData(joinIp.Trim(), port);
            nm.StartClient();
        }

        void OnGUI()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null) return;

            const int W = 360;
            GUILayout.BeginArea(new Rect(16, 16, W, 260), GUI.skin.box);
            GUILayout.Label("<b>STS 크레인 — LAN 멀티플레이</b>", Rich());

            if (!nm.IsClient && !nm.IsServer)
            {
                GUILayout.Space(4);
                GUILayout.Label($"내 IP: <b>{localIp}</b>  (참가자에게 불러주세요)", Rich());
                if (GUILayout.Button("호스트 시작 (조종자)", Big())) StartHost();

                GUILayout.Space(8);
                GUILayout.Label("관전자 — 호스트 IP 입력 후 참가:");
                joinIp = GUILayout.TextField(joinIp, Big());
                if (GUILayout.Button("참가 (관전)", Big())) StartClient();
            }
            else
            {
                string role = nm.IsServer ? "호스트(조종자)" : "관전자";
                GUILayout.Label($"<b>{role}</b> — 접속 인원: {nm.ConnectedClientsIds.Count}/{maxPlayers}", Rich());
                if (nm.IsServer) GUILayout.Label($"내 IP: <b>{localIp}</b> : {port}", Rich());
                GUILayout.Space(8);
                if (GUILayout.Button("연결 끊기", Big())) nm.Shutdown();
            }
            GUILayout.EndArea();
        }

        static GUIStyle Rich() => new GUIStyle(GUI.skin.label) { richText = true };
        static GUIStyle Big()
        {
            var s = new GUIStyle(GUI.skin.button) { fontSize = 16, richText = true };
            s.fixedHeight = 34;
            return s;
        }

        static string GetLocalIPv4()
        {
            // 1순위: 아웃바운드 UDP 소켓으로 실제 사용하는 LAN 인터페이스 IP를 구한다(실제 패킷은 안 나감 —
            //   connect는 라우팅만 결정). 멀티 인터페이스인 Quest/Android에서 GetHostEntry보다 정확/신뢰적.
            try
            {
                using var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                s.Connect("8.8.8.8", 65530);
                if (s.LocalEndPoint is IPEndPoint ep && !IPAddress.IsLoopback(ep.Address))
                    return ep.Address.ToString();
            }
            catch { /* 폴백으로 진행 */ }
            // 2순위: DNS 조회(일부 플랫폼 미지원 가능).
            try
            {
                foreach (var ip in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
                    if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                        return ip.ToString();
            }
            catch { /* 일부 플랫폼은 GetHostEntry 미지원 */ }
            return "127.0.0.1";
        }
    }
}
