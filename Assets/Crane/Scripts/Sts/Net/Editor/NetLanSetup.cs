using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Container.Crane.Sts.Net.EditorTools
{
    /// <summary>
    /// LAN 멀티플레이 씬 배선을 한 번에 자동 구성:
    ///   1) 플레이어 아바타 프리팹(머리+양손) 생성 → Assets/Crane/Net/PlayerAvatar.prefab
    ///   2) 씬에 NetworkManager(+UnityTransport, NetLanUI, LanDiscovery) 생성·연결
    ///   3) 씬에 CraneNetSync(+NetworkObject) 생성
    /// 메뉴: Container ▸ LAN 멀티플레이 셋업 (5인)
    /// ※ 라벨의 "5" = NetConfig.MaxPlayers(고정 정원 5명). MenuItem 어트리뷰트는 const만 받아 자동참조가
    ///   불가하므로 라벨엔 숫자를 직접 적는다. 정원을 바꾸면 NetConfig.MaxPlayers와 이 라벨을 함께 수정할 것.
    ///
    /// 손으로 NetworkObject를 붙이는 실수 위험을 없앤다. 셋업 후 씬을 저장하면 끝.
    /// </summary>
    public static class NetLanSetup
    {
        const string PrefabDir = "Assets/Crane/Net";
        const string PrefabPath = PrefabDir + "/PlayerAvatar.prefab";

        [MenuItem("Scene/LAN 멀티플레이 셋업 (5인)", false, 60)]   // "5" = NetConfig.MaxPlayers (어트리뷰트 const 제약상 직접 표기)
        public static void Setup()
        {
            var avatar = CreateOrLoadAvatarPrefab();
            var nm = SetupNetworkManager(avatar);
            SetupCraneSync();

            EditorSceneManager.MarkSceneDirty(nm.gameObject.scene);
            EditorUtility.DisplayDialog("LAN 멀티플레이 셋업 완료",
                "NetworkManager / 플레이어 아바타 / CraneNetSync 구성이 끝났습니다.\n\n" +
                "씬을 저장(Ctrl+S)한 뒤, 빌드해서 같은 와이파이의 기기들에서:\n" +
                " • 조종자 = '호스트 시작'\n • 관전자 = 호스트 IP로 '참가'\n\n" +
                "테스트 후 동작을 알려주세요(현재 미검증 상태).", "확인");
        }

        // ───────── 1) 아바타 프리팹 ─────────
        // 매끈한 차콜 픽토그램(이목구비 없는 3D 마네킹) — 머리+양손. 언캐니 회피 위해 단순 조형.
        // 매번 새로 만들어 같은 경로에 덮어쓴다(GUID 유지 → NetworkManager.PlayerPrefab 참조 보존).
        // 디자인을 바꾸려면 이 함수의 오프셋/스케일/색을 수정 후 셋업 메뉴를 다시 실행.
        static GameObject CreateOrLoadAvatarPrefab()
        {
            if (!AssetDatabase.IsValidFolder(PrefabDir))
                AssetDatabase.CreateFolder("Assets/Crane", "Net");

            // 매끈한 차콜 픽토그램(이목구비 없는 3D 마네킹 스타일). 머리가 참가자색 tint 대상.
            var CHARCOAL = new Color(0.20f, 0.21f, 0.24f);

            var root = new GameObject("PlayerAvatar");
            root.AddComponent<NetworkObject>();
            var sync = root.AddComponent<PlayerAvatarSync>();

            // 머리(매끈한 구) — 회전이 목에 전달되도록 부모. 참가자색은 이 머리에 칠해짐(tintTarget).
            var head = MakeVisual(PrimitiveType.Sphere, "Head", root.transform,
                Vector3.zero, new Vector3(0.198f, 0.214f, 0.198f), CHARCOAL, 0.55f);
            var H = head.transform;

            // 짧은 목(머리 아래) — 머리 로컬 기준
            MakeVisual(PrimitiveType.Sphere, "Neck", H,
                new Vector3(0f, -0.4907f, 0.0505f), new Vector3(0.4545f, 0.4673f, 0.4545f), CHARCOAL, 0.45f);

            // 양손(둥근 주먹) — 위치는 런타임에 컨트롤러를 따라가므로 형상만, localPos는 0.
            var lh = MakeVisual(PrimitiveType.Sphere, "LeftHand",  root.transform, Vector3.zero, new Vector3(0.11f, 0.10f, 0.132f), CHARCOAL, 0.5f);
            var rh = MakeVisual(PrimitiveType.Sphere, "RightHand", root.transform, Vector3.zero, new Vector3(0.11f, 0.10f, 0.132f), CHARCOAL, 0.5f);

            // 직렬화 private 필드 연결
            var so = new SerializedObject(sync);
            so.FindProperty("head").objectReferenceValue = head.transform;
            so.FindProperty("leftHand").objectReferenceValue = lh.transform;
            so.FindProperty("rightHand").objectReferenceValue = rh.transform;
            so.FindProperty("tintTarget").objectReferenceValue = head.GetComponent<Renderer>();
            so.ApplyModifiedPropertiesWithoutUndo();

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        static GameObject MakeVisual(PrimitiveType type, string name, Transform parent,
                                     Vector3 localPos, Vector3 localScale, Color color, float smoothness = 0.5f)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);   // 아바타는 물리 충돌 없음
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;

            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = color };
            mat.SetFloat("_Smoothness", smoothness);   // 매끈한 광택(픽토그램 느낌)
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        // ───────── 2) NetworkManager ─────────
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
            nm.NetworkConfig.PlayerPrefab = avatarPrefab;
            nm.NetworkConfig.ConnectionApproval = true;   // 인원 제한(NetConfig.MaxPlayers)용 — 실제 정원 검사는 NetLanUI.ApproveConnection

            EditorUtility.SetDirty(nm);
            return nm;
        }

        // ───────── 3) CraneNetSync ─────────
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
