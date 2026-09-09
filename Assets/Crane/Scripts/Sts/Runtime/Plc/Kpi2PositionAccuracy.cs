using System.IO;
using System.Text;
using UnityEngine;

namespace Container.Crane.Sts.Plc
{
    /// <summary>
    /// 평가지표 2 — "XR 3D 위치·동작 표시 정확도" 측정 하니스 (1차년도 비중 30%).
    ///
    /// <para><b>무엇을 재는가</b> — "PLC 가 지령한 위치대로 3D 크레인이 서 있는가".
    /// 지령(<see cref="PlcSnapshot"/> 의 실척 m)과 실제 렌더 위치(축 Transform)를 같은 단위로 놓고 뺀다.
    /// 한 시행 = 한 샘플 시점이고, 3축 오차가 <b>모두</b> 허용 안이면 그 시행은 성공이다.
    /// 계획서 판정("100회 반복 중 목표 충족 비율")을 그대로 따른다.</para>
    ///
    /// <para><b>역변환 근거</b> — PlcBridge 는 <c>MoveToNormalized(realPos / rangeM)</c> 로 축을 몬다.
    /// 무버는 <c>Current = Lerp(Min, Max, clamp01(t))</c> 이므로 되돌리면
    ///   <c>렌더 실척 = (Current − Min) / ModelScale</c>
    /// 이다 — <c>rangeM = (Max − Min) / ModelScale</c> 이 약분돼 range 를 몰라도 된다.
    /// 클램프가 걸리지 않는 한 이 역변환은 오차 0 이므로, 남는 오차는 아래 셋뿐이다.</para>
    ///
    /// <para><b>오차가 나오는 곳(=이 하니스가 잡아내는 것)</b>
    /// ① 방향 규약 — PlcBridge 는 "0 이 어느 끝인가"를 벤더 미확인으로 남겨뒀다(질의서 대상).
    ///    반대면 오차가 곧 전체 range 라 즉시 드러난다.
    /// ② 차단 — <c>AxisMoverBase.MoveTo</c> 는 진행 방향에 장애물이 있으면 이동을 <b>거부</b>한다.
    ///    지령은 갔는데 3D 는 안 간 상태 = 진짜 표시 오차다.
    /// ③ 클램프 — 지령이 가동범위 밖이면 잘린다(데이터 range 와 크레인 기하 불일치).
    /// 실 PLC 가 오면 여기에 통신 지터가 얹힌다. 소스만 갈리고 이 코드는 그대로다.</para>
    ///
    /// 붙이는 법: 메뉴 <c>PLC ▸ 지표2 정확도 측정 부착</c>, 또는 STS_Crane 에 직접 추가.
    /// PlcBridge 가 Active 일 때만 잰다(직접조종 중에는 지령이 없어 잴 것이 없다).
    /// </summary>
    // PlcBridge(-100)가 축을 구동한 뒤에 읽어야 같은 틱의 지령↔결과가 짝이 맞는다.
    [DefaultExecutionOrder(100)]
    [AddComponentMenu("Container/STS Crane/KPI 지표2 (위치·동작 정확도)")]
    [RequireComponent(typeof(PlcBridge))]
    [DisallowMultipleComponent]
    public sealed class Kpi2PositionAccuracy : MonoBehaviour
    {
        [Header("판정 기준")]
        [Tooltip("한 축의 허용오차(실척 m). 계획서가 '하네스 설계 시 확정'으로 미뤄둔 값이라 여기가 SSOT다. " +
                 "기본 0.05m — 제어 정확도가 아니라 '표시' 정확도라 STS 스프레더 위치결정 공차(±25~50mm) 기준.")]
        [SerializeField] float toleranceM = 0.05f;

        [Tooltip("반복 시행 수. 계획서 판정 단위(100회).")]
        [SerializeField, Min(1)] int trials = 100;

        [Tooltip("목표 충족률(%). 1차년도 내부목표 90, 2차 KOLAS 95.")]
        [SerializeField, Range(0f, 100f)] float targetPercent = 90f;

        [Header("샘플링")]
        [Tooltip("시행 간격(초). 사이클 전 구간이 고루 잡히게 충분히 벌린다.")]
        [SerializeField, Min(0.02f)] float sampleIntervalS = 0.5f;

        [Tooltip("끄면 콘솔 요약만 내고 CSV 를 안 쓴다.")]
        [SerializeField] bool writeCsv = true;

        StsCrane crane;
        PlcBridge bridge;
        float sampleT;
        int n, passN;
        readonly int[] axisPass = new int[3];
        readonly float[] maxErr = new float[3];
        int blockedN, clampedN;
        StringBuilder csv;
        bool done;

        /// <summary>측정 완료 여부 — 외부(자동 시나리오)가 종료 판정에 쓸 수 있게 공개.</summary>
        public bool Done => done;
        /// <summary>종합 충족률(%) — 3축이 모두 허용 안인 시행의 비율.</summary>
        public float PassPercent => n > 0 ? passN * 100f / n : 0f;

        static readonly string[] AxisName = { "GT", "TR", "HO" };

        void Awake()
        {
            crane = GetComponent<StsCrane>();
            bridge = GetComponent<PlcBridge>();
            csv = new StringBuilder("trial,t_s,axis,commanded_m,rendered_m,error_m,pass,blocked,clamped\n");
        }

        void FixedUpdate()
        {
            if (done || crane == null || bridge == null || !bridge.Active) return;
            if (crane.Gantry == null || crane.Trolley == null || crane.Spreader == null) return;

            sampleT += Time.fixedDeltaTime;
            if (sampleT < sampleIntervalS) return;
            sampleT = 0f;

            var s = bridge.Latest;
            bool trialPass = true;

            for (int i = 0; i < 3; i++)
            {
                var axis = i == 0 ? crane.Gantry : i == 1 ? crane.Trolley : crane.Spreader;
                float commanded = i == 0 ? s.GtPosition : i == 1 ? s.TrPosition : s.HoPosition;

                // 렌더 실척 — PlcBridge 정규화의 정확한 역변환(위 주석의 유도).
                float rendered = (axis.Current - axis.Min) / crane.ModelScale;
                float err = Mathf.Abs(commanded - rendered);

                bool blocked = (axis as AxisMoverBase)?.IsBlocked ?? false;
                float rangeM = (axis.Max - axis.Min) / crane.ModelScale;
                bool clamped = commanded < 0f || commanded > rangeM;

                bool ok = err <= toleranceM;
                if (ok) axisPass[i]++; else trialPass = false;
                if (err > maxErr[i]) maxErr[i] = err;
                if (blocked) blockedN++;
                if (clamped) clampedN++;

                if (writeCsv)
                    csv.Append(n + 1).Append(',').Append(Time.time.ToString("F2")).Append(',')
                       .Append(AxisName[i]).Append(',').Append(commanded.ToString("F4")).Append(',')
                       .Append(rendered.ToString("F4")).Append(',').Append(err.ToString("F4")).Append(',')
                       .Append(ok ? 1 : 0).Append(',').Append(blocked ? 1 : 0).Append(',')
                       .Append(clamped ? 1 : 0).Append('\n');
            }

            n++;
            if (trialPass) passN++;
            if (n >= trials) Finish();
        }

        void Finish()
        {
            done = true;
            bool pass = PassPercent >= targetPercent;

            string path = "";
            if (writeCsv) path = WriteCsv();

            Debug.Log(
                $"[Kpi2] 지표2 위치·동작 정확도 — n={n} 허용±{toleranceM * 1000f:F0}mm 소스={bridge.Source?.Name}\n" +
                $"  축별 충족률  GT {axisPass[0] * 100f / n:F1}%  TR {axisPass[1] * 100f / n:F1}%  HO {axisPass[2] * 100f / n:F1}%\n" +
                $"  종합(3축 동시) {PassPercent:F1}%  목표 {targetPercent:F0}%  → {(pass ? "PASS" : "FAIL")}\n" +
                $"  최대오차  GT {maxErr[0]:F4}  TR {maxErr[1]:F4}  HO {maxErr[2]:F4} m\n" +
                $"  차단(장애물로 이동거부) {blockedN}회 · 클램프(지령이 가동범위 밖) {clampedN}회" +
                (string.IsNullOrEmpty(path) ? "" : $"\n  → {path}"));

            // 오차가 range 의 절반을 넘으면 값 문제가 아니라 규약 문제다 — 따로 짚어준다.
            for (int i = 0; i < 3; i++)
            {
                var axis = i == 0 ? crane.Gantry : i == 1 ? crane.Trolley : crane.Spreader;
                float rangeM = (axis.Max - axis.Min) / crane.ModelScale;
                if (rangeM > 0f && maxErr[i] > rangeM * 0.5f)
                    Debug.LogWarning($"[Kpi2] {AxisName[i]} 최대오차 {maxErr[i]:F1}m 가 가동범위 {rangeM:F1}m 의 절반을 넘습니다 " +
                                     "— 값 오차가 아니라 방향 규약(0이 어느 끝인가) 불일치일 가능성이 큽니다. " +
                                     "PlcBridge.DriveAxis 주석의 벤더 질의 항목.");
            }
        }

        string WriteCsv()
        {
            try
            {
#if UNITY_EDITOR
                string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "KPI");
#else
                string dir = Path.Combine(Application.persistentDataPath, "KPI");
#endif
                Directory.CreateDirectory(dir);
                string p = Path.Combine(dir, $"kpi2_{System.DateTime.Now:yyyyMMdd_HHmmss}.csv");
                File.WriteAllText(p, csv.ToString());
                return p;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Kpi2] CSV 쓰기 실패(측정 결과는 위 로그가 전부): {e.Message}");
                return "";
            }
        }
    }
}
