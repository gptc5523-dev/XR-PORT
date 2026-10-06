using System.Collections.Generic;
using AIXRCrane.Crane.Sts.Plc;
using UnityEngine;

namespace AIXRCrane.Crane.Sts
{
    /// <summary>알람이 난 부품을 심각도 색으로 깜빡인다 — '어디서' 났는지 멀리서도 한눈에(지표 4 O&amp;M 상태표시).
    /// 알람 → 코드북 part → 부품 ID(CranePartId) → 그 부품 렌더러. 출처는 알람 HUD 와 같다:
    /// 호스트·단독 = CraneFault.EvaluateAll + PLC 알람 코드, 관전자 = 호스트가 보낸 코드(CraneNetSync).
    /// 색은 MaterialPropertyBlock 으로 _BaseColor 만 바꾸고, 알람이 풀리면 블록을 지워 원래대로 돌린다(머티리얼 무변경).</summary>
    [DisallowMultipleComponent]
    public sealed class CraneAlarmHighlight : MonoBehaviour
    {
        [Tooltip("원래 색에서 심각도 색으로 섞는 최대 비율(깜빡임 꼭대기).")]
        [SerializeField, Range(0f, 1f)] float maxMix = 0.85f;
        [Tooltip("이 개수보다 렌더러가 많은 조립체(GT = 크레인 전체 2,949개)는 하위 부품(GT.BOGIE.*)을 대신 칠한다.")]
        [SerializeField] int maxRenderers = 200;   // ponytail: 개수 컷 — 조립체별 대표 부품 지정이 필요해지면 표에 칸 추가
        [Tooltip("테두리 상자 최소 한 변(실척 m) — 보기 피벗(1.1m)처럼 작은 부품도 멀리서 보이게.")]
        [SerializeField] float minBoxMeters = 1.4f;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        const float EvalHz = 4f, PulseHz = 15f;

        sealed class Lit { public Renderer r; public Color[] baseColors; public FaultSeverity sev; }

        readonly Dictionary<Renderer, Lit> lit = new Dictionary<Renderer, Lit>();
        readonly Dictionary<Renderer, FaultSeverity> want = new Dictionary<Renderer, FaultSeverity>();
        readonly FaultDef[] faults = new FaultDef[8];
        readonly List<int> codes = new List<int>();
        readonly List<(Renderer[] rends, FaultSeverity sev)> boxes = new List<(Renderer[], FaultSeverity)>();   // 부품마다 상자 하나
        readonly List<LineRenderer> boxPool = new List<LineRenderer>();
        Material boxMat;
        MaterialPropertyBlock mpb;
        float nextEval, nextPulse;

        /// <summary>지금 강조 중인 렌더러 수(검증용).</summary>
        public int LitCount => lit.Count;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoSpawn() => CraneHud.EnsureSpawned<CraneAlarmHighlight>("AlarmLit");

        void Awake() => mpb = new MaterialPropertyBlock();

        // 자체 점검 — 부품이 있는 XR 알람마다 칠할 부품이 찾아지고, 부품 하나가 maxRenderers 를 넘지 않아야 한다.
        void Start()
        {
            var sts = GameObject.Find(StsPartNames.StsCraneRoot);
            if (sts == null || sts.GetComponentInChildren<CranePartId>() == null) return;
            int n = 0; var bad = new List<int>();
            foreach (var e in AlarmCodebook.All)
            {
                if (!e.xr || e.Severity < FaultSeverity.Warning || string.IsNullOrEmpty(e.part)) continue;
                n++;
                var parts = PartRenderers(sts.transform, e.part);
                if (parts.Count == 0 || parts.Exists(rs => rs.Length == 0 || rs.Length > maxRenderers)) bad.Add(e.code);
            }
            QaLog.Check("ALARMLIT", "selfcheck", bad.Count == 0, $"alarms={n} bad={bad.Count} {string.Join(",", bad)}");
        }

        void OnDisable()
        {
            foreach (var l in lit.Values) Clear(l);
            lit.Clear();
            boxes.Clear();
            foreach (var b in boxPool) if (b != null) b.enabled = false;
        }

        void Update()
        {
            if (CraneHud.Due(ref nextEval, EvalHz)) Evaluate();
            if ((lit.Count > 0 || boxes.Count > 0) && CraneHud.Due(ref nextPulse, PulseHz)) Pulse();
        }

        // 크레인마다 활성 알람 → 부품 렌더러 → 원하는 심각도. 바뀐 것만 켜고 끈다.
        void Evaluate()
        {
            want.Clear();
            boxes.Clear();
            foreach (var crane in FindObjectsByType<StsCrane>(FindObjectsSortMode.None))
            {
                ActiveCodes(crane);
                foreach (int code in codes)
                {
                    var e = AlarmCodebook.Get(code);
                    if (e == null || !e.xr || e.Severity < FaultSeverity.Warning || string.IsNullOrEmpty(e.part)) continue;
                    foreach (var rs in PartRenderers(crane.transform, e.part))
                    {
                        boxes.Add((rs, e.Severity));
                        foreach (var r in rs)
                            if (!want.TryGetValue(r, out var s) || e.Severity > s) want[r] = e.Severity;
                    }
                }
            }

            var drop = new List<Renderer>();
            foreach (var kv in lit) if (kv.Key == null || !want.ContainsKey(kv.Key)) drop.Add(kv.Key);
            foreach (var r in drop) { Clear(lit[r]); lit.Remove(r); }
            foreach (var kv in want)
            {
                if (lit.TryGetValue(kv.Key, out var l)) { l.sev = kv.Value; continue; }
                var mats = kv.Key.sharedMaterials;
                var bc = new Color[mats.Length];
                for (int i = 0; i < mats.Length; i++)
                    bc[i] = mats[i] != null && mats[i].HasProperty(BaseColorId) ? mats[i].GetColor(BaseColorId) : Color.white;
                lit[kv.Key] = new Lit { r = kv.Key, baseColors = bc, sev = kv.Value };
            }
            Pulse();
        }

        void ActiveCodes(StsCrane crane)
        {
            codes.Clear();
            if (PortDemoDirector.Spectator)
            {
                // 관전자는 그랩·충돌 판정이 꺼져 있다 — 호스트가 보낸 코드(최고 심각도 1건)만 믿는다.
                int c = Net.CraneNetSync.Syncs(crane) ? Net.CraneNetSync.ActiveAlarmCode(crane) : 0;
                if (c != 0) codes.Add(c);
                return;
            }
            int n = CraneFault.EvaluateAll(crane, faults);
            for (int i = 0; i < n; i++) codes.Add(faults[i].Code);
            foreach (var br in crane.GetComponents<PlcBridge>())
                if (br.isActiveAndEnabled && br.Active)
                {
                    var s = br.Latest;
                    if (s.AlarmActive && s.AlarmCode != 0) codes.Add(s.AlarmCode);
                    break;
                }
        }

        // 부품(또는 그룹) ID 의 부품별 렌더러 — 너무 큰 조립체(크레인 전체)는 하위 부품으로 내려간다.
        List<Renderer[]> PartRenderers(Transform root, string partId)
        {
            var result = new List<Renderer[]>();
            foreach (var p in CranePartId.Find(root, partId))
            {
                var rs = p.GetComponentsInChildren<Renderer>();
                if (rs.Length <= maxRenderers) { result.Add(rs); continue; }
                foreach (var child in CranePartId.Find(root, p.id + "."))
                    if (child != p) result.Add(child.GetComponentsInChildren<Renderer>());
            }
            return result;
        }

        // 심각도가 높을수록 빨리 깜빡인다(Fatal 2Hz · Critical 1.5Hz · Warning 1Hz).
        void Pulse()
        {
            float t = Time.unscaledTime;
            foreach (var l in lit.Values)
            {
                if (l.r == null) continue;
                float hz = l.sev == FaultSeverity.Fatal ? 2f : l.sev == FaultSeverity.Critical ? 1.5f : 1f;
                float k = maxMix * (0.5f + 0.5f * Mathf.Sin(t * hz * 2f * Mathf.PI));
                Color c = CraneFault.SevColor(l.sev);
                for (int i = 0; i < l.baseColors.Length; i++)
                {
                    l.r.GetPropertyBlock(mpb, i);
                    mpb.SetColor(BaseColorId, Color.Lerp(l.baseColors[i], c, k));
                    l.r.SetPropertyBlock(mpb, i);
                }
            }
            DrawBoxes(t);
        }

        // 부품마다 심각도 색 테두리 상자(항상 위에 그림) — 작은 부품·가려진 부품도 어디인지 보이게.
        void DrawBoxes(float t)
        {
            float minU = minBoxMeters * StsConfig.ModelScale;
            for (int i = 0; i < boxes.Count; i++)
            {
                var (rends, sev) = boxes[i];
                if (!SceneUtil.TryBoundsOf(rends, out var b)) continue;
                b.size = Vector3.Max(b.size, Vector3.one * minU);
                if (i == boxPool.Count) boxPool.Add(MakeBox());
                var line = boxPool[i];
                float hz = sev == FaultSeverity.Fatal ? 2f : sev == FaultSeverity.Critical ? 1.5f : 1f;
                Color c = CraneFault.SevColor(sev);
                c.a = 0.35f + 0.65f * (0.5f + 0.5f * Mathf.Sin(t * hz * 2f * Mathf.PI));
                line.startColor = line.endColor = c;
                Vector3 mn = b.min, mx = b.max;
                for (int k = 0; k < BoxPath.Length; k++)
                {
                    int v = BoxPath[k];
                    float x = (v == 1 || v == 2 || v == 5 || v == 6) ? mx.x : mn.x;
                    float z = (v == 2 || v == 3 || v == 6 || v == 7) ? mx.z : mn.z;
                    line.SetPosition(k, new Vector3(x, v < 4 ? mn.y : mx.y, z));
                }
                line.enabled = true;
            }
            for (int i = boxes.Count; i < boxPool.Count; i++) boxPool[i].enabled = false;
        }

        // 상자 12모서리를 한 줄로(몇 모서리는 두 번 지난다) — CranePartPicker 와 같은 경로.
        static readonly int[] BoxPath = { 0, 1, 2, 3, 0, 4, 5, 1, 5, 6, 2, 6, 7, 3, 7, 4 };

        LineRenderer MakeBox()
        {
            if (boxMat == null)
            {
                var sh = Shader.Find(CraneHud.OverlayShader);
                if (sh == null) sh = Shader.Find("Sprites/Default");
                boxMat = new Material(sh);
            }
            var go = new GameObject("AlarmBox");
            go.transform.SetParent(transform, false);
            var l = go.AddComponent<LineRenderer>();
            l.material = boxMat;
            l.useWorldSpace = true;
            l.positionCount = BoxPath.Length;
            l.startWidth = l.endWidth = 0.004f;
            l.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            l.receiveShadows = false;
            return l;
        }

        static MaterialPropertyBlock _empty;
        static void Clear(Lit l)
        {
            if (l.r == null) return;
            _empty ??= new MaterialPropertyBlock();   // 빈 블록 = 덮어쓴 값 없음(원래 머티리얼 색)
            for (int i = 0; i < l.baseColors.Length; i++) l.r.SetPropertyBlock(_empty, i);
        }
    }
}
