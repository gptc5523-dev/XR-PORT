using UnityEngine;

namespace Container.Crane.Sts
{
    /// <summary>
    /// 야드 칸(라인) 격자 — 놓을 자리를 <b>수식으로</b> 유도하는 한 곳. 오너 2026-09-16
    /// "컨테이너 내릴 때 바닥 라인 안 지키고 그냥 내려놓는다. 수식으로 계산해서 수정작업해".
    ///
    /// 격자는 생산부(QuayPartsPlacer)가 야드 컨테이너를 깔 때 쓴 식과 <b>같은 식</b>이지만 좌표를 복사하지 않고
    /// PortConfig(런타임 SSOT)에서 다시 유도한다. 생산부는 Editor 라 런타임에서 참조할 수 없다.
    ///   칸 중심 x = YardBlockCenterX(레인) − 블록폭/2  + 행피치·(r + 0.5)
    ///   칸 중심 z = YardBlockCenterZ(블록) − 블록길이/2 + 베이피치·(b + 0.5)
    ///   20ft 는 실물처럼 40ft 베이 한 칸에 앞뒤 2개 → 칸 중심 z ± (20ft길이 + 틈)/2
    /// 행피치 2.838m(= 컨테이너폭 2.438 + 열틈 0.4) · 베이피치 12.792m(= 길이 12.192 + 베이틈 0.6) 모두 PortConfig 유도값.
    ///
    /// ★ 이 식이 실제 씬과 일치하는지 먼저 실측했다(YardSnapProbe, 2026-09-16): 야드 컨테이너 20개 전부
    ///   칸 중심에서 <b>이탈 0.000m · 방향 0.0°</b>. 즉 이 격자가 바닥에 보이는 라인의 출처다. 먼저 재지 않고
    ///   스냅을 붙이면 상자를 오히려 라인 밖으로 옮길 수 있었다.
    ///
    /// 쓰는 쪽: 수동 놓기(SpreaderGrabber.Release)와 자동 시나리오(CraneDemoRunner.FindSlot, xr-port-04 담당)가
    ///   같은 식을 읽는다 — 수식이 두 벌이 되면 두 경로가 서로 다른 자리에 놓는다.
    /// </summary>
    public static class YardGrid
    {
        /// <summary>20ft 앞뒤 배치 틈 — 실척 m. 생산부 QuayPartsPlacer.Yard20ftGapM 와 같은 값(그쪽은 Editor 라 참조 불가).</summary>
        public const float Gap20ftM = 0.30f;
        /// <summary>20ft 컨테이너 길이 — 실척 m. 생산부가 FBX 실측을 이 값과 대조해 검증한다.</summary>
        public const float Len20ftM = 6.058f;

        /// <summary>긴 축이 이 길이(<b>모델 단위</b>)를 넘으면 40ft 로 본다 — SpreaderGrabber.sizeThreshold 와 같은 기준·같은 단위.
        /// 20ft 0.252u / 40ft 0.508u 사이(실척 환산 9.12m = 6.058 과 12.192 사이). 값이 바뀌면 둘을 같이 볼 것.</summary>
        const float Is40ThresholdU = 0.38f;

        /// <summary>
        /// 놓을 점 p(월드)를 가장 가까운 야드 칸 중심으로 맞춘다. 야드 블록 밖이면 false — 에이프런·배·트럭 위에
        /// 놓는 건 격자와 무관하므로 건드리지 않는다.
        /// </summary>
        /// <param name="p">놓을 컨테이너의 바운즈 중심(월드). y 는 쓰지 않는다 — 높이는 통과방지·바닥 하한이 이미 정한다.</param>
        /// <param name="longSideU">컨테이너 긴 축 길이(모델 단위). 20/40ft 판정에 쓴다.</param>
        /// <param name="snappedXZ">맞춘 칸 중심의 x·z(월드). y 는 p.y 를 그대로 돌려준다.</param>
        public static bool TrySnapXZ(Vector3 p, float longSideU, out Vector3 snappedXZ)
        {
            snappedXZ = p;
            float s = StsConfig.ModelScale;
            float rowPitch = PortConfig.RowPitchM * s, bayPitch = PortConfig.BayPitchM * s;
            float halfW = PortConfig.YardBlockWidthM * 0.5f * s, halfL = PortConfig.YardBlockLengthM * 0.5f * s;

            // 어느 블록 위인가 — 레인·블록은 최대 4개뿐이라 가장 가까운 하나를 고른다(격자는 그 안에서 수식으로).
            bool found = false;
            float bestD = float.MaxValue, bx = 0f, bz = 0f;
            for (int i = PortConfig.YardLaneStart; i < PortConfig.YardLanes; i++)
                for (int j = 0; j < PortConfig.YardBlocksPerLane; j++)
                {
                    float cx = PortConfig.YardBlockCenterX(i) * s, cz = PortConfig.YardBlockCenterZ(j) * s;
                    // 블록 footprint 밖이면 제외. 여유는 반칸 — 칸 경계에 걸친 것도 그 블록으로 본다.
                    if (Mathf.Abs(p.x - cx) > halfW + rowPitch * 0.5f) continue;
                    if (Mathf.Abs(p.z - cz) > halfL + bayPitch * 0.5f) continue;
                    float d = (p.x - cx) * (p.x - cx) + (p.z - cz) * (p.z - cz);
                    if (d < bestD) { bestD = d; bx = cx; bz = cz; found = true; }
                }
            if (!found) return false;

            // 블록 안에서 행·베이 번호를 수식으로 — 첫 칸 중심에서 피치로 나눠 가장 가까운 정수, 블록 범위로 클램프.
            float x0 = bx - halfW + rowPitch * 0.5f, z0 = bz - halfL + bayPitch * 0.5f;
            int r = Mathf.Clamp(Mathf.RoundToInt((p.x - x0) / rowPitch), 0, PortConfig.YardRows - 1);
            int b = Mathf.Clamp(Mathf.RoundToInt((p.z - z0) / bayPitch), 0, PortConfig.YardBays - 1);
            float x = x0 + rowPitch * r, z = z0 + bayPitch * b;

            // 20ft 는 한 베이의 앞·뒤 두 자리 중 가까운 쪽. 40ft 는 베이 중앙.
            if (longSideU <= Is40ThresholdU)
            {
                float off = (Len20ftM + Gap20ftM) * 0.5f * s;
                z += (p.z < z) ? -off : off;
            }

            snappedXZ = new Vector3(x, p.y, z);
            return true;
        }

        /// <summary>격자 축(월드 X/Z)에 맞춘 yaw(도) — 지금 각도에서 가장 가까운 90° 배수. 칸에 비뚤게 걸치는 것을 막는다.</summary>
        public static float SnapYawDeg(float yawDeg) => Mathf.Round(yawDeg / 90f) * 90f;
    }
}
