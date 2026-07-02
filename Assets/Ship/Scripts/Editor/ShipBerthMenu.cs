using UnityEngine;
using UnityEditor;
using Container.Crane.Sts;   // StsConfig, StsPartNames, TrolleyMover

namespace Container.Ship.EditorTools
{
    /// <summary>
    /// 컨테이너선(ContainerShip)을 기존 STS 크레인·부두 안벽에 접안 정렬한다(오너 지시 2026-06-19, 방법 A).
    ///
    /// ── 배치 원칙([[feedback_match_real_world_reference]], [[feedback_calculate_before_placing]]) ──
    ///   실물 STS: 크레인은 안벽(육지)에 서고, 배는 그 바다측(+X)에 현측을 안벽에 붙여 접안한다.
    ///   크레인 +X=바다(아웃리치 45m), 배 길이=+Z·선폭=±X·흘수선 y=0 — 두 좌표축이 같은 방향이라 회전 없이 평행이동만.
    ///   크레인·부두·바다는 이미 한 세트로 정렬돼 있으므로(부두가 크레인에 자동정합), 크레인은 그대로 두고 배만 옮긴다.
    ///
    /// ── 계산(실척 m) ──
    ///   배 중심선 X = 바다측레일 X + 접안틈 + 선폭/2.   (배 피벗 = 중심선 x0·미드십 z0·흘수선 y0)
    ///   배 Z = 크레인 Z (크레인이 미드십 화물구 위에 오게 — 거주구/브리지 z −92~−114 간섭 회피).
    ///   배 Y = 0 (흘수선 = 안벽/바다 표면 y0).
    ///   검증: (바다측레일 X + 아웃리치) ≥ (배 중심선 + 선폭/2) 이면 크레인이 전폭을 덮는다.
    /// </summary>
    public static class ShipBerthMenu
    {
        const string CraneName = "STS_Crane";

        /// <summary>바다측 레일 ↔ 배 현측 접안 틈(실척 m). 레일-코핑 ~3.5m + 펜더 ~1.5m.</summary>
        const float BerthGapMeters = 5f;

        /// <summary>
        /// 컨테이너선을 크레인 안벽에 접안 정렬한다. 성공 시 true.
        ///   ShipCreator.CreateShip이 생성 직후 자동 호출한다(별도 메뉴 없이 생성 한 번으로 접안까지).
        ///   접안은 '생성 순간' 자동으로만 수행하며 별도 정렬 메뉴는 두지 않는다(오너 지시 2026-07-03).
        ///   크레인·배 위치가 나중에 바뀌면 배를 다시 생성하면 재접안된다.
        ///   크레인('STS_Crane')이 아직 없으면 false + 안내 메시지(배는 그대로 둠 — 호출부가 처리).
        /// </summary>
        public static bool TryBerth(GameObject ship, out string msg)
        {
            var crane = GameObject.Find(CraneName);
            if (crane == null)
            {
                msg = $"[ShipBerth] 씬에 '{CraneName}'가 없어 접안을 건너뜁니다. 크레인을 먼저 생성한 뒤 컨테이너선을 다시 생성하면 자동 접안됩니다.";
                return false;
            }

            float scale = StsConfig.ModelScale;

            // 1) 바다측 레일 X(월드) — Rail_Water 바운즈 중심. 폴백: 크레인 X + 게이지(실척).
            float waterRailX;
            if (TryFindWorldX(crane.transform, StsPartNames.RailPrefix + "Water", out float railX))
                waterRailX = railX;
            else
            {
                waterRailX = crane.transform.position.x + StsConfig.LegGaugeXMeters * scale;
                Debug.LogWarning($"[ShipBerth] 'Rail_Water'를 못 찾아 폴백(크레인 X + 게이지)으로 바다측 레일 X를 추정합니다.");
            }

            // 2) 목표 좌표(모델 단위)
            float beamHalf   = ShipConfig.BeamMeters * 0.5f * scale;   // 선폭/2
            float berthGap   = BerthGapMeters * scale;                 // 접안 틈
            float targetX = waterRailX + berthGap + beamHalf;          // 배 중심선 X(피벗=중심선)
            float targetZ = crane.transform.position.z;               // 크레인이 미드십 위
            float targetY = 0f;                                        // 흘수선 = 표면 y0

            // 3) 아웃리치 커버 검증 — 크레인이 전폭을 덮는가.
            float outreachReachX;
            var trolley = crane.GetComponentInChildren<TrolleyMover>();
            if (trolley != null) outreachReachX = crane.transform.position.x + trolley.Max;  // 트롤리 바다쪽 한계(월드)
            else                 outreachReachX = waterRailX + 45f * scale;                  // 폴백: 정격 아웃리치 45m
            float shipFarSideX = targetX + beamHalf;                   // 배 반대편(바다측) 현측
            float coverMargin  = outreachReachX - shipFarSideX;        // ≥0 이면 전폭 커버

            // 4) 배치
            Undo.RecordObject(ship.transform, "Berth ContainerShip");
            ship.transform.position = new Vector3(targetX, targetY, targetZ);
            EditorUtility.SetDirty(ship.transform);

            // 5) 보고(실척 환산)
            float inv = StsConfig.InvModelScale;
            msg =
                $"[ShipBerth] 컨테이너선 접안 완료 — 배 중심선 X={targetX:F3}u({targetX*inv:F1}m), Z={targetZ:F3}u, Y=0(흘수선).\n" +
                $"  바다측 레일 X={waterRailX:F3}u({waterRailX*inv:F1}m) + 접안틈 {BerthGapMeters:F0}m + 선폭/2 {ShipConfig.BeamMeters*0.5f:F1}m.\n" +
                $"  아웃리치 도달 X={outreachReachX:F3}u({outreachReachX*inv:F1}m) vs 반대현측 X={shipFarSideX:F3}u({shipFarSideX*inv:F1}m) → " +
                (coverMargin >= 0f ? $"전폭 커버 ✓ 여유 {coverMargin*inv:F1}m." : $"⚠ 커버 부족 {(-coverMargin*inv):F1}m — 접안틈을 줄이거나 크레인 아웃리치 확인.");
            if (coverMargin >= 0f) Debug.Log(msg);
            else                   Debug.LogWarning(msg);
            return true;
        }

        /// <summary>root 하위에서 이름이 일치하는 첫 Transform의 월드 렌더러 바운즈 중심 X를 반환.</summary>
        static bool TryFindWorldX(Transform root, string name, out float worldX)
        {
            worldX = 0f;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name != name) continue;
                if (TryWorldBounds(t, out Bounds b)) { worldX = b.center.x; return true; }
                worldX = t.position.x; return true;
            }
            return false;
        }

        static bool TryWorldBounds(Transform t, out Bounds b)
        {
            b = default;
            var rends = t.GetComponentsInChildren<Renderer>(true);
            if (rends == null || rends.Length == 0) return false;
            b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            return true;
        }
    }
}
