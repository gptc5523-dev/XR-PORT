using UnityEngine;

namespace AIXRCrane.Crane.Sts
{
    /// <summary>야드 칸 격자 — 놓을 자리를 PortConfig 에서 유도(QuayPartsPlacer 와 같은 식). 20ft 는 한 베이에 앞뒤 2개.
    /// SpreaderGrabber·CraneDemoRunner 공용.</summary>
    public static class YardGrid
    {
        /// <summary>20ft 앞뒤 배치 틈 — 실척 m. 야드를 까는 QuayPartsPlacer·YardSnapProbe 도 이 값을 읽는다.</summary>
        public const float Gap20ftM = 0.30f;
        /// <summary>20ft 컨테이너 길이 — 실척 m.</summary>
        public const float Len20ftM = ProceduralContainerMesh.Length20ft;

        /// <summary>긴 축이 이 길이(모델 단위)를 넘으면 40ft — SpreaderGrabber.sizeThreshold 와 같은 기준.</summary>
        public const float Is40ThresholdU = 0.38f;

        /// <summary>p(월드 바운즈 중심)를 가장 가까운 야드 칸 중심 x·z 로 맞춘다(y 유지). 블록 밖이면 false.
        /// longSideU = 긴 축 길이(모델 단위, 20/40ft 판정).</summary>
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
