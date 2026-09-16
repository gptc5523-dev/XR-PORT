using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;          // InputDevices — Quest 컨트롤러 직접 읽기(기존 조종 컨트롤러와 동일 방식)
using Container.Crane.Sts;     // CraneHud, StsCraneVRController

namespace Container.Crane.Sts.Net
{
    /// <summary>
    /// 접속 전, 사용자 눈앞에 뜨는 "시작 화면" — 호스트(조종) / 참가(관전)를 컨트롤러로 선택.
    ///
    /// IMGUI(NetLanUI.OnGUI)는 VR 헤드셋에 안 그려지므로, 같은 접속 로직(NetLanUI.BeginHost/BeginClient)을
    /// 월드공간 Canvas + 컨트롤러 입력으로 대체한다. PC 에디터에선 NetLanUI의 IMGUI가 폴백으로 그대로 동작.
    ///
    ///   - 오른쪽 스틱 ↑↓ 로 선택, A(primaryButton)로 확정 (기존 조종 입력과 같은 InputDevices 방식)
    ///   - 메뉴가 떠 있는 동안은 StsCraneVRController를 꺼서 선택 입력이 크레인을 움직이지 않게 한다
    ///   - 접속되면(IsClient/IsServer) 메뉴를 숨기고, 호스트면 조종 컨트롤러를 다시 켠다(관전자는 꺼둔 채 유지)
    /// 씬에 안 붙여도 [RuntimeInitializeOnLoadMethod]로 자동 스폰. NetworkManager가 없으면 아무것도 안 한다.
    /// </summary>
    [AddComponentMenu("Container/Net/Crane Net Menu HUD")]
    [DisallowMultipleComponent]
    public sealed class CraneNetMenuHUD : MonoBehaviour
    {
        [Header("배치(카메라 로컬 좌표, m) — 머리 정면에 고정")]
        [SerializeField] Vector3 cameraOffset = new Vector3(0f, -0.02f, 1.2f);
        [SerializeField] float worldScale = 0.0016f;

        [Header("패널/텍스트")]
        [SerializeField] Color bgColor = new Color(0f, 0f, 0f, CraneHud.PanelBgAlpha);   // 패널 배경 알파 표준(공용 토큰)
        [SerializeField] int fontSize = 26;

        [Header("입력 임계값")]
        [SerializeField] float stickThreshold = 0.6f;
        [SerializeField] float stickReset = 0.3f;

        static readonly string[] Options = { "호스트 시작", "참가" };

        Canvas canvas;
        Text text;
        NetLanUI ui;
        StsCraneVRController craneController;
        bool controllerSuppressed;
        readonly List<Behaviour> lockedLoco = new List<Behaviour>();   // 메뉴 중 끈 로코모션(복구용)
        bool attached;
        int selected;
        bool stickLatchedX, stickLatchedY;
        bool aPrev, bPrev;
        float nextFind;
        bool ipEntryMode;          // 참가 선택 후, 호스트 IP(마지막 옥텟) 입력 중
        int joinOctet = NetConfig.DefaultJoinOctet;   // [H3] SSOT. 최종 IP = NetConfig.DefaultSubnetPrefix + joinOctet
        bool discoveredPrev;       // 자동 발견 엣지 검출(false→true 순간 한 번만 자동 접속)
        float connectedAt;         // 접속된 시각(무접속이면 0) — 결과 화면을 잠깐 띄우는 데만 쓴다
        const float ResultSeconds = 6f;   // 접속 결과를 보여주는 시간. 더 길면 시야를 가리고, 더 짧으면 헤드셋에서 놓친다
        readonly StringBuilder sb = new StringBuilder(256);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoSpawn() => CraneHud.EnsureSpawned<CraneNetMenuHUD>("NetMenuHUD");

        void Start()
        {
            BuildCanvas();
            TryAttach();
            SetVisible(false);   // NetworkManager 확인 전까진 숨김
        }

        void BuildCanvas()
        {
            canvas = CraneHud.BuildPanel(transform, "CraneNetMenuCanvas", new Vector2(520, 320), worldScale,
                bgColor, fontSize, Color.white, TextAnchor.UpperCenter, new Vector2(28, 22), out text,
                fitToText: true);
            text.text = "...";
        }

        // 카메라(머리)에 부착 — 머리를 따라 정면에 고정. 카메라가 늦게 뜨면 주기적으로 재시도.
        void TryAttach()
        {
            if (canvas == null || attached) return;
            var cam = Camera.main;
            if (cam == null) return;
            canvas.transform.SetParent(cam.transform, worldPositionStays: false);
            canvas.transform.localPosition = cameraOffset;
            CraneHud.FaceCameraChild(canvas.transform, cameraOffset);   // 카메라 향함 + 거울 해소(다른 HUD와 동일) — identity면 뒤집힘 위험
            attached = true;
        }

        void Update()
        {
            if (canvas == null) return;
            if (!attached) TryAttach();

            // NetworkManager/NetLanUI 없으면(=네트워킹 미구성) 메뉴 숨기고 크레인 입력 복구 후 종료.
            if (ui == null && Time.unscaledTime >= nextFind)
            {
                nextFind = Time.unscaledTime + 0.5f;
                ui = FindAnyObjectByType<NetLanUI>();
            }
            var nm = Unity.Netcode.NetworkManager.Singleton;
            if (ui == null || nm == null) { SetVisible(false); RestoreController(forceEnable: true); UnlockLocomotion(); return; }

            bool connected = nm.IsClient || nm.IsServer;
            if (connected)
            {
                // 접속됨 — 호스트면 조종 컨트롤러 복구(관전자는 CraneNetSync가 꺼둔 채 유지).
                // 이동(로코모션)은 모두 복구 — 호스트·관전자 모두 선택 후엔 돌아다닐 수 있게.
                // 종전엔 여기서 메뉴를 바로 숨겨, 성공했는지 실패했는지는커녕 '눌리긴 했는지'도 알 수 없었다
                //   (오너 2026-09-16 "호스트 참가가 안 된다" — 서버 관측상 아무도 호스트를 누르지 않은 상태였다).
                //   접속 직후 ResultSeconds 동안만 결과를 보여주고 닫는다 — 시야를 계속 가리지 않게.
                if (connectedAt <= 0f) connectedAt = Time.unscaledTime;
                bool showResult = Time.unscaledTime - connectedAt < ResultSeconds;
                SetVisible(showResult);
                if (showResult) text.text = BuildConnectedText(nm);
                RestoreController(forceEnable: nm.IsServer);
                UnlockLocomotion();
                return;
            }
            connectedAt = 0f;   // 끊기면 다음 접속에서 다시 보여준다

            // 접속 전 — 메뉴 표시 + 크레인 입력/이동 모두 차단 + 선택/확정 처리.
            SetVisible(true);
            SuppressController();
            LockLocomotion();
            HandleInput();
            text.text = BuildText();
        }

        void HandleInput()
        {
            var right = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            if (!right.isValid) return;

            Vector2 rs = Vector2.zero;
            right.TryGetFeatureValue(CommonUsages.primary2DAxis, out rs);
            bool aNow = right.TryGetFeatureValue(CommonUsages.primaryButton, out bool a) && a;
            bool bNow = right.TryGetFeatureValue(CommonUsages.secondaryButton, out bool b) && b;

            // 스틱 엣지 검출(축별) — 한 번 기울일 때 한 스텝.
            int stepY = 0, stepX = 0;
            if (!stickLatchedY && Mathf.Abs(rs.y) >= stickThreshold) { stepY = rs.y > 0f ? +1 : -1; stickLatchedY = true; }
            else if (Mathf.Abs(rs.y) <= stickReset) stickLatchedY = false;
            if (!stickLatchedX && Mathf.Abs(rs.x) >= stickThreshold) { stepX = rs.x > 0f ? +1 : -1; stickLatchedX = true; }
            else if (Mathf.Abs(rs.x) <= stickReset) stickLatchedX = false;

            if (!ipEntryMode)
            {
                // 선택 화면 — 스틱 ↑↓ 로 호스트/참가 이동, A로 확정.
                if (stepY != 0) selected = Mathf.Clamp(selected - stepY, 0, Options.Length - 1);  // ↑=위 항목
                if (aNow && !aPrev)
                {
                    if (selected == 0) ui.BeginHost();              // 호스트는 바로 시작
                    else if (ui.HostDiscovered)                     // 참가 — 이미 자동 발견됐으면 IP 입력 없이 즉시 접속
                    {
                        discoveredPrev = true;
                        ui.BeginClient();
                    }
                    else { ipEntryMode = true; joinOctet = GuessOctet(); }  // 미발견 → 수동 입력 폴백
                }
            }
            else
            {
                // IP 입력(수동 폴백) 화면 — 그 사이 호스트가 자동 발견되면(엣지) 즉시 접속.
                bool justDiscovered = ui.HostDiscovered && !discoveredPrev;
                discoveredPrev = ui.HostDiscovered;
                if (justDiscovered)
                {
                    ui.BeginClient();   // ui.JoinIp 는 LanDiscovery가 이미 호스트 IP로 채움
                }
                else
                {
                    // ↑↓ ±1, ←→ ±10 으로 마지막 숫자 맞춤. A 접속, B 뒤로.
                    if (stepY != 0) joinOctet = Mathf.Clamp(joinOctet + stepY, 0, 255);
                    if (stepX != 0) joinOctet = Mathf.Clamp(joinOctet + stepX * 10, 0, 255);
                    if (aNow && !aPrev)
                    {
                        ui.JoinIp = IpPrefix() + joinOctet;   // [대역prefix].[joinOctet] (prefix는 IpPrefix(), 폴백 NetConfig.DefaultSubnetPrefix)
                        ui.BeginClient();
                    }
                    else if (bNow && !bPrev) ipEntryMode = false;   // 선택 화면으로 복귀
                }
            }

            aPrev = aNow; bPrev = bNow;
        }

        // 참가 기기 자신의 IP에서 대역(prefix) 추출 — 같은 와이파이면 호스트와 같은 대역.
        string IpPrefix()
        {
            string ip = ui != null ? ui.LocalIp : null;
            int dot = string.IsNullOrEmpty(ip) ? -1 : ip.LastIndexOf('.');
            return dot > 0 ? ip.Substring(0, dot + 1) : NetConfig.DefaultSubnetPrefix;   // [H3] 폴백 대역 SSOT
        }

        // 자동탐색으로 채워진 JoinIp가 있으면 그 마지막 숫자를, 없으면 1을 초기값으로.
        int GuessOctet()
        {
            string ip = ui != null ? ui.JoinIp : null;
            int dot = string.IsNullOrEmpty(ip) ? -1 : ip.LastIndexOf('.');
            if (dot > 0 && int.TryParse(ip.Substring(dot + 1), out int n) && n >= 0 && n <= 255) return n;
            return 1;
        }

        // 크레인 조종 입력 차단/복구
        void SuppressController()
        {
            if (controllerSuppressed) return;
            // 켜진 조종기(Active)를 막는다 — FindAnyObjectByType 은 크레인이 여러 대면 감독이 꺼 둔 RTG 조종기를 집기도 해서,
            //   호스트 접속 때 그걸 강제로 켜 조종기가 두 대 켜졌다(2026-09-15 RtgControlSmoke: 시작 직후 Active=RTG 크레인_1).
            if (craneController == null) craneController = StsCraneVRController.Active != null ? StsCraneVRController.Active : FindAnyObjectByType<StsCraneVRController>();
            if (craneController != null) { craneController.enabled = false; controllerSuppressed = true; }
        }

        void RestoreController(bool forceEnable)
        {
            if (!controllerSuppressed) return;
            if (forceEnable && craneController != null) craneController.enabled = true;
            controllerSuppressed = false;   // 관전자(forceEnable=false)는 꺼둔 채로 둠
        }

        // 이동(로코모션) 잠금/복구
        // 메뉴 떠 있는 동안 씬의 모든 XR LocomotionProvider(이동/텔레포트/회전)를 끈다.
        // StsCraneVRController.ApplyMode와 같은 방식 — 내가 끈 것만 기억했다가 그대로 복구.
        void LockLocomotion()
        {
            if (lockedLoco.Count > 0) return;   // 이미 잠금 상태면 재스캔 불필요
            var locoType = LocomotionProviderType;
            if (locoType == null) return;
            foreach (var o in FindObjectsByType(locoType, FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (o is Behaviour b && b.enabled) { b.enabled = false; lockedLoco.Add(b); }
        }

        void UnlockLocomotion()
        {
            if (lockedLoco.Count == 0) return;
            foreach (var b in lockedLoco) if (b != null) b.enabled = true;
            lockedLoco.Clear();
        }

        static bool _locoSearched;
        static System.Type _locoType;
        static System.Type LocomotionProviderType
        {
            get
            {
                if (_locoSearched) return _locoType;
                _locoSearched = true;
                _locoType =
                    System.Type.GetType("UnityEngine.XR.Interaction.Toolkit.Locomotion.LocomotionProvider, Unity.XR.Interaction.Toolkit")
                    ?? System.Type.GetType("UnityEngine.XR.Interaction.Toolkit.LocomotionProvider, Unity.XR.Interaction.Toolkit");
                return _locoType;
            }
        }

        void SetVisible(bool v)
        {
            if (canvas != null && canvas.gameObject.activeSelf != v) canvas.gameObject.SetActive(v);
        }

        // 접속 직후 결과 화면 — 내가 호스트인지 관전인지, 운전이 되는지를 한눈에.
        //   관전자가 "왜 크레인이 안 움직이지"로 막히지 않게 운전 권한을 여기서 명시한다(오너 규칙: 운전은 호스트만).
        string BuildConnectedText(Unity.Netcode.NetworkManager nm)
        {
            sb.Clear();
            sb.AppendLine("<b><size=30>STS 크레인 멀티플레이</size></b>");
            sb.AppendLine();
            if (nm.IsServer)
            {
                int joined = Mathf.Max(0, nm.ConnectedClientsIds.Count - 1);   // 호스트 자신(0번)은 참가자 수에서 뺀다
                sb.AppendLine("<color=#5FE0FF><b>호스트 중 · 운전 가능</b></color>");
                sb.AppendLine($"<size=18>내 IP <b>{ui.LocalIp}</b> · 참가 {joined}/{Mathf.Max(0, ui.MaxPlayers - 1)}</size>");
                sb.AppendLine();
                sb.AppendLine("<size=16><color=#999999>참가자에게 이 IP 를 불러주세요</color></size>");
            }
            else
            {
                sb.AppendLine("<color=#5FE0FF><b>관전 중</b></color>");
                sb.AppendLine($"<size=18>호스트 <b>{ui.JoinIp}</b></size>");
                sb.AppendLine();
                sb.AppendLine("<size=16><color=#999999>운전은 호스트만 할 수 있어요</color></size>");
            }
            return sb.ToString();
        }

        string BuildText()
        {
            sb.Clear();
            sb.AppendLine("<b><size=30>STS 크레인 멀티플레이</size></b>");
            sb.AppendLine();

            if (ipEntryMode)
            {
                // 호스트 IP 입력 화면(수동 폴백)
                sb.AppendLine("<color=#5FE0FF><b>호스트 IP 입력</b></color>");
                sb.AppendLine();
                if (ui.HostDiscovered)
                {
                    sb.AppendLine($"<size=18><color=#5FE0FF>호스트 발견 <b>{ui.JoinIp}</b> · 접속 중...</color></size>");
                }
                else
                {
                    sb.AppendLine($"<size=34><b>{IpPrefix()}<color=#5FE0FF>{joinOctet}</color></b></size>");
                    sb.AppendLine();
                    sb.AppendLine("<size=16><color=#999999>호스트 '내 IP' 끝자리에 맞추세요</color></size>");
                    sb.AppendLine();
                    sb.AppendLine("<size=15><color=#999999>스틱 ↑↓ ±1 · ←→ ±10 · A 접속 · B 뒤로</color></size>");
                }
                return sb.ToString();
            }

            for (int i = 0; i < Options.Length; i++)
            {
                if (i == selected) sb.AppendLine($"<color=#5FE0FF><b>▸ {Options[i]} ◂</b></color>");
                else               sb.AppendLine($"<color=#999999>{Options[i]}</color>");
            }
            sb.AppendLine();
            // 호스트 시작 실패 안내 — 한 머신에 인스턴스가 여러 개면(서버 5개) 먼저 뜬 쪽이 포트를 쥐어 두 번째는 반드시 실패한다.
            //   종전엔 버튼을 눌러도 화면이 그대로라 오너가 "호스트 참가가 안 된다"로 막혔다. 막힌 자리에서 다음 행동까지 알려준다.
            //   (#EB332E = HudColor.Danger, #5FE0FF = Accent — 이 파일의 다른 줄과 같은 표기)
            if (ui.HostFailed)
            {
                sb.AppendLine("<size=17><color=#EB332E><b>호스트 시작 실패</b> — 이 PC 에서 다른 인스턴스가 이미 호스트 중입니다.</color></size>");
                sb.AppendLine("<size=16><color=#5FE0FF>'참가'를 고르면 그 세계에 들어갑니다 · 관전이라 운전은 호스트만 할 수 있어요.</color></size>");
                sb.AppendLine();
            }
            if (selected == 0)
                sb.AppendLine($"<size=16><color=#999999>내 IP <b>{ui.LocalIp}</b> · 최대 {ui.MaxPlayers}인</color></size>");
            else if (ui.HostDiscovered)
                sb.AppendLine($"<size=16><color=#5FE0FF>호스트 발견됨 · A로 접속</color></size>");
            else
                sb.AppendLine($"<size=16><color=#999999>호스트를 자동으로 찾습니다</color></size>");
            sb.AppendLine();
            sb.AppendLine("<size=15><color=#999999>스틱 ↑↓ 선택 · A 확정</color></size>");
            return sb.ToString();
        }
    }
}
