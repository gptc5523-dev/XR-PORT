using System.Collections.Generic;
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
        [SerializeField] ushort port = NetConfig.DefaultPort;            // [H3] SSOT
        [Tooltip("호스트 포함 최대 동시 인원")]
        [SerializeField] int maxPlayers = NetConfig.MaxPlayers;          // [H3] SSOT
        [SerializeField] string joinIp = NetConfig.DefaultJoinIp;        // [H3] SSOT (static readonly — 인스턴스 필드 초기자에서 정적 멤버 참조는 합법)

        string localIp = "...";
        bool hostDiscovered;
        bool hostFailed;

        // 승인됐지만 아직 ConnectedClientsIds에 합류 전인 클라이언트들 — 동시 접속 시 정원 초과 레이스 방지용.
        readonly HashSet<ulong> pendingApprovals = new();

        // VR 시작 메뉴(CraneNetMenuHUD)가 읽는 정보/조작 진입점.
        public string LocalIp => localIp;
        public int MaxPlayers => maxPlayers;
        public string JoinIp { get => joinIp; set { if (!string.IsNullOrEmpty(value)) joinIp = value; } }
        /// <summary>LanDiscovery가 호스트 비콘을 받아 JoinIp를 자동 설정했는지(자동 접속 트리거용).</summary>
        public bool HostDiscovered => hostDiscovered;
        /// <summary>직전 '호스트 시작'이 실패했는지 — 대개 같은 PC의 다른 인스턴스가 이미 포트를 쥐고 있는 경우.
        /// VR 시작 메뉴(CraneNetMenuHUD)가 사용자에게 알리는 데 쓴다.</summary>
        public bool HostFailed => hostFailed;

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

        /// <summary>세션을 끊고 시작 메뉴로 — 바닥 '나가는 존'(<see cref="ExitZone"/>)과 헤드셋 이탈 감시가 부른다.
        /// ★ 서버의 Crane.exe 는 헤드셋이 빠져도 계속 살아 있다. 이걸 안 부르면 그 인스턴스가 포트(7777)를
        ///   쥔 채 남아 다음 '호스트 시작'이 반드시 실패한다(오너 2026-09-16 "호스트 세션이 안 끊긴다").
        /// IMGUI 의 '연결 끊기'와 같은 동작이지만, 그쪽은 헤드셋에 안 그려져 VR 에서는 쓸 수 없었다.</summary>
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

            // 최대 인원 제한 — 승인 콜백(서버에서 실행). 정원 초과 시 거부.
            nm.NetworkConfig.ConnectionApproval = true;
            pendingApprovals.Clear();
            nm.ConnectionApprovalCallback = ApproveConnection;
            // 승인했지만 아직 합류 전인 인원을 추적해 동시 접속 시 정원 초과를 막는다(중복 구독 방지로 먼저 해제).
            nm.OnClientConnectedCallback  -= OnClientJoined; nm.OnClientConnectedCallback  += OnClientJoined;
            nm.OnClientDisconnectCallback -= OnClientLeft;   nm.OnClientDisconnectCallback += OnClientLeft;

            Transport.SetConnectionData("0.0.0.0", port, "0.0.0.0");   // 모든 인터페이스에서 수신
            // 한 머신에 인스턴스를 여러 개 띄우면(서버 5개 구성) 먼저 뜬 쪽이 포트를 쥐므로 두 번째 호스트는 반드시 실패한다.
            //   StartHost 는 실패를 false 로만 알리고 사유는 서버 로그(UnityTransport start failure)에만 남아,
            //   헤드셋에서는 버튼을 눌러도 아무 일도 안 일어난 것처럼 보였다(오너 2026-09-16 "호스트 참가가 안 된다").
            //   결과를 남겨 시작 메뉴가 사용자에게 알리고 '참가'로 안내하게 한다.
            hostFailed = !nm.StartHost();
        }

        // 정원 검사 — ConnectedClientsIds.Count만 보면 거의 동시에 들어온 두 요청이 둘 다 통과해 정원을
        //   초과할 수 있다(승인~합류 사이 Count가 갱신 전). '승인했지만 합류 전' 인원(pendingApprovals)을
        //   함께 더해 비교하면 레이스에도 초과되지 않는다.
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

        void OnClientJoined(ulong clientId) => pendingApprovals.Remove(clientId);   // 합류 완료 → 이제 ConnectedClientsIds로 카운트됨
        void OnClientLeft(ulong clientId)   => pendingApprovals.Remove(clientId);   // 합류 전 이탈한 예약 정리(이미 합류했으면 set에 없어 무해)

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

            // ConnectionApproval은 에디터 셋업(NetLanSetup)에서 이미 true로 구워져 호스트와 일치한다.
            //   런타임에 토글하면 설정 해시 불일치로 거부될 위험이 있어 건드리지 않는다.
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
