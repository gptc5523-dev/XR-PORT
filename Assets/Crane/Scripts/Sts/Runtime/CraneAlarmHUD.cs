using UnityEngine;
using UnityEngine.UI;
using AIXRCrane.Crane.Sts.Net;   // CraneNetSync(자식 네임스페이스) 참조

namespace AIXRCrane.Crane.Sts
{
    /// <summary>활성 알람 경보 배너(시야 상단, head-locked) — 안전 신호라 전원에게 보임. 없으면 자동 스폰.
    /// 코드는 CraneNetSync.ActiveAlarmCode(네트워크=호스트 권위), 색은 AlarmCodebook(SSOT).</summary>
    [AddComponentMenu("AI-XR Crane/STS Crane/Crane Alarm HUD")]
    [DisallowMultipleComponent]
    public sealed class CraneAlarmHUD : MonoBehaviour
    {
        [Header("참조")]
        [SerializeField] StsCrane crane;
        [Tooltip("이 카메라에 자식으로 붙음. 비우면 Camera.main 자동 사용.")]
        [SerializeField] Camera targetCamera;

        [Header("HMD 위치 (카메라 로컬 좌표, m) — 역할별")]
        // 참가자(관전): 정면 약간 위 중앙 — 운전 HUD가 없으니 시야 중앙.
        [SerializeField] Vector3 hmdOffset = new Vector3(0f, 0.13f, CraneHud.HudDistance);
        // 호스트(조종): 운전 HUD(상태판)와 안 겹치게 더 위 중앙. 실기기 보고 미세조정.
        [SerializeField] Vector3 hostHmdOffset = new Vector3(0f, 0.24f, CraneHud.HudDistance);
        [SerializeField, Range(-30f, 30f)] float tiltPitchDeg = -4f;

        [Header("배너")]
        [SerializeField] Vector2 panelPixels = new Vector2(640f, 96f);   // fitToText라 실제론 글자에 맞춰 자동
        [SerializeField] float worldScale = 0.0009f;        // 참가자(시야 중앙) 크기 — 운전 HUD 없으니 크게
        [SerializeField] float hostWorldScale = 0.0006f;    // 호스트: 운전 HUD(상태판)와 안 겹치게 더 작게
        [SerializeField] int fontSize = 48;   // ⚠ 아이콘 단독이라 크게

        // ── 외부 공지 ─────────────────────────────────────────────────────
        // 존 진입 공지도 이 배너로(알람이 우선). 호출자가 매 프레임 갱신, 끊기면 유예 뒤 자동 소멸.
        static string notice;
        static Color  noticeColor = CraneHud.HudColor.Danger;
        static float  noticeUntil;

        /// <summary>헤드셋 상단에 공지를 띄운다. 매 프레임 다시 불러 갱신하고, 안 부르면 <paramref name="holdSeconds"/> 뒤 사라진다.</summary>
        public static void Notify(string message, Color color, float holdSeconds = 0.4f)
        {
            notice = message; noticeColor = color; noticeUntil = Time.unscaledTime + holdSeconds;
        }

        /// <summary>공지를 즉시 내린다.</summary>
        public static void ClearNotice() { notice = null; noticeUntil = 0f; }

        static bool NoticeActive => !string.IsNullOrEmpty(notice) && Time.unscaledTime < noticeUntil;

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

            // 펄스 — ⚠ 아이콘 알파를 사인으로 흔듦(매 프레임, 무할당).
            if (canvas.enabled && text != null && !NoticeActive)
            {
                float a = 0.74f + 0.20f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 3.4f));
                var c = curSev; c.a = a; text.color = c;
            }
        }

        // 현재 알람 코드 → 표시 갱신. 0이면 캔버스 끔.
        void Refresh()
        {
            int code = CurrentAlarmCode();
            if (code == 0)                      // 알람 없음 — 공지가 있으면 공지를 띄운다
            {
                bool notify = NoticeActive;
                canvas.enabled = notify;
                if (!notify) { lastText = null; return; }
                curSev = noticeColor;
                text.color = curSev;
                CraneHud.SetTextIfChanged(text, ref lastText, notice);
                return;
            }
            canvas.enabled = true;

            var e = AlarmCodebook.Get(code);
            curSev = e != null ? AlarmCodebook.Color(e) : CraneHud.HudColor.Danger;
            // 배너 텍스트 제거 — ⚠ 아이콘만(상세 알람 내용·코드는 상태판이 표시).
            text.color = curSev;
            CraneHud.SetTextIfChanged(text, ref lastText, "<b>⚠</b>");
        }

        // 알람 코드는 CraneNetSync.ActiveAlarmCode(SSOT) — 부품 말풍선과 동일 값.
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
            // 배경 투명, '⚠' 아이콘만 심각도색+펄스(상세는 상태판 담당).
            canvas = CraneHud.BuildPanel(transform, "CraneAlarmCanvas", panelPixels, worldScale,
                new Color(0f, 0f, 0f, 0f), fontSize, curSev,
                TextAnchor.MiddleCenter, new Vector2(8, 8), out text, fitToText: true);
            text.text = "...";
        }

        // HMD 카메라 부착(head-locked)
        void TryAttachToCamera()
        {
            if (canvas == null) return;
            var cam = CraneHud.HudCamera(targetCamera);
            if (cam == null) return;
            CraneHud.AttachHeadLocked(canvas.transform, cam, ActiveOffset(), tiltPitchDeg, 0f);
            canvas.transform.localScale = Vector3.one * ActiveScale();   // 역할별 크기
        }

    }
}
