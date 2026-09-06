using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Container.Crane.Sts.Net.EditorTools
{
    /// <summary>
    /// LAN 멀티플레이 씬 배선을 한 번에 자동 구성:
    ///   1) 씬에 NetworkManager(+UnityTransport, NetLanUI, LanDiscovery) 생성·연결
    ///   2) 씬에 CraneNetSync(+NetworkObject) 생성
    /// 메뉴: Scene ▸ LAN 멀티플레이 셋업 (5인)
    /// ※ 라벨의 "5" = NetConfig.MaxPlayers(고정 정원 5명). MenuItem 어트리뷰트는 const만 받아 자동참조가
    ///   불가하므로 라벨엔 숫자를 직접 적는다. 정원을 바꾸면 NetConfig.MaxPlayers와 이 라벨을 함께 수정할 것.
    ///
    /// 원격 플레이어는 시각 아바타 없이 접속·크레인 상태만 공유한다(아바타 제거됨 → PlayerPrefab 미지정).
    /// 손으로 NetworkObject를 붙이는 실수 위험을 없앤다. 셋업 후 씬을 저장하면 끝.
    /// </summary>
    public static class NetLanSetup
    {
        [MenuItem("Scene/LAN 멀티플레이 셋업 (5인)", false, 60)]   // "5" = NetConfig.MaxPlayers (어트리뷰트 const 제약상 직접 표기)
        public static void Setup()
        {
            var nm = SetupNetworkManager();
            SetupCraneSync();

            EditorSceneManager.MarkSceneDirty(nm.gameObject.scene);
            EditorUtility.DisplayDialog("LAN 멀티플레이 셋업 완료",
                "NetworkManager / CraneNetSync 구성이 끝났습니다.\n\n" +
                "씬을 저장(Ctrl+S)한 뒤, 빌드해서 같은 와이파이의 기기들에서:\n" +
                " • 조종자 = '호스트 시작'\n • 관전자 = 호스트 IP로 '참가'\n\n" +
                "원격 플레이어 아바타는 표시되지 않습니다(접속·크레인 동기화만).", "확인");
        }

        // NetworkManager
        static NetworkManager SetupNetworkManager()
        {
            var nm = Object.FindFirstObjectByType<NetworkManager>();
            if (nm == null)
            {
                var go = new GameObject("NetworkManager");
                nm = go.AddComponent<NetworkManager>();
            }
            var utp = nm.GetComponent<UnityTransport>();
            if (utp == null) utp = nm.gameObject.AddComponent<UnityTransport>();
            if (nm.GetComponent<NetLanUI>() == null) nm.gameObject.AddComponent<NetLanUI>();
            if (nm.GetComponent<LanDiscovery>() == null) nm.gameObject.AddComponent<LanDiscovery>();

            if (nm.NetworkConfig == null) nm.NetworkConfig = new NetworkConfig();
            nm.NetworkConfig.NetworkTransport = utp;
            nm.NetworkConfig.PlayerPrefab = null;         // 원격 아바타 제거 — 접속 시 플레이어 오브젝트를 스폰하지 않음
            nm.NetworkConfig.ConnectionApproval = true;   // 인원 제한(NetConfig.MaxPlayers)용 — 실제 정원 검사는 NetLanUI.ApproveConnection

            EditorUtility.SetDirty(nm);
            return nm;
        }

        // CraneNetSync
        static void SetupCraneSync()
        {
            if (Object.FindFirstObjectByType<CraneNetSync>() != null) return;
            var go = new GameObject("CraneNetSync");
            go.AddComponent<NetworkObject>();
            go.AddComponent<CraneNetSync>();
            EditorUtility.SetDirty(go);
        }
    }
}
