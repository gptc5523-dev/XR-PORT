using UnityEngine;

namespace AIXRCrane.Crane.Sts
{
    /// <summary>단일 축을 한계 범위 내에서 이동시키는 추상화(TrolleyMover·SpreaderHoist 등).</summary>
    public interface IAxisMover
    {
        float Min { get; }
        float Max { get; }
        float Current { get; }

        /// <summary>축 값 1 당 월드 유닛. 실척 m = 축 값 × 이것 ÷ ModelScale.
        /// ★ 부모 스케일 포함 — FBX RTG(루트 스케일 4.17)는 ÷ModelScale 만 하면 4.17배 짧다.</summary>
        float WorldPerUnit { get; }

        /// <summary>축 값 1 당 월드 이동 벡터(길이 = WorldPerUnit). PlcBridge.WorldAtPose 가 쓴다.</summary>
        Vector3 WorldAxis { get; }

        /// <summary>로컬 좌표(미터). 범위 밖이면 클램프하여 적용.</summary>
        void MoveTo(float value);

        /// <summary>0..1 정규화된 값으로 이동. 슬라이더/PLC normalize 값에 편함.</summary>
        void MoveToNormalized(float t01);
    }

    /// <summary>축 값 ↔ 실척 m 환산 — 화면·검증·로그가 같은 식을 쓰게.</summary>
    public static class AxisMeters
    {
        /// <summary>축 1 = 실척 몇 m (부모 스케일 포함).</summary>
        public static float MetersPerUnit(this IAxisMover a) => a.WorldPerUnit / StsConfig.ModelScale;
        /// <summary>하한(Min)부터 현재까지 실척 m — PLC 위치 규약.</summary>
        public static float PlcMeters(this IAxisMover a) => (a.Current - a.Min) * a.MetersPerUnit();
        /// <summary>가동범위 실척 m.</summary>
        public static float RangeMeters(this IAxisMover a) => (a.Max - a.Min) * a.MetersPerUnit();
    }
}
