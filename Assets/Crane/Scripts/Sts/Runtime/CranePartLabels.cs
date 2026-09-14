using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace Container.Crane.Sts
{
    /// <summary>
    /// 크레인 부품을 가리키는 3D 월드 말풍선(빌보드 + 지시선 callout).
    ///   - 말풍선은 부품 바로 위가 아니라 살짝 비켜 떠 있고, '지시선(leader line)'이 해당 부품을 가리킴
    ///     → 어느 부품 얘긴지 직관적이면서 라벨끼리 안 포개짐
    ///   - 난잡 방지(요청): 가동부(스프레더/트롤리/갠트리)만 상시 표시(거리 컬링), 고정 부품 4종은
    ///     '쳐다볼 때만' 표시 → 평소 화면이 깔끔
    ///   - 스프레더: 적재/하중(t)/잠금 · 트롤리·갠트리: 위치%/속도(m/min) · 고정: 이름+역할
    /// 씬에 안 붙여도 [RuntimeInitializeOnLoadMethod]로 자동 스폰. 이미 있으면 스킵.
    /// </summary>
    [AddComponentMenu("Container/STS Crane/Crane Part Labels")]
    [DisallowMultipleComponent]
    public sealed class CranePartLabels : MonoBehaviour
    {
        [Header("참조")]
        [SerializeField] StsCrane crane;
        [SerializeField] Camera targetCamera;

        [Header("배치/크기")]
        [Tooltip("부품 기준 말풍선을 띄울 높이(월드 m, 위로). 중간시점 가독 위해 낮춤 — 부품에 더 붙임.")]
        [SerializeField] float labelHeight = 0.08f;
        [Tooltip("트롤리/스프레더 말풍선을 좌우(Z)로 벌려 겹침 방지하는 양(월드 m).")]
        [SerializeField] float sideStagger = 0.16f;
        [Tooltip("패널 크기 배율(1px=이 값 m). 모델이 1/24라 작게.")]
        [SerializeField] float worldScale = 0.0009f;   // 가독성 ↑
        [SerializeField] Vector2 panelPixels = new Vector2(360f, 150f);
        [SerializeField] Color bgColor = new Color(0f, 0f, 0f, CraneHud.PanelBgAlpha);   // 패널 배경 알파 표준(공용 토큰)
        [SerializeField] int fontSize = 26;   // 가독성 ↑

        [Header("표시 규칙(난잡 방지)")]
        [Tooltip("이 거리(m)보다 멀면 라벨 숨김(가독성). 0이면 항상 표시.")]
        [SerializeField] float hideBeyond = 8f;
        [Tooltip("고정 부품(기계실/운전실/붐/평형추) 라벨은 시선이 이 각도(°) 안에 들 때만 표시 → 쳐다보는 것만 뜸.")]
        [SerializeField] float staticLookAngle = 14f;
        [Tooltip("이 거리(m)까지는 기본 크기, 그보다 멀면 거리에 비례해 키워 멀리서도 읽히게(각크기 유지).")]
        [SerializeField] float labelRefDist = 2.5f;
        [Tooltip("거리비례 확대 상한(기본 크기의 N배). 너무 크면 가까운 듯 보이므로 클램프.")]
        [SerializeField] float labelMaxScale = 3.5f;

        [Header("지시선(말풍선 꼬리)")]
        [SerializeField] bool showLeader = true;
        [SerializeField] float leaderWidth = 0.003f;
        [SerializeField] Color leaderColor = new Color(1f, 1f, 1f, 0.5f);

        enum Kind { Spreader, Axis, Static }   // Spreader=적재/하중/잠금, Axis=위치%/속도(트롤리·갠트리), Static=이름+역할

        sealed class Label
        {
            public Kind kind;
            public Canvas canvas;
            public Text text;
            public Transform anchor;     // 가리킬/따라갈 부품
            public IAxisMover mover;     // Axis/Spreader용(없으면 null)
            public string title, role;   // 머리말 / 정적 설명
            public Vector3 offset;       // anchor 기준 말풍선 위치 오프셋(겹침 분산)
            public LineRenderer line;    // anchor→말풍선 지시선(없으면 null)
            public float prevPos, speedMpm;
            public bool primed;
            public string lastText;   // 직전 표시 문자열 — 바뀔 때만 Text.text 대입(캔버스 리빌드 절감)
        }

        readonly List<Label> labels = new List<Label>();
        readonly StringBuilder sb = new StringBuilder(160);
        float nextTextRefresh;   // 라벨 텍스트 생성/대입 스로틀(CraneHud.TextHz). 위치/빌보드는 매 프레임 갱신.
        SpreaderAttach attach;
        SpreaderLockAnimator lockAnim;
        SpreaderGrabber grabber;
        Material leaderMat;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoSpawn() => CraneHud.EnsureSpawned<CranePartLabels>("PartLabels");

        bool built;   // 라벨 1회 생성 완료 여부(크레인이 절차 생성이라 늦게 나타날 수 있음 → 폴링 재시도)

        void Start() => EnsureBuilt();

        // 크레인이 아직 없으면(절차 생성 전) 비활성화하지 말고 매 프레임 재시도 → 늦게 생성돼도 라벨이 뜬다.
        // (예전: crane==null이면 enabled=false로 영구 비활성 → 라벨이 영원히 안 보이던 버그)
        void EnsureBuilt()
        {
            if (built) return;
            if (crane == null) crane = FindAnyObjectByType<StsCrane>();
            if (crane == null) return;

            attach = crane.Attach;
            grabber = crane.GetComponent<SpreaderGrabber>();
            var spreaderT = (crane.Spreader as Component)?.transform;
            if (spreaderT != null) lockAnim = spreaderT.GetComponent<SpreaderLockAnimator>();
            if (lockAnim == null) lockAnim = FindAnyObjectByType<SpreaderLockAnimator>();

            Vector3 up = Vector3.up * labelHeight;
            // 가동부(실시간) — 트롤리/스프레더는 좌우(Z)로 벌려 겹침 방지(스프레더 올리면 둘이 포개지던 문제 해소)
            BuildLabel(Kind.Axis, (crane.Trolley as Component)?.transform, crane.Trolley, "트롤리", null,
                       up + Vector3.forward * sideStagger);
            BuildLabel(Kind.Spreader, (crane.Spreader as Component)?.transform, crane.Spreader, "스프레더", null,
                       up - Vector3.forward * sideStagger);
            // 갠트리 주행 — 위치%/속도(m/min). 다리 포스트에 앵커.
            //   다리에 박히지 않게 앞(+X=붐 아웃리치/크레인 앞쪽)으로 빼고 조금 더 올림. 지시선이 다리를 가리킴.
            BuildLabel(Kind.Axis, FindPart(StsPartNames.LegPost), crane.Gantry, "갠트리 주행", null,
                       up * 1.3f + Vector3.right * 0.22f);
            // 고정 부품(이름 + 역할) — 쳐다볼 때만 표시
            // 일단 주석처리(사용자 요청) — 복구하려면 주석 해제
            // BuildLabel(Kind.Static, FindPart(StsPartNames.MachineryHouse), null, "기계실", "권상기계·전장실", up);
            // BuildLabel(Kind.Static, FindPart(StsPartNames.OperatorCab), null, "운전실", "운전사 탑승", up);
            // BuildLabel(Kind.Static, FindPart(StsPartNames.BoomGirder), null, "붐 거더", "트롤리 레일", up);
            // BuildLabel(Kind.Static, FindPart(StsPartNames.Counterweight), null, "평형추", "붐 균형추", up);

            built = true;
        }

        // 크레인 하위에서 이름으로 부품 찾기(첫 매치). 정적 라벨 앵커용.
        Transform FindPart(string partName)
        {
            foreach (var t in crane.GetComponentsInChildren<Transform>(true))
                if (CraneHud.BaseName(t.name) == partName) return t;
            return null;
        }

        void BuildLabel(Kind kind, Transform anchor, IAxisMover mover, string title, string role, Vector3 offset)
        {
            if (anchor == null) return;

            // this(스케일1)의 자식으로 말풍선 생성 → 부모 회전/스케일 영향 없음
            // fitToText: 검정 배경이 글자 분량에 맞춰 자동 축소(빈 여백 제거)
            var canvas = CraneHud.BuildPanel(transform, $"Label_{title}", panelPixels, worldScale,
                bgColor, fontSize, Color.white, TextAnchor.MiddleCenter, new Vector2(12, 8), out var text,
                fitToText: true);

            LineRenderer line = null;
            if (showLeader)
            {
                if (leaderMat == null) leaderMat = MakeLeaderMaterial();
                if (leaderMat != null)
                {
                    var lgo = new GameObject($"Leader_{title}");
                    lgo.transform.SetParent(transform, worldPositionStays: false);
                    line = lgo.AddComponent<LineRenderer>();
                    line.material = leaderMat;
                    line.useWorldSpace = true;
                    line.positionCount = 2;
                    line.numCapVertices = 2;
                    line.startColor = line.endColor = leaderColor;
                    line.startWidth = line.endWidth = leaderWidth;
                    line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    line.receiveShadows = false;
                }
            }

            var label = new Label { kind = kind, canvas = canvas, text = text, anchor = anchor, mover = mover,
                                    title = title, role = role, offset = offset, line = line };
            labels.Add(label);
            // 고정 부품(이름+역할)은 내용이 안 변함 → 텍스트 1회만 설정하고 매 프레임 재생성 스킵
            if (kind == Kind.Static) label.text.text = BuildText(label);
        }

        // 지시선용 머티리얼 — 라벨과 동일한 "항상 위" 오버레이 셰이더 사용(구조물에 안 가림).
        //   전용 셰이더가 없으면(폴백) 깊이테스트가 남는 일반 셰이더로 — 지시선만 가릴 수 있으나 표시는 됨.
        static Material MakeLeaderMaterial()
        {
            Shader sh = Shader.Find("Container/CraneHudOverlay");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            if (sh == null) sh = Shader.Find("Unlit/Color");
            if (sh == null) sh = Shader.Find("UI/Default");
            return sh != null ? new Material(sh) : null;
        }

        void LateUpdate()
        {
            if (!built) { EnsureBuilt(); if (!built) return; }   // 크레인 늦게 생성돼도 라벨 생성 재시도

            var cam = targetCamera != null ? targetCamera : Camera.main;
            if (cam == null) return;
            bool refreshText = CraneHud.Due(ref nextTextRefresh, CraneHud.TextHz);   // 텍스트 생성은 이 빈도로만
            Vector3 camPos = cam.transform.position;
            Vector3 camFwd = cam.transform.forward;
            float dt = Time.deltaTime;
            float halfH = panelPixels.y * worldScale * 0.5f;   // 말풍선 아래 가장자리(지시선 끝점)

            foreach (var L in labels)
            {
                if (L.canvas == null || L.anchor == null) continue;

                UpdateSpeed(L, dt);

                Vector3 anchorPos = L.anchor.position;
                Vector3 bubblePos = anchorPos + L.offset;
                Vector3 toBubble = bubblePos - camPos;
                float dist = toBubble.magnitude;

                // 표시 규칙(난잡 방지)
                bool show;
                if (L.kind == Kind.Static)
                    // 고정 부품: 거리 안 + '쳐다볼 때'만 → 평소엔 안 보임
                    show = (hideBeyond <= 0f || dist <= hideBeyond) &&
                           Vector3.Angle(camFwd, toBubble) <= staticLookAngle;
                else
                    show = hideBeyond <= 0f || dist <= hideBeyond;

                L.canvas.enabled = show;
                if (L.line != null) L.line.enabled = show;
                if (!show) continue;

                // 거리비례 스케일 — 멀수록 키워 각크기(읽히는 정도)를 유지. refDist 이내는 기본, 이후 비례 확대(상한 클램프).
                float distScale = Mathf.Clamp(dist / Mathf.Max(labelRefDist, 0.01f), 1f, labelMaxScale);

                // 위치 + 빌보드(카메라 정면) + 거리비례 크기
                L.canvas.transform.position = bubblePos;
                L.canvas.transform.localScale = Vector3.one * (worldScale * distScale);
                if (toBubble.sqrMagnitude > 1e-6f)
                    L.canvas.transform.rotation = Quaternion.LookRotation(toBubble.normalized, Vector3.up);

                // 지시선 — 부품(anchor)에서 말풍선 아래 가장자리로 (callout 꼬리). 끝점도 확대된 말풍선 높이에 맞춤.
                if (L.line != null)
                {
                    L.line.SetPosition(0, anchorPos);
                    L.line.SetPosition(1, bubblePos - Vector3.up * (halfH * distScale));
                }

                // 가동 라벨만, 스로틀 주기에 한해, 내용이 바뀔 때만 대입. 고정 라벨은 1회 설정 끝.
                if (L.kind != Kind.Static && refreshText)
                    CraneHud.SetTextIfChanged(L.text, ref L.lastText, BuildText(L));
            }
        }

        void UpdateSpeed(Label L, float dt)
        {
            if (L.mover == null || dt < 1e-5f) return;
            float cur = L.mover.Current;
            if (!L.primed) { L.prevPos = cur; L.primed = true; return; }
            float vModel = Mathf.Abs(cur - L.prevPos) / dt;     // 모델 units/s
            float vRealMpm = vModel * L.mover.WorldPerUnit / crane.ModelScale * 60f;   // 실척 m/min (×월드/축)
            L.speedMpm = Mathf.Lerp(L.speedMpm, vRealMpm, 1f - Mathf.Exp(-dt / 0.15f));
            L.prevPos = cur;
        }

        string BuildText(Label L)
        {
            sb.Clear();
            sb.Append("<b>"); sb.Append(L.title); sb.Append("</b>");
            switch (L.kind)
            {
                case Kind.Spreader:
                    bool has = attach != null && attach.HasContainer;
                    sb.AppendLine();
                    if (has)
                    {
                        string id = attach.AttachedDisplayId;   // ISO6346 식별번호(잡는 순간 결정적 확정)
                        if (!string.IsNullOrEmpty(id))
                            sb.AppendLine($"<color=#7FFF7F>{id}</color>");
                        float t = attach.AttachedMassKg / 1000f;
                        if (t > 0.05f)
                        {
                            var g = ContainerProject.ContainerLoad.Grade(t);
                            string hex = ColorUtility.ToHtmlStringRGB(ContainerProject.ContainerLoad.GradeColor(g));
                            sb.AppendLine($"하중 <color=#{hex}>{t:0.#} t — {ContainerProject.ContainerLoad.GradeLabel(g)}</color>");
                        }
                        else sb.AppendLine("<color=#7FFF7F>적재 중</color>");
                        // 잠금 상태는 애니메이터(지령값)로만 판정. 없으면 '불명(--)' — 컨테이너 유무로 추정 금지(미체결을 OK로 오표시→안전 위험).
                        if (lockAnim != null)
                            sb.Append(lockAnim.Locked ? "잠금 <color=#7FFF7F>OK</color>" : "잠금 <color=#FF6666>해제</color>");
                        else
                            sb.Append("잠금 <color=#999999>--</color>");
                    }
                    else sb.Append("<color=#999999>공차(빈 스프레더)</color>");
                    AppendFault(CraneFault.EvaluateSpreader(crane));   // 과부하/호이스트 끝단
                    if (grabber != null && grabber.IsLanded)           // 안착(정상 안내)
                    {
                        sb.AppendLine();
                        sb.Append("<color=#7FFF7F>✓ 안착</color>");
                        // 빈 스프레더가 컨테이너 위에 얹혔는데 아직 안 잡음 → '체결 대기' 액션 큐.
                        //   들고 있을 때(has)는 잠글 게 아니라 놓는 상황이라 제외. 호박색=액션 필요('해제' 빨강과 구분).
                        bool lockedNow = lockAnim != null ? lockAnim.Locked : has;
                        if (!has && !lockedNow)
                        {
                            sb.AppendLine();
                            sb.Append("<color=#FFD25F>⊘ 체결 대기</color>");
                        }
                    }
                    break;

                case Kind.Axis:   // 트롤리·갠트리 — 위치% · 속도
                    sb.AppendLine();
                    sb.Append(Pct(L.mover));
                    sb.Append("  ·  ");
                    sb.Append($"<color=#5FE0FF>{Mathf.RoundToInt(L.speedMpm)} m/min</color>");
                    AppendFault(L.mover == crane.Trolley ? CraneFault.EvaluateTrolley(crane)
                                                         : CraneFault.EvaluateGantry(crane));
                    break;

                case Kind.Static:   // 고정 부품 — 역할 설명
                    if (!string.IsNullOrEmpty(L.role))
                    {
                        sb.AppendLine();
                        sb.Append("<color=#CCCCCC>"); sb.Append(L.role); sb.Append("</color>");
                    }
                    break;
            }
            return sb.ToString();
        }

        // 부품 말풍선에 활성 알람 한 줄 추가(코드북 Sev색). 없으면 아무것도 안 함.
        void AppendFault(FaultDef f)
        {
            if (!f.IsValid) return;
            sb.AppendLine();
            string hex = ColorUtility.ToHtmlStringRGB(CraneFault.SevColor(f.Sev));
            sb.Append($"<color=#{hex}>⚠ {CraneFault.Format(f)}</color>");
        }

        static string Pct(IAxisMover m)
        {
            if (m == null) return "--%";
            float r = m.Max - m.Min;
            float t = r > 1e-6f ? (m.Current - m.Min) / r : 0f;
            return $"{Mathf.Clamp(Mathf.RoundToInt(t * 100f), 0, 100)}%";
        }
    }
}
