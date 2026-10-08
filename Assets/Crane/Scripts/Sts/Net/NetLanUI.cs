using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace AIXRCrane.Crane.Sts.Net
{
    /// <summary>LAN 멀티플레이 접속 UI(IMGUI) — 호스트는 자기 IP 표시, 관전자는 IP 입력 후 참가(LanDiscovery 자동 채움).
    /// maxPlayers(호스트 포함) 초과는 거부.</summary>
    [AddComponentMenu("AI-XR Crane/Net/Net LAN UI")]
    [DisallowMultipleComponent]
    public sealed class NetLanUI : MonoBehaviour
    {
        [SerializeField] ushort port = NetConfig.DefaultPort;            // [H3] SSOT
        [Tooltip("호스트 포함 최대 동시 인원")]
        [SerializeField] int maxPlayers = NetConfig.MaxPlayers;          // [H3] SSOT
        [SerializeField] string joinIp = NetConfig.DefaultJoinIp;        // [H3] SSOT

        string localIp = "...", vpnIp;
        bool hostDiscovered;
        bool hostFailed;

        // 승인됐지만 합류 전인 클라이언트 — 동시 접속 정원 초과 레이스 방지.
        readonly HashSet<ulong> pendingApprovals = new();

        // VR 시작 메뉴(CraneNetMenuHUD)가 읽는 정보/조작 진입점.
        public string LocalIp => localIp;
        /// <summary>Tailscale(100.64/10) 주소 — 없으면 null. 서버·원격 참가자는 VPN 으로만 닿으므로 같이 보여 준다.</summary>
        /// <summary>화면용 — "LAN · VPN 100.x" (VPN 없으면 LAN 만).</summary>
        public string IpLabel => vpnIp == null || vpnIp == localIp ? localIp : $"{localIp} · VPN {vpnIp}";
        public int MaxPlayers => maxPlayers;
        public string JoinIp { get => joinIp; set { if (!string.IsNullOrEmpty(value)) joinIp = value; } }
        /// <summary>LanDiscovery가 호스트 비콘을 받아 JoinIp를 자동 설정했는지(자동 접속 트리거용).</summary>
        public bool HostDiscovered => hostDiscovered;
        /// <summary>마지막 호스트 비콘 시각(unscaled 초). HostDiscovered 는 계속 true 라 생존 판정은 이 값으로(비콘 1초 주기).</summary>
        public float LastHostSeen { get; private set; } = float.NegativeInfinity;
        /// <summary>직전 '호스트 시작' 실패 여부(대개 같은 PC 다른 인스턴스가 포트 점유). CraneNetMenuHUD 가 알림.</summary>
        public bool HostFailed => hostFailed;

        void Awake() => localIp = GetLocalIPv4(out vpnIp);

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
            LastHostSeen = Time.unscaledTime;
        }

        public void BeginHost()  => StartHost();
        public void BeginClient() => StartClient();

        /// <summary>세션을 끊고 시작 메뉴로(<see cref="ExitZone"/>·헤드셋 이탈 감시가 호출).
        /// 안 부르면 서버가 포트를 쥔 채 남아 다음 호스트 시작이 실패한다.</summary>
        public void Leave()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null) return;
            if (nm.IsClient || nm.IsServer) nm.Shutdown();
            hostFailed = false;   // 다음 시도에 옛 실패 문구가 남지 않게
        }

        void StartHost()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || Transport == null) return;

            // ★ 모바일(안경·폰 관전)은 호스트 불가(오너 결정). 호스트를 여는 길은 여기 하나뿐이라 여기서 막는다.
            if (AIXRCrane.Crane.Flat.FlatModeBootstrap.Active)
            {
                Debug.LogWarning("[NetLanUI] 모바일은 호스트가 될 수 없습니다 — 참가자로만 접속합니다.");
                hostFailed = true;
                return;
            }

            // 최대 인원 제한 — 승인 콜백(서버에서 실행). 정원 초과 시 거부.
            nm.NetworkConfig.ConnectionApproval = true;
            pendingApprovals.Clear();
            nm.ConnectionApprovalCallback = ApproveConnection;
            // 합류 전 인원 추적(중복 구독 방지로 먼저 해제).
            nm.OnClientConnectedCallback  -= OnClientJoined; nm.OnClientConnectedCallback  += OnClientJoined;
            nm.OnClientDisconnectCallback -= OnClientLeft;   nm.OnClientDisconnectCallback += OnClientLeft;

            Transport.SetConnectionData("0.0.0.0", port, "0.0.0.0");   // 모든 인터페이스에서 수신
            // 한 머신 두 번째 호스트는 포트 점유로 실패 — 결과를 남겨 시작 메뉴가 '참가'로 안내.
            hostFailed = !nm.StartHost();
        }

        // 정원 검사 — 접속 수 + pendingApprovals 로 비교해야 동시 요청 레이스에도 초과 안 함.
        void ApproveConnection(NetworkManager.ConnectionApprovalRequest req,
                               NetworkManager.ConnectionApprovalResponse resp)
        {
            var nm = NetworkManager.Singleton;
            int projected = nm.ConnectedClientsIds.Count + pendingApprovals.Count;
            bool ok = projected < maxPlayers;
            resp.Approved = ok;
            resp.CreatePlayerObject = true;
            resp.Pending = false;
            if (ok) pendingApprovals.Add(req.ClientNetworkId);
        }

        void OnClientJoined(ulong clientId) => pendingApprovals.Remove(clientId);   // 합류 완료 → ConnectedClientsIds 로 카운트
        void OnClientLeft(ulong clientId)   => pendingApprovals.Remove(clientId);   // 합류 전 이탈 예약 정리

        void OnDestroy()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null) return;
            nm.OnClientConnectedCallback  -= OnClientJoined;
            nm.OnClientDisconnectCallback -= OnClientLeft;
        }

        void StartClient()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || Transport == null) return;

            // 참가 IP 검증 — 잘못된 값이면 StartClient가 조용히 실패하므로 미리 거른다.
            string ip = joinIp.Trim();
            if (!IPAddress.TryParse(ip, out _))
            {
                Debug.LogWarning($"[NetLanUI] 참가 IP가 올바르지 않습니다: '{ip}'. 호스트 IP(예: {NetConfig.DefaultJoinIp})를 정확히 입력하세요.");
                return;
            }

            // ConnectionApproval 은 NetLanSetup 이 이미 true — 런타임 토글하면 설정 해시 불일치로 거부될 수 있음.
            Transport.SetConnectionData(ip, port);
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
                GUILayout.Label($"내 IP: <b>{IpLabel}</b>  (참가자에게 불러주세요)", Rich());
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
                if (nm.IsServer) GUILayout.Label($"내 IP: <b>{IpLabel}</b> : {port}", Rich());
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

        // 폐쇄망(기본 경로 없음)에서도 맞게 — 인터페이스 주소에서 직접 고르고, 라우팅 조회는 같은 후보일 때만 앞세운다(WBS 9.1).
        static string GetLocalIPv4(out string vpn)
        {
            vpn = null;
            var cands = new List<IPAddress>();
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                        if (ua.Address.AddressFamily == AddressFamily.InterNetwork) { cands.Add(ua.Address); if (vpn == null && IsVpn(ua.Address)) vpn = ua.Address.ToString(); }
                }
            }
            catch { /* 일부 플랫폼은 인터페이스 목록 미지원 — 아래 폴백 */ }
            IPAddress routed = null;
            try
            {
                using var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                s.Connect("8.8.8.8", 65530);   // 패킷은 안 나간다 — 기본 경로가 없으면 여기서 예외
                routed = (s.LocalEndPoint as IPEndPoint)?.Address;
            }
            catch { /* 폐쇄망: 기본 경로 없음 */ }
            string pick = PickLanIPv4(cands, routed);
            if (pick != null) return pick;
            if (vpn != null) return vpn;   // LAN 없이 VPN 만 있는 원격 기기
            try
            {
                foreach (var ip in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
                    if (LanRank(ip) > 0) return ip.ToString();
            }
            catch { /* 일부 플랫폼은 GetHostEntry 미지원 */ }
            return "127.0.0.1";
        }

        /// <summary>LAN 주소 고르기 — 192.168 > 10 > 172.16 > 그 밖, 루프백·링크로컬(169.254, 안경 USB)·CGNAT(100.64/10, Tailscale) 제외. 라우팅 결과가 후보면 그것.</summary>
        public static string PickLanIPv4(IList<IPAddress> cands, IPAddress routed)
        {
            if (routed != null && LanRank(routed) > 0 && cands.Contains(routed)) return routed.ToString();
            IPAddress best = null;
            foreach (var c in cands) if (LanRank(c) > (best == null ? 0 : LanRank(best))) best = c;
            return best?.ToString();
        }

        static bool IsVpn(IPAddress a) { var b = a.GetAddressBytes(); return b[0] == 100 && b[1] >= 64 && b[1] < 128; }

        static int LanRank(IPAddress a)
        {
            if (a.AddressFamily != AddressFamily.InterNetwork) return 0;
            var b = a.GetAddressBytes();
            if (b[0] == 127 || (b[0] == 169 && b[1] == 254) || IsVpn(a) || b[0] == 0) return 0;   // VPN 은 LAN 후보가 아니라 vpnIp 로 따로 보인다
            if (b[0] == 192 && b[1] == 168) return 4;
            if (b[0] == 10) return 3;
            if (b[0] == 172 && b[1] >= 16 && b[1] < 32) return 2;
            return 1;
        }
    }
}
