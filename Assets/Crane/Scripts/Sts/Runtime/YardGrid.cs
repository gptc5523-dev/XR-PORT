using UnityEngine;

namespace AIXRCrane.Crane.Sts
{
    /// <summary>야드 칸(라인) 격자 — 놓을 자리를 PortConfig 에서 수식으로 유도한다(QuayPartsPlacer 가 깐 식과 같다).
    /// 칸 중심 = 블록 중심 − 블록 크기/2 + 피치·(i + 0.5), 20ft 는 한 베이에 앞뒤 2개. SpreaderGrabber·CraneDemoRunner 공용.</summary>
    public static class YardGrid
    {
        /// <summary>20ft 앞뒤 배치 틈 — 실척 m. QuayPartsPlacer.Yard20ftGapM 와 같은 값(Editor 라 참조 불가).</summary>
        public const float Gap20ftM = 0.30f;
        /// <summary>20ft 컨테이너 길이 — 실척 m.</summary>
        public const float Len20ftM = 6.058f;

        /// <summary>긴 축이 이 길이(모델 단위)를 넘으면 40ft — SpreaderGrabber.sizeThreshold 와 같은 기준.
        /// 20ft 0.252u 와 40ft 0.508u 사이.</summary>
        const float Is40ThresholdU = 0.38f;

        /// <summary>놓을 점 p(월드)를 가장 가까운 야드 칸 중심으로 맞춘다. 야드 블록 밖이면 false.</summary>
        /// <param name="p">놓을 컨테이너 바운즈 중심(월드). y 는 쓰지 않는다.</param>
        /// <param name="longSideU">컨테이너 긴 축 길이(모델 단위). 20/40ft 판정에 쓴다.</param>
        /// <param name="snappedXZ">맞춘 칸 중심의 x·z(월드). y 는 p.y 를 그대로 돌려준다.</param>
        public static bool TrySnapXZ(Vector3 p, float longSideU, out Vector3 snappedXZ)
        {
            snappedXZ = p;
            float s = StsConfig.ModelScale;
            float rowPitch = PortConfig.RowPitchM * s, bayPitch = PortConfig.BayPitchM * s;
            float halfW = PortConfig.YardBlockWidthM * 0.5f * s, halfL = PortConfig.YardBlockLengthM * 0.5f * s;

            // 가장 가까운 블록을 고르고, 칸은 그 안에서 수식으로 정한다.
            bool found = false;
            float bestD = float.MaxValue, bx = 0f, bz = 0f;
            for (int i = PortConfig.YardLaneStart; i < PortConfig.YardLanes; i++)
                for (int j = 0; j < PortConfig.YardBlocksPerLane; j++)
                {
                    float cx = PortConfig.YardBlockCenterX(i) * s, cz = PortConfig.YardBlockCenterZ(j) * s;
                    // 블록 footprint 밖이면 제외(여유 반칸).
                    if (Mathf.Abs(p.x - cx) > halfW + rowPitch * 0.5f) continue;
                    if (Mathf.Abs(p.z - cz) > halfL + bayPitch * 0.5f) continue;
                    float d = (p.x - cx) * (p.x - cx) + (p.z - cz) * (p.z - cz);
                    if (d < bestD) { bestD = d; bx = cx; bz = cz; found = true; }
                }
            if (!found) return false;

            // 행·베이 번호 = 첫 칸 중심에서 피치로 나눈 가장 가까운 정수(블록 범위로 클램프).
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

        /// <summary>격자 축에 맞춘 yaw(도) — 지금 각도에서 가장 가까운 90° 배수.</summary>
        public static float SnapYawDeg(float yawDeg) => Mathf.Round(yawDeg / 90f) * 90f;
    }
}
