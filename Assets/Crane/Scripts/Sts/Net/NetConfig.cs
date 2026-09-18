namespace AIXRCrane.Crane.Sts.Net
{
    /// <summary>LAN 멀티플레이 전역 설정의 단일 출처(SSOT) — 포트·디스커버리 포트·정원·기본 대역을 한 곳에 모은다.
    /// 런타임 어셈블리에 둬 에디터 셋업도 자동 참조. 현장 와이파이 대역이 다르면 자동발견/수동입력이 우선.</summary>
    public static class NetConfig
    {
        /// <summary>UnityTransport 기본 포트 — 호스트 수신·관전자 접속·디스커버리 비콘 폴백 공통.</summary>
        public const ushort DefaultPort = 7777;

        /// <summary>호스트 자동발견 UDP 비콘 포트. 호스트/관전자 기기가 동일해야 발견된다.</summary>
        public const int DiscoveryPort = 47777;

        /// <summary>호스트 포함 최대 동시 인원. (NetLanSetup 메뉴 라벨은 어트리뷰트 const 제약상 숫자를 빼고 이 값을 SSOT로 삼는다.)</summary>
        public const int MaxPlayers = 5;

        /// <summary>로컬 IP에서 대역(prefix)을 못 뽑을 때의 폴백 서브넷. 다른 대역 현장에선 자동발견/수동입력으로 대체된다.</summary>
        public const string DefaultSubnetPrefix = "192.168.0.";

        /// <summary>기본 호스트 옥텟(마지막 자리) — 폴백/초기 추정용.</summary>
        public const int DefaultJoinOctet = 10;

        /// <summary>기본 참가 IP = DefaultSubnetPrefix + DefaultJoinOctet (= "192.168.0.10"). 폴백 기본값.</summary>
        public static readonly string DefaultJoinIp = DefaultSubnetPrefix + DefaultJoinOctet;
    }
}
