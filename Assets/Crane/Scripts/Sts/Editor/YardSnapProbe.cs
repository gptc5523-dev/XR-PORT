#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Container.Crane.Sts.EditorTools
{
    /// <summary>
    /// 야드 칸(라인) 정렬 실측 — 오너 2026-09-16 "컨테이너 내릴 때 바닥 라인 안 지키고 그냥 내려놓는다. 수식으로 계산해서 수정".
    ///   Unity -batchmode -nographics -projectPath &lt;클론&gt; -executeMethod Container.Crane.Sts.EditorTools.YardSnapProbe.Run -logFile &lt;log&gt;
    ///
    /// 두 가지를 잰다(플레이 모드 없이 씬만 열어서 — 몇 초).
    ///   ① 씬에 실제로 놓인 야드 컨테이너가 <b>PortConfig 수식으로 유도한 칸 격자</b> 위에 있는가.
    ///      → 이게 안 맞으면 스냅을 수식에 맞추는 순간 컨테이너가 눈에 보이는 라인에서 더 벗어난다. 고치기 전에 반드시 확인.
    ///   ② 방향(yaw)이 격자 축(월드 X/Z)에 맞는가 — 칸에 비뚤게 걸친 것 판정.
    ///
    /// 격자 유도는 생산부 QuayPartsPlacer 와 같은 식이지만 <b>좌표를 복사하지 않고</b> PortConfig(런타임 SSOT)에서 다시 유도한다:
    ///   cell.x = YardBlockCenterX(lane) − YardBlockWidthM/2  + RowPitchM·(r + 0.5)
    ///   cell.z = YardBlockCenterZ(block) − YardBlockLengthM/2 + BayPitchM·(b + 0.5)
    ///   20ft 는 한 베이에 앞뒤 2개 → cell.z ± (20ft길이 + Yard20ftGapM)/2 도 후보에 넣는다.
    /// </summary>
    public static class YardSnapProbe
    {
        const string ScenePath = "Assets/Scenes/Port.unity";

        // 판정 허용(실척 m). 행 간 틈이 RowGapM 0.4m 라, 칸 중심에서 0.2m 를 넘으면 틈을 먹기 시작한다 = 라인 침범.
        const float TolM = 0.2f;
        const float YawTolDeg = 2f;

        public static void Run()
        {
            EditorSceneManager.OpenScene(ScenePath);

            var cells = CellsU();
            var boxes = Object.FindObjectsByType<LODGroup>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .Select(l => l.transform)
                .Where(t => Regex.IsMatch(t.name, @"^Cont(20|40)_\d+$"))
                .ToList();

            float inv = StsConfig.InvModelScale;
            int off = 0, yawOff = 0;
            float maxD = 0f, maxYaw = 0f;
            string worst = "없음", worstYaw = "없음";

            foreach (var t in boxes)
            {
                if (!TryBounds(t, out Bounds b)) continue;

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

            // ★ 감도 시험 — 이 계측이 '벗어난 것'을 실제로 잡는지 본다.
            //   초록만 관측한 기준은 공허할 수 있다: 스모크의 야드 칸 기준(PortDemoMenu.YardCellMaxErrM)은 첫 실행 때
            //   이미 라운딩(6d28561)이 들어간 HEAD 였어서 FAIL 을 한 번도 못 봤다. 같은 수식(YardGrid.TrySnapXZ)을 쓰는
            //   여기서 일부러 칸에서 밀어 놓고 '검출되는지'를 확인한다 — 플레이 모드 없이 몇 초, 메모리도 안 먹는다.
            //   (배치 스모크로 end-to-end 를 보려 했으나 시스템 메모리 부족으로 죽었다. 이건 그 대체가 아니라 계측 감도만 본다.)
            float probeM = 0.6f;   // 실척 0.6m — 허용 0.2m 의 3배. 이만큼 밀면 반드시 잡혀야 한다.
            int sensed = 0, tried = 0;
            foreach (var t in boxes)
            {
                if (tried >= 2) break;
                if (!TryBounds(t, out Bounds b0)) continue;
                if (!YardGrid.TrySnapXZ(b0.center, Mathf.Max(b0.size.x, b0.size.z), out _)) continue;
                tried++;
                Vector3 was = t.position;
                t.position = was + new Vector3(probeM * StsConfig.ModelScale, 0f, 0f);
                if (TryBounds(t, out Bounds b1)
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
            float off20 = (6.058f + Yard20ftGapM) * 0.5f * s;   // 20ft 길이 6.058m + 틈, 한 베이에 앞뒤 2개

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

        // 생산부(QuayPartsPlacer:242)의 20ft 틈과 같은 값 — 생산부가 Editor 라 런타임 SSOT 가 없다. 어긋나면 여기와 그쪽을 같이 고칠 것.
        const float Yard20ftGapM = 0.30f;

        static bool TryBounds(Transform t, out Bounds b)
        {
            b = default;
            var rends = t.GetComponentsInChildren<Renderer>();
            if (rends == null || rends.Length == 0) return false;
            b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            return true;
        }
    }
}
#endif
