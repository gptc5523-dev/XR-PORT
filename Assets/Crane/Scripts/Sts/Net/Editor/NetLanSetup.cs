using System.IO;
using Unity.Netcode;
using Unity.Netcode.Components;
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
    ///   3) 참가자 아바타 프리팹 생성·연결(PlayerPrefab) — 접속하면 1인당 하나씩 자동 스폰
    /// 메뉴: Scene ▸ LAN 멀티플레이 셋업 (5인)
    /// ※ 라벨의 "5" = NetConfig.MaxPlayers(고정 정원 5명). MenuItem 어트리뷰트는 const만 받아 자동참조가
    ///   불가하므로 라벨엔 숫자를 직접 적는다. 정원을 바꾸면 NetConfig.MaxPlayers와 이 라벨을 함께 수정할 것.
    ///
    /// 아바타는 '메시 없는 껍데기'로 만들어진다 — Body 자식이 메시 자리다(오너 방침: 메시는 지시가 있을 때만).
    /// 메시를 Body 밑에 넣기만 하면 그대로 보인다. 비어 있어도 접속·위치 동기화는 정상 동작한다.
    /// 손으로 NetworkObject를 붙이는 실수 위험을 없앤다. 셋업 후 씬을 저장하면 끝.
    /// </summary>
    public static class NetLanSetup
    {
        /// <summary>참가자 아바타 프리팹 경로. 메시를 붙일 자리(Body)는 이 프리팹 안에 있다.</summary>
        public const string AvatarPath = "Assets/Crane/Net/PlayerAvatar.prefab";

        [MenuItem("Scene/LAN 멀티플레이 셋업 (5인)", false, 60)]   // "5" = NetConfig.MaxPlayers (어트리뷰트 const 제약상 직접 표기)
        public static void Setup()
        {
            var avatar = SetupAvatarPrefab();
            var nm = SetupNetworkManager(avatar);
            SetupCraneSync();

            EditorSceneManager.MarkSceneDirty(nm.gameObject.scene);
            EditorUtility.DisplayDialog("LAN 멀티플레이 셋업 완료",
                "NetworkManager / CraneNetSync 구성이 끝났습니다.\n\n" +
                "씬을 저장(Ctrl+S)한 뒤, 빌드해서 같은 와이파이의 기기들에서:\n" +
                " • 조종자 = '호스트 시작'\n • 관전자 = 호스트 IP로 '참가'\n\n" +
                "참가자 아바타: " + AvatarPath + "\n" +
                "  · 접속 인원마다 하나씩 자동 스폰됩니다.\n" +
                "  · 메시 자리는 Body 자식입니다 — 지금은 비어 있어 화면엔 안 보입니다.\n" +
                "  · 메시를 Body 밑에 넣으면 그대로 표시됩니다.", "확인");
        }

        // NetworkManager
        static NetworkManager SetupNetworkManager(GameObject avatarPrefab)
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
            nm.NetworkConfig.PlayerPrefab = avatarPrefab; // 접속 1인당 아바타 하나 자동 스폰(AutoSpawnPlayerPrefabClientSide)
            nm.NetworkConfig.ConnectionApproval = true;   // 인원 제한(NetConfig.MaxPlayers)용 — 실제 정원 검사는 NetLanUI.ApproveConnection

            EditorUtility.SetDirty(nm);
            return nm;
        }

        /// <summary>참가자 아바타 프리팹 — 없으면 만든다. 이미 있으면 그대로 쓴다(메시 작업분을 덮어쓰지 않는다).
        ///
        /// 구성
        ///   PlayerAvatar            NetworkObject · NetworkTransform(소유자 권위) · PlayerAvatarSync
        ///                           localScale = ModelScale — 리그가 1/24 라 아바타도 미니어처여야 크기가 맞는다
        ///     └ Body                메시 자리(비어 있음)
        ///
        /// 스케일은 프리팹에 박혀 있어 매 프레임 보낼 이유가 없다 → Sync Scale 을 끈다(대역폭 절약).</summary>
        static GameObject SetupAvatarPrefab()
        {
            var found = AssetDatabase.LoadAssetAtPath<GameObject>(AvatarPath);
            if (found != null) return found;

            Directory.CreateDirectory(Path.GetDirectoryName(AvatarPath));

            var root = new GameObject("PlayerAvatar");
            root.transform.localScale = Vector3.one * StsConfig.ModelScale;
            root.AddComponent<NetworkObject>();

            var nt = root.AddComponent<NetworkTransform>();
            nt.AuthorityMode = NetworkTransform.AuthorityModes.Owner;   // 각자 자기 아바타를 움직인다
            nt.SyncScaleX = nt.SyncScaleY = nt.SyncScaleZ = false;
            nt.Interpolate = true;                                      // 남의 아바타가 끊겨 보이지 않게

            var sync = root.AddComponent<PlayerAvatarSync>();

            var body = new GameObject("Body");
            body.transform.SetParent(root.transform, worldPositionStays: false);

            // private [SerializeField] 라 SerializedObject 로 연결한다 — 공개 API 를 늘리지 않는다.
            var so = new SerializedObject(sync);
            so.FindProperty("body").objectReferenceValue = body.transform;
            so.ApplyModifiedPropertiesWithoutUndo();

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, AvatarPath);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            return prefab;
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
