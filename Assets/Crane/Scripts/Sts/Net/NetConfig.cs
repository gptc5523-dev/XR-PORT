namespace AIXRCrane.Crane.Sts.Net
{
    /// <summary>LAN 멀티플레이 전역 설정 SSOT — 포트·디스커버리 포트·정원·기본 대역.
    /// 현장 와이파이 대역이 다르면 자동발견/수동입력이 우선.</summary>
    public static class NetConfig
    {
        /// <summary>UnityTransport 기본 포트 — 호스트 수신·관전자 접속·디스커버리 비콘 폴백 공통.</summary>
        public const ushort DefaultPort = 7777;

        /// <summary>호스트 자동발견 UDP 비콘 포트. 호스트/관전자 기기가 동일해야 발견된다.</summary>
        public const int DiscoveryPort = 47777;

        /// <summary>호스트 포함 최대 동시 인원(NetLanSetup 메뉴 라벨도 이 값을 SSOT로).</summary>
        public const int MaxPlayers = 5;

        /// <summary>로컬 IP에서 대역을 못 뽑을 때의 폴백 서브넷.</summary>
        public const string DefaultSubnetPrefix = "192.168.0.";

        /// <summary>기본 호스트 옥텟(마지막 자리) — 폴백/초기 추정용.</summary>
        public const int DefaultJoinOctet = 10;

        /// <summary>기본 참가 IP = DefaultSubnetPrefix + DefaultJoinOctet (= "192.168.0.10"). 폴백 기본값.</summary>
        public static readonly string DefaultJoinIp = DefaultSubnetPrefix + DefaultJoinOctet;
    }
}
