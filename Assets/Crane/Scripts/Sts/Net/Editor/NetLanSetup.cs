using System.IO;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AIXRCrane.Crane.Sts.Net.EditorTools
{
    /// <summary>LAN 멀티플레이 씬 배선 자동 구성 — NetworkManager·CraneNetSync·참가자 아바타 프리팹(메시 없는
    /// 껍데기, Body 밑에 채우면 표시). 라벨의 "5"=NetConfig.MaxPlayers, 정원 바꾸면 라벨도 같이 고칠 것.</summary>
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
            var nm = Object.FindAnyObjectByType<NetworkManager>();
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
            nm.NetworkConfig.ConnectionApproval = true;   // 인원 제한용 — 실제 정원 검사는 NetLanUI.ApproveConnection

            EditorUtility.SetDirty(nm);
            return nm;
        }

        /// <summary>참가자 아바타 프리팹 — 없으면 만들고 있으면 그대로 쓴다(메시 작업분 보존).
        /// NetworkObject·NetworkTransform(소유자 권위)·PlayerAvatarSync, localScale=ModelScale(1/24), Sync Scale은 꺼둔다.</summary>
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
            if (Object.FindAnyObjectByType<CraneNetSync>() != null) return;
            var go = new GameObject("CraneNetSync");
            go.AddComponent<NetworkObject>();
            go.AddComponent<CraneNetSync>();
            EditorUtility.SetDirty(go);
        }
    }
}
