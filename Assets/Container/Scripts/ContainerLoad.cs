using UnityEngine;

namespace ContainerProject
{
    /// <summary>컨테이너 하중 상태 등급(정상/주의/이상).</summary>
    public enum LoadGrade { Normal, Caution, Over }

    /// <summary>
    /// 컨테이너 ID로부터 하중(톤)과 상태 등급을 '결정적으로' 산출한다.
    /// 같은 ID = 항상 같은 무게(재현 가능). 야드의 컨테이너마다 무게가 달라
    /// 정상/주의/이상이 골고루 나오도록 세 구간에 분포시킨다.
    ///
    /// 하중 정의: 스프레더가 잡은 '컨테이너 총중량(Gross)'. 스프레더 자중은 영점(tare) 처리.
    /// PLC 실하중 센서가 연동되면, 이 산출값 대신 측정값을 쓰고 Grade()만 재사용하면 된다.
    /// </summary>
    public static class ContainerLoad
    {
        // 상태 등급 경계 (총중량 Gross, 톤). ISO 6346 컨테이너 max gross ≈ 30.5t.
        // 이 경계는 '컨테이너 자체 과적(ISO)' 라벨용 — 크레인 과부하(SWL)와 별개(H1 분리).
        public const float NormalMax  = 24f;    // 이하 = 정상
        public const float CautionMax = 30.5f;  // 이하 = 주의 / 초과 = 이상(컨테이너 과적)

        // 크레인 과부하(SWL) — 컨테이너 ISO 한계와 무관한 '크레인 정격 인양하중' 기준(H1)
        // 실제 SWL은 ㈜엠비이 벤더 확인 대상(질의서 H1). 통상 STS 단동 SWL ≈ 50~65t(트윈리프트는 컨테이너당 절반).
        //   단일 ISO 컨테이너(max 30.5t)는 정상 운전에서 SWL을 넘지 않는다 → '상시 과부하 트립'(가상 32t) 오경보 제거.
        public const float RatedLoadTon    = 65f;     // 크레인 정격 인양하중(SWL), 톤 — 벤더 확인 후 확정.
        public const float OverloadWarnFrac = 1.05f;  // 정격 105% — 경고
        public const float OverloadTripFrac = 1.10f;  // 정격 110% — 과부하 트립(코드북 3012)

        // 시뮬용 무게 분포 범위. 세 등급이 골고루 나오도록 구간별로 매핑.
        const float MinTons = 6f;     // 가벼운 적재 하한
        const float MaxTons = 34f;    // 과적 상한

        // 등급별 해시 구간 비율 (정상 40% / 주의 35% / 이상 25%)
        const float NormalBand  = 0.40f;
        const float CautionBand = 0.75f;

        /// <summary>ID로부터 결정적 하중(톤, 0.1 단위).</summary>
        public static float WeightTons(string containerId)
        {
            float t = StableHash.Hash01(containerId);
            float gross;
            if (t < NormalBand)
                gross = Mathf.Lerp(MinTons, NormalMax, t / NormalBand);
            else if (t < CautionBand)
                gross = Mathf.Lerp(NormalMax, CautionMax, (t - NormalBand) / (CautionBand - NormalBand));
            else
                gross = Mathf.Lerp(CautionMax, MaxTons, (t - CautionBand) / (1f - CautionBand));
            return Mathf.Round(gross * 10f) / 10f;
        }

        public static LoadGrade Grade(float tons)
        {
            if (tons <= NormalMax)  return LoadGrade.Normal;
            if (tons <= CautionMax) return LoadGrade.Caution;
            return LoadGrade.Over;
        }

        /// <summary>크레인 과부하 등급(SWL 기준, %임계) — 컨테이너 ISO 과적(Grade)과 별개. 정상/경고/과부하(Over).</summary>
        public static LoadGrade CraneLoadGrade(float tons)
        {
            if (tons >= RatedLoadTon * OverloadTripFrac) return LoadGrade.Over;     // ≥110% → 과부하 트립
            if (tons >= RatedLoadTon * OverloadWarnFrac) return LoadGrade.Caution;  // ≥105% → 경고
            return LoadGrade.Normal;
        }

        public static string GradeLabel(LoadGrade g) => g switch
        {
            LoadGrade.Normal  => "정상",
            LoadGrade.Caution => "주의",
            _                 => "이상",
        };

        public static Color GradeColor(LoadGrade g) => g switch
        {
            LoadGrade.Normal  => new Color(0.30f, 0.85f, 0.40f),  // 녹색
            LoadGrade.Caution => new Color(0.95f, 0.80f, 0.20f),  // 노랑
            _                 => new Color(0.92f, 0.27f, 0.24f),  // 빨강
        };
    }
}
