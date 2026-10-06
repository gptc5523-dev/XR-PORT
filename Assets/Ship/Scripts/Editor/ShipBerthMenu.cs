using UnityEngine;
using UnityEditor;
using AIXRCrane.Crane.Sts;              // StsConfig, StsPartNames, TrolleyMover

namespace AIXRCrane.Ship.EditorTools
{
    /// <summary>컨테이너선을 STS 크레인·안벽에 접안 정렬 — 축이 같아 평행이동만. 중심선 X = 바다측레일 + 접안틈 + 선폭/2,
    /// Z = 크레인 Z, Y = SeaLevelY(흘수선=수면). 아웃리치가 반대 현측까지 닿는지 검증.</summary>
    public static class ShipBerthMenu
    {
        const string CraneName = StsPartNames.StsCraneRoot;

        /// <summary>배 현측 ↔ 안벽 전면 틈(실척 m) — 방충재(펜더) 압축 여유.</summary>
        const float FenderClearanceM = 1.5f;

        /// <summary>바다측 레일 ↔ 배 현측 접안 틈(실척 m) = 레일→안벽 가장자리 + 펜더 틈. 에이프런 유도값.</summary>
        static float BerthGapMeters => PortConfig.ApronSeawardM + FenderClearanceM;

        /// <summary>컨테이너선을 크레인 안벽에 접안 정렬한다(ShipCreator.CreateShip이 생성 직후 자동 호출, 별도 메뉴 없음). 성공 시 true.
        /// 앵커는 부두(QuayRail) 우선, 둘 다 없으면 false(배는 그대로).</summary>
        public static bool TryBerth(GameObject ship, out string msg)
        {
            var crane = GameObject.Find(CraneName);
            float scale = StsConfig.ModelScale;

            // 1) 바다측 레일 X(월드) — 안벽 위치의 SSOT는 부두. 우선순위: ①Quay_Ground 바다측 QuayRail(크레인 없어도 접안)
            // ②크레인 Rail_Water* ③크레인 위치+게이지(실척, 최후 폴백).
            float waterRailX; float quayCenterZ; bool haveQuay;
            string anchor;
            haveQuay = TryQuayBerthAnchor(out waterRailX, out quayCenterZ);
            if (haveQuay) anchor = $"부두 '{StsPartNames.QuayGround}'의 바다측 {StsPartNames.QuayRail}";
            else if (crane != null && TryFindWorldX(crane.transform, StsPartNames.RailPrefix + "Water", out float railX))
            { waterRailX = railX; anchor = "크레인 Rail_Water"; }
            else if (crane != null)
            {
                waterRailX = crane.transform.position.x + StsConfig.LegGaugeXMeters * scale;
                anchor = "크레인 위치 + 게이지(폴백)";
                Debug.LogWarning("[ShipBerth] 부두 QuayRail·크레인 Rail_Water를 못 찾아 폴백(크레인 X + 게이지)으로 바다측 레일 X를 추정합니다.");
            }
            else
            {
                msg = $"[ShipBerth] 씬에 부두('{StsPartNames.QuayGround}')도 크레인('{CraneName}')도 없어 접안을 건너뜁니다. " +
                      $"'Ground/부두 바닥 생성'을 먼저 실행하면 컨테이너선이 자동으로 접안합니다.";
                return false;
            }

            // 2) 목표 좌표(모델 단위)
            float beamHalf   = ShipConfig.BeamMeters * 0.5f * scale;   // 선폭/2
            float berthGap   = BerthGapMeters * scale;                 // 접안 틈
            float targetX = waterRailX + berthGap + beamHalf;          // 배 중심선 X(피벗=중심선)
            // Z: 크레인이 있으면 크레인이 미드십 위에 오게, 없으면 선석(안벽) 중앙.
            float targetZ = crane != null ? crane.transform.position.z : quayCenterZ;
            // 배 피벗 = 흘수선 → 수면 높이(StsConfig.SeaLevelY)에 맞춘다.
            float targetY = StsConfig.SeaLevelY;                       // 흘수선 = 수면

            // 3) 아웃리치 커버 검증 — 크레인이 전폭을 덮는가. 크레인이 없으면 검증 생략(배치는 그대로 한다).
            float shipFarSideX = targetX + beamHalf;                   // 배 반대편(바다측) 현측
            float outreachReachX = 0f, coverMargin = 0f;
            if (crane != null)
            {
                var trolley = crane.GetComponentInChildren<TrolleyMover>();
                if (trolley != null) outreachReachX = crane.transform.position.x + trolley.Max;  // 트롤리 바다쪽 한계(월드)
                else                 outreachReachX = waterRailX + 45f * scale;                  // 폴백: 정격 아웃리치 45m
                coverMargin = outreachReachX - shipFarSideX;           // ≥0 이면 전폭 커버
            }

            // 4) 배치
            Undo.RecordObject(ship.transform, "Berth ContainerShip");
            ship.transform.position = new Vector3(targetX, targetY, targetZ);
            EditorUtility.SetDirty(ship.transform);

            // 5) 보고(실척 환산)
            float inv = StsConfig.InvModelScale;
            msg =
                $"[ShipBerth] 컨테이너선 접안 완료 — 배 중심선 X={targetX:F3}u({targetX*inv:F1}m), Z={targetZ:F3}u, " +
                $"Y={targetY:F3}u(흘수선=수면, 안벽 데크 아래 {StsConfig.QuayDeckAboveSeaMeters:F1}m).\n" +
                $"  앵커={anchor} · 바다측 레일 X={waterRailX:F3}u({waterRailX*inv:F1}m) + 접안틈 {BerthGapMeters:F1}m" +
                $"(에이프런 해측 {PortConfig.ApronSeawardM:F1} + 펜더 {FenderClearanceM:F1}) + 선폭/2 {ShipConfig.BeamMeters*0.5f:F1}m.\n" +
                (crane == null
                    ? $"  크레인 없음 → 아웃리치 커버 검증 생략(안벽 중앙 Z에 접안). 크레인을 만들면 부두 재생성 시 자동 재접안."
                    : $"  아웃리치 도달 X={outreachReachX:F3}u({outreachReachX*inv:F1}m) vs 반대현측 X={shipFarSideX:F3}u({shipFarSideX*inv:F1}m) → " +
                      (coverMargin >= 0f ? $"전폭 커버 ✓ 여유 {coverMargin*inv:F1}m." : $"⚠ 커버 부족 {(-coverMargin*inv):F1}m — 접안틈을 줄이거나 크레인 아웃리치 확인."));
            if (crane == null || coverMargin >= 0f) Debug.Log(msg);
            else                                    Debug.LogWarning(msg);
            return true;
        }

        /// <summary>접안 앵커 — 씬 QuayRail 렌더러 실측으로 바다측(+X) 레일 중심 X·안벽 중심 Z(부두가 바뀌면 배가 따라온다).
        /// ★ bounds.max.x 가 아니라 center.x — 바깥면을 쓰면 레일 반폭만큼 배가 밀린다.</summary>
        static bool TryQuayBerthAnchor(out float waterRailX, out float centerZ)
        {
            waterRailX = 0f; centerZ = 0f;
            var quay = GameObject.Find(StsPartNames.QuayGround);
            if (quay == null) return false;

            bool found = false;
            float minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (var r in quay.GetComponentsInChildren<Renderer>())
            {
                if (r.gameObject.name != StsPartNames.QuayRail) continue;
                var b = r.bounds;
                if (!found || b.center.x > waterRailX) waterRailX = b.center.x;
                minZ = Mathf.Min(minZ, b.min.z);
                maxZ = Mathf.Max(maxZ, b.max.z);
                found = true;
            }
            if (!found) return false;
            centerZ = (minZ + maxZ) * 0.5f;
            return true;
        }

        /// <summary>씬의 컨테이너선을 다시 접안(배 없으면 통과). 부두를 다시 깔면 안벽 X·수면 Y가 바뀌므로 부두 쪽에서 부른다.</summary>
        public static void ReberthExistingShip()
        {
            var ship = GameObject.Find(ShipConfig.ShipRootName);
            if (ship == null) return;
            TryBerth(ship, out _);
        }

        /// <summary>root 하위에서 이름이 namePrefix로 시작하는 Transform 중 가장 바다측(+X) 월드 X를 반환.
        /// 접두 비교 — 생성부가 일련번호를 붙인다(예: Rail_Water_1).</summary>
        static bool TryFindWorldX(Transform root, string namePrefix, out float worldX)
        {
            worldX = 0f; bool found = false;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (!t.name.StartsWith(namePrefix)) continue;
                float x = SceneUtil.TryBounds(t, out Bounds b, true) ? b.center.x : t.position.x;
                worldX = found ? Mathf.Max(worldX, x) : x;   // 바다측 = +X 관례
                found = true;
            }
            return found;
        }

    }
}
