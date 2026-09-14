namespace Container.Crane.Sts
{
    /// <summary>
    /// 단일 축을 한계 범위 내에서 이동시키는 추상화.
    /// TrolleyMover(레일축), SpreaderHoist(수직축)가 동일 인터페이스를 구현 →
    /// 상위(PLC·테스트·인스펙터)는 어떤 축인지 모르고 일관되게 호출.
    /// </summary>
    public interface IAxisMover
    {
        float Min { get; }
        float Max { get; }
        float Current { get; }

        /// <summary>축 값 1 이 월드 몇 유닛인가. 실척 m = 축 값 × 이것 ÷ ModelScale.
        /// 축은 부모 로컬에 쓰므로 부모 스케일을 탄다 — FBX RTG 트롤리는 루트 스케일 4.17 아래라
        /// ÷ModelScale 만 하면 4.17배 짧게 잰다(트롤리 20.19m 가 4.85m 로).</summary>
        float WorldPerUnit { get; }

        /// <summary>로컬 좌표(미터). 범위 밖이면 클램프하여 적용.</summary>
        void MoveTo(float value);

        /// <summary>0..1 정규화된 값으로 이동. 슬라이더/PLC normalize 값에 편함.</summary>
        void MoveToNormalized(float t01);
    }
}
