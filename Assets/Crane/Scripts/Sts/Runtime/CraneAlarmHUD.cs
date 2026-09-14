using UnityEngine;
using UnityEngine.UI;
using Container.Crane.Sts.Net;   // CraneNetSync(자식 네임스페이스) 참조

namespace Container.Crane.Sts
{
    /// <summary>
    /// 크레인 활성 알람을 시야 '상단 중앙'에 크게 띄우는 공유 경보 배너.
    ///   - 상태 패널(CraneStatusHUD)과 달리 호스트·관전자 '전원'에게 보인다 — 알람은 안전 신호라 모두 봐야 함.
    ///     (그래서 IsServer·조종모드 게이트가 없다. 오직 '활성 알람이 있을 때만' 표시.)
    ///   - 네트워크 세션이면 CraneNetSync.NetAlarmCode(호스트 권위)를 읽어 호스트=관전자 동일 표시.
    ///     단독 실행(네트워크 없음/미접속)이면 로컬에서 CraneFault.Evaluate로 직접 판정.
    ///   - 코드 → 메시지/심각도는 AlarmCodebook(SSOT)에서 조회. 알람 없으면 캔버스를 끈다.
    /// HMD 카메라 자식으로 붙어 머리를 돌려도 정면 상단에 고정(head-locked). 발생 시 심각도색 배경 + 펄스로 주의를 끈다.
    /// 씬 어디든 한 곳에 붙이면 됨(없으면 자동 스폰). crane을 비우면 자동 탐색.
    /// </summary>
    [AddComponentMenu("Container/STS Crane/Crane Alarm HUD")]
    [DisallowMultipleComponent]
    public sealed class CraneAlarmHUD : MonoBehaviour
    {
        [Header("참조")]
        [SerializeField] StsCrane crane;
        [Tooltip("이 카메라에 자식으로 붙음. 비우면 Camera.main 자동 사용.")]
        [SerializeField] Camera targetCamera;

        [Header("HMD 위치 (카메라 로컬 좌표, m) — 역할별")]
        // 참가자(관전): 정면 약간 위(z=0.9 기준 ≈8°) '한가운데' — 운전 HUD가 없으니 시야 중앙이 제일 잘 보임.
        [SerializeField] Vector3 hmdOffset = new Vector3(0f, 0.13f, CraneHud.HudDistance);
        // 호스트(조종): 운전 HUD(상태판 우상단 0.22,0.09)와 겹치지 않게 더 '위'로 올린 중앙.
        //   상태판 윗변보다 높이 띄워 운전 시야를 침범하지 않음. 알람 있을 때만 뜸. (실기기 보고 미세조정)
        [SerializeField] Vector3 hostHmdOffset = new Vector3(0f, 0.24f, CraneHud.HudDistance);
        [SerializeField, Range(-30f, 30f)] float tiltPitchDeg = -4f;

        [Header("배너")]
        [SerializeField] Vector2 panelPixels = new Vector2(640f, 96f);   // fitToText라 실제론 글자에 맞춰 자동
        [SerializeField] float worldScale = 0.0009f;        // 참가자(시야 중앙) 크기 — 운전 HUD 없으니 크게
        [SerializeField] float hostWorldScale = 0.0006f;    // 호스트: 운전 HUD(상태판)와 안 겹치게 더 작게
        [SerializeField] int fontSize = 48;   // ⚠ 아이콘 단독이라 크게

        Canvas canvas;
        Text text;
        string lastText;
        Color curSev = CraneHud.HudColor.Danger;   // 현재 알람 심각도색(펄스가 이 색의 알파만 흔듦)
        float nextRefresh;

        // 씬 로드 시 자동 스폰 — 수동 부착 안 해도 동작. 이미 있으면 스킵.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoSpawn() => CraneHud.EnsureSpawned<CraneAlarmHUD>("ALARM");

        void Start()
        {
            if (crane == null) crane = FindAnyObjectByType<StsCrane>();
            BuildCanvas();
            TryAttachToCamera();
            if (canvas != null) canvas.enabled = false;   // 알람 없을 땐 숨김으로 시작
        }

        void LateUpdate()
        {
            if (canvas == null || text == null) return;

            // 카메라 부착 안 됐으면 재시도(XR Rig 초기화가 늦는 경우).
            if (canvas.transform.parent == null || canvas.transform.parent == transform)
                TryAttachToCamera();
            else
            {
                // 부착돼 있으면 역할(호스트/참가자)에 맞는 위치·크기 유지 — 접속 후 역할이 정해지면 자동 반영.
                var want = ActiveOffset();
                if ((canvas.transform.localPosition - want).sqrMagnitude > 1e-8f)
                {
                    canvas.transform.localPosition = want;
                    CraneHud.FaceCameraChild(canvas.transform, want, tiltPitchDeg, 0f);
                }
                float ws = ActiveScale();
                if (!Mathf.Approximately(canvas.transform.localScale.x, ws))
                    canvas.transform.localScale = Vector3.one * ws;
            }

            if (CraneHud.Due(ref nextRefresh, CraneHud.TextHz))
                Refresh();

            // 펄스 — 활성 중엔 ⚠ 아이콘 알파를 사인으로 흔들어 주의를 끈다(매 프레임, 무할당). 배경은 투명.
            if (canvas.enabled && text != null)
            {
                float a = 0.74f + 0.20f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 3.4f));
                var c = curSev; c.a = a; text.color = c;
            }
        }

        // 현재 알람 코드 → 표시 갱신. 0이면 캔버스 끔.
        void Refresh()
        {
            int code = CurrentAlarmCode();
            bool show = code != 0;
            canvas.enabled = show;
            if (!show) { lastText = null; return; }

            var e = AlarmCodebook.Get(code);
            curSev = e != null ? AlarmCodebook.Color(e) : CraneHud.HudColor.Danger;
            // 배너 텍스트 제거 — ⚠ 아이콘만(상세 알람 내용·코드는 상태판이 표시).
            text.color = curSev;
            CraneHud.SetTextIfChanged(text, ref lastText, "<b>⚠</b>");
        }

        // 알람 코드 출처는 공용 단일 출처(CraneNetSync.ActiveAlarmCode)를 따른다 — 부품 말풍선과 동일 값.
        int CurrentAlarmCode()
        {
            var active = StsCraneVRController.Active;   // 크레인이 여러 대면 조종기를 받는 크레인의 알람
            if (active != null) crane = active.GetComponent<StsCrane>();
            if (crane == null) crane = FindAnyObjectByType<StsCrane>();
            return CraneNetSync.ActiveAlarmCode(crane);
        }

        // 순수 관전자(참가자)만 true. 호스트·단독 실행은 false(운전 HUD가 있어 위로 비킴).
        static bool IsParticipant()
        {
            var nm = Unity.Netcode.NetworkManager.Singleton;
            return nm != null && nm.IsClient && !nm.IsServer;
        }

        // 역할별 표시 위치 — 참가자=시야 중앙, 호스트=운전 HUD 위로.
        Vector3 ActiveOffset() => IsParticipant() ? hmdOffset : hostHmdOffset;

        // 역할별 크기 — 참가자=크게, 호스트=운전 HUD(상태판)와 안 겹치게 작게.
        float ActiveScale() => IsParticipant() ? worldScale : hostWorldScale;

        void BuildCanvas()
        {
            // 배너 박스 제거 — 배경 투명, '⚠' 아이콘만 심각도색+펄스로 남긴다(상세 알람 내용은 상태판이 담당).
            canvas = CraneHud.BuildPanel(transform, "CraneAlarmCanvas", panelPixels, worldScale,
                new Color(0f, 0f, 0f, 0f), fontSize, curSev,
                TextAnchor.MiddleCenter, new Vector2(8, 8), out text, fitToText: true);
            text.text = "...";
        }

        // HMD 카메라 부착(head-locked)
        void TryAttachToCamera()
        {
            if (canvas == null) return;
            var cam = targetCamera != null ? targetCamera : Camera.main;
            if (cam == null) cam = FindHMDCamera();
            if (cam == null) return;
            canvas.transform.SetParent(cam.transform, worldPositionStays: false);
            var off = ActiveOffset();
            canvas.transform.localPosition = off;
            canvas.transform.localScale = Vector3.one * ActiveScale();   // 역할별 크기
            CraneHud.FaceCameraChild(canvas.transform, off, tiltPitchDeg, 0f);   // 카메라 향함 — 부착 시 1회
        }

        // Camera.main 실패 시 XR(헤드셋) 카메라 후보 탐색
        static Camera FindHMDCamera()
        {
            foreach (var c in Camera.allCameras)
                if (c != null && c.stereoEnabled) return c;
            foreach (var c in Camera.allCameras)
                if (c != null && c.GetComponent("TrackedPoseDriver") != null) return c;
            return Camera.allCameras.Length > 0 ? Camera.allCameras[0] : null;
        }
    }
}
