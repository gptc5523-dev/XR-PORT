using UnityEngine;
using UnityEditor;
using Container.Crane.Sts;              // StsConfig, StsPartNames, TrolleyMover

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
    ///   배 Y = StsConfig.SeaLevelY (흘수선 = 수면 = 안벽 데크 아래 코핑고 4 m).
    ///   검증: (바다측레일 X + 아웃리치) ≥ (배 중심선 + 선폭/2) 이면 크레인이 전폭을 덮는다.
    /// </summary>
    public static class ShipBerthMenu
    {
        const string CraneName = "STS_Crane";

        /// <summary>배 현측 ↔ 안벽 전면 틈(실척 m) — 방충재(펜더) 압축 여유.</summary>
        const float FenderClearanceM = 1.5f;

        /// <summary>바다측 레일 ↔ 배 현측 접안 틈(실척 m) = 레일→안벽 가장자리 + 펜더 틈.
        /// 종전엔 5f 고정이었고 주석이 "레일-코핑 ~3.5m + 펜더 ~1.5m" 였다. 현 부두는
        /// PortConfig.ApronSeawardM 이 4m 라, 5f 를 두면 펜더 틈이 조용히 1.0m 로 줄어든다.
        /// 에이프런을 바꾸면 접안 위치도 따라오도록 유도값으로 바꾼다. 4 + 1.5 = 5.5m.</summary>
        static float BerthGapMeters => PortConfig.ApronSeawardM + FenderClearanceM;

        /// <summary>
        /// 컨테이너선을 크레인 안벽에 접안 정렬한다. 성공 시 true.
        ///   ShipCreator.CreateShip이 생성 직후 자동 호출한다(별도 메뉴 없이 생성 한 번으로 접안까지).
        ///   접안은 '생성 순간' 자동으로만 수행하며 별도 정렬 메뉴는 두지 않는다(오너 지시 2026-07-03).
        ///   크레인·배 위치가 나중에 바뀌면 배를 다시 생성하면 재접안된다.
        ///   안벽 위치는 부두(Quay_Ground의 바다측 QuayRail)가 1순위 앵커라 **크레인 없이 부두만 있어도 접안**한다.
        ///   부두도 크레인도 없을 때만 false + 안내(배는 그대로 둠 — 호출부가 처리).
        /// </summary>
        public static bool TryBerth(GameObject ship, out string msg)
        {
            var crane = GameObject.Find(CraneName);
            float scale = StsConfig.ModelScale;

            // 1) 바다측 레일 X(월드) — 안벽 위치의 SSOT는 '부두'다. 우선순위:
            //      ① Quay_Ground 의 바다측 QuayRail   ← 부두만 있으면 크레인 없이도 접안된다
            //      ② 크레인의 Rail_Water*             ← 부두를 아직 안 깐 경우
            //      ③ 크레인 위치 + 게이지(실척)        ← 최후 폴백
            //   ★ 종전엔 크레인이 없으면 바로 return false 라 배가 원점에 남았고, 원점은 슬래브 한가운데(육지)라
            //     "컨테이너선 생성하면 육지로 출력된다"가 됐다(오너 지적 2026-08-10). 부두 기준으로 바꿔 해소.
            float waterRailX; float quayCenterZ = 0f; bool haveQuay;
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
            // 배 피벗 = 흘수선 → 수면 높이에 맞춘다. 수면은 데크(y=0) 아래 코핑고만큼(StsConfig.SeaLevelY, ≈−4m).
            //   종전 0f는 '수면=데크'였던 시절 값이라, 안벽에 건현이 생긴 뒤로는 배가 물 위에 뜬 것처럼 보인다.
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

        /// <summary>부두의 바다측 주행레일 중심 X 와 안벽 중심 Z — 접안 앵커.
        /// 삭제된 StsQuayGroundCreator.TryQuayBerthAnchor 를 대신한다(부두가 FBX 로 돌아왔으므로 복구).
        ///
        /// 좌표를 산식으로 계산하지 않고 씬의 QuayRail 렌더러를 '실측'한다 — 부두를 통째로 옮기거나
        /// 에이프런 치수를 바꿔도 배가 따라오게 하기 위해서다. 바다는 +X 규약이라 두 레일 중
        /// 중심 X 가 큰 쪽이 해측이다.
        ///
        /// 렌더러 바운즈의 max.x(레일 바깥면)가 아니라 각 유닛의 center.x 를 쓴다 — 접안틈이
        /// '레일 중심' 기준으로 정의돼 있어 바깥면을 쓰면 레일 반폭(0.24m)만큼 배가 밀린다.</summary>
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

        /// <summary>
        /// 씬에 '이미 있는' 컨테이너선을 다시 접안시킨다. 배가 없으면 아무것도 하지 않는다(조용히 통과).
        ///
        /// 접안은 생성 순간 자동으로만 한다는 원칙(오너 지시 2026-07-03)은 그대로다 — 이건 그 원칙의 반대 방향
        /// 구멍을 막는 것이다. 부두를 다시 깔면 안벽 X(레일 위치)와 수면 Y(StsConfig.SeaLevelY)가 바뀌는데,
        /// 배는 제자리에 남아 공중에 뜨거나 안벽에서 떨어진다. 배를 손으로 다시 만들게 하지 않고 부두 쪽에서 맞춘다.
        /// (로그는 TryBerth가 남긴다 — 별도 메뉴는 두지 않는다.)
        /// </summary>
        public static void ReberthExistingShip()
        {
            var ship = GameObject.Find(ShipConfig.ShipRootName);
            if (ship == null) return;
            TryBerth(ship, out _);
        }

        /// <summary>root 하위에서 이름이 namePrefix로 '시작'하는 Transform 중 가장 바다측(+X) 월드 X를 반환.
        /// ★ 정확일치가 아니라 접두 비교다 — 크레인의 실제 레일 이름은 'Rail_Water_1'(생성부가 일련번호를 붙인다)이라
        ///   종전 `t.name != name` 비교는 **항상 실패**해 매번 폴백으로 빠졌다(경고 로그 상시 발생).</summary>
        static bool TryFindWorldX(Transform root, string namePrefix, out float worldX)
        {
            worldX = 0f; bool found = false;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (!t.name.StartsWith(namePrefix)) continue;
                float x = TryWorldBounds(t, out Bounds b) ? b.center.x : t.position.x;
                worldX = found ? Mathf.Max(worldX, x) : x;   // 바다측 = +X 관례
                found = true;
            }
            return found;
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
