#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AIXRCrane.Crane.Sts.EditorTools
{
    /// <summary>야드 칸 정렬 실측(배치 전용, 플레이 모드 없이 몇 초) — 씬 야드 컨테이너가 PortConfig 격자 위에 있는지·yaw 가 격자 축인지.
    ///   -executeMethod AIXRCrane.Crane.Sts.EditorTools.YardSnapProbe.Run</summary>
    public static class YardSnapProbe
    {
        const string ScenePath = StsPartNames.PortScenePath;

        // 판정 허용(실척 m) — 행 틈 0.4m 의 절반. 넘으면 라인 침범.
        const float TolM = 0.2f;
        const float YawTolDeg = 2f;

        public static void Run()
        {
            EditorSceneManager.OpenScene(ScenePath);

            var cells = CellsU();
            var boxes = Object.FindObjectsByType<LODGroup>(FindObjectsInactive.Exclude)
                .Select(l => l.transform)
                .Where(t => StsPartNames.IsYardContainerName(t.name))
                .ToList();

            float inv = StsConfig.InvModelScale;
            int off = 0, yawOff = 0;
            float maxD = 0f, maxYaw = 0f;
            string worst = "없음", worstYaw = "없음";

            foreach (var t in boxes)
            {
                if (!SceneUtil.TryBounds(t, out Bounds b)) continue;

                float best = float.MaxValue;
                foreach (var c in cells)
                {
                    float dx = b.center.x - c.x, dz = b.center.z - c.z;
                    float d = Mathf.Sqrt(dx * dx + dz * dz);
                    if (d < best) best = d;
                }
                float bestM = best * inv;
                if (bestM > maxD) { maxD = bestM; worst = t.name; }
                if (bestM > TolM) off++;

                // 격자 축은 월드 X/Z — yaw 를 90° 격자에 접었을 때의 잔차
                float yaw = Mathf.Abs(Mathf.DeltaAngle(t.eulerAngles.y, Mathf.Round(t.eulerAngles.y / 90f) * 90f));
                if (yaw > maxYaw) { maxYaw = yaw; worstYaw = t.name; }
                if (yaw > YawTolDeg) yawOff++;
            }

            bool pass = boxes.Count > 0 && off == 0 && yawOff == 0;
            Debug.Log($"[YardSnapProbe] 칸 {cells.Count}개(레인 {PortConfig.YardLaneStart}~{PortConfig.YardLanes - 1} · " +
                      $"블록 {PortConfig.YardBlocksPerLane} · 행 {PortConfig.YardRows} · 베이 {PortConfig.YardBays}) · " +
                      $"행피치 {PortConfig.RowPitchM:F3}m · 베이피치 {PortConfig.BayPitchM:F3}m");
            Debug.Log($"[YardSnapProbe] {(pass ? "PASS" : "FAIL")} — 컨테이너 {boxes.Count}개, " +
                      $"칸 벗어남 {off}개(허용 {TolM:F2}m), 최대 이탈 {maxD:F3}m ← {worst}, " +
                      $"방향 틀어짐 {yawOff}개(허용 {YawTolDeg:F0}°), 최대 {maxYaw:F1}° ← {worstYaw}");

            // 감도 시험 — 일부러 칸에서 밀어 놓고 YardGrid.TrySnapXZ 가 검출하는지 본다(초록만 보면 기준이 공허할 수 있다).
            float probeM = 0.6f;   // 실척 0.6m — 허용의 3배, 반드시 잡혀야 한다.
            int sensed = 0, tried = 0;
            foreach (var t in boxes)
            {
                if (tried >= 2) break;
                if (!SceneUtil.TryBounds(t, out Bounds b0)) continue;
                if (!YardGrid.TrySnapXZ(b0.center, Mathf.Max(b0.size.x, b0.size.z), out _)) continue;
                tried++;
                Vector3 was = t.position;
                t.position = was + new Vector3(probeM * StsConfig.ModelScale, 0f, 0f);
                if (SceneUtil.TryBounds(t, out Bounds b1)
                    && YardGrid.TrySnapXZ(b1.center, Mathf.Max(b1.size.x, b1.size.z), out Vector3 cell1))
                {
                    float d = new Vector2(b1.center.x - cell1.x, b1.center.z - cell1.z).magnitude * inv;
                    if (d > TolM) sensed++;
                    Debug.Log($"[YardSnapProbe] 감도 {(d > TolM ? "검출" : "★놓침")} {t.name} — {probeM:F2}m 밀었을 때 이탈 {d:F3}m(허용 {TolM:F2})");
                }
                t.position = was;   // 원복 — 씬은 저장하지 않지만 상태를 남기지 않는다
            }
            bool sensOk = tried > 0 && sensed == tried;
            Debug.Log($"[YardSnapProbe] 감도 시험 {(sensOk ? "PASS" : "FAIL")} — {tried}건 중 {sensed}건 검출. " +
                      $"{(sensOk ? "이 계측은 벗어난 배치를 잡는다(같은 수식을 쓰는 스모크 기준도 유효)" : "★계측이 못 잡는다 — 이 기준을 신뢰하지 말 것")}");

            EditorApplication.Exit(pass && sensOk ? 0 : 1);
        }

        /// <summary>PortConfig 에서 유도한 칸 중심 XZ(모델 단위). 20ft 앞뒤 자리도 후보로 포함.</summary>
        static List<(float x, float z)> CellsU()
        {
            float s = StsConfig.ModelScale;
            float rowPitch = PortConfig.RowPitchM * s, bayPitch = PortConfig.BayPitchM * s;
            float halfW = PortConfig.YardBlockWidthM * 0.5f * s, halfL = PortConfig.YardBlockLengthM * 0.5f * s;
            float off20 = (YardGrid.Len20ftM + YardGrid.Gap20ftM) * 0.5f * s;   // 한 베이에 앞뒤 2개

            var cells = new List<(float x, float z)>();
            for (int i = PortConfig.YardLaneStart; i < PortConfig.YardLanes; i++)
                for (int j = 0; j < PortConfig.YardBlocksPerLane; j++)
                {
                    float bx = PortConfig.YardBlockCenterX(i) * s, bz = PortConfig.YardBlockCenterZ(j) * s;
                    for (int r = 0; r < PortConfig.YardRows; r++)
                        for (int b = 0; b < PortConfig.YardBays; b++)
                        {
                            float x = bx - halfW + rowPitch * (r + 0.5f);
                            float z = bz - halfL + bayPitch * (b + 0.5f);
                            cells.Add((x, z));
                            cells.Add((x, z - off20));
                            cells.Add((x, z + off20));
                        }
                }
            return cells;
        }


    }
}
#endif
