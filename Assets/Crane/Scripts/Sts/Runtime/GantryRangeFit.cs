using UnityEngine;

namespace Container.Crane.Sts
{
    /// <summary>
    /// 갠트리 주행범위(로컬 Z Min/Max)를 '씬의 부두 레일 + 크레인 바퀴 실측'으로 계산해 적용하는 런타임 SSOT.
    ///
    /// [왜 런타임인가]
    ///   주행범위 계산이 에디터 메뉴(GantryRangeFitMenu)에만 있어, 전량 양하 시나리오를 그냥 Play하면
    ///   갠트리 Min/Max가 기본값(±1)인 채여서 배 앞/뒤 베이가 '도달불가'로 걸러지던 문제가 있었다.
    ///   → 계산을 여기(런타임)로 옮겨 자동 시나리오가 시작 시 스스로 맞추게 하고, 에디터 메뉴·부두/크레인
    ///     생성기는 이 계산을 '공유'하게 한다(한 곳만 고치면 모두 일치).
    ///
    ///   주행반경 = (크레인 중심→레일 안쪽 끝 거리) − (크레인 중심→바깥 바퀴 거리)
    ///   → 끝까지 가면 바깥 바퀴가 노란선(레일) 안쪽 끝에 '딱' 닿고 그 이상은 안 나간다. 좌우 대칭.
    /// </summary>
    public static class GantryRangeFit
    {
        const string CraneName = "STS_Crane";

        /// <summary>씬에서 STS_Crane(또는 첫 StsCrane)과 GantryMover를 찾아 주행범위를 맞춘다. 적용 여부 반환.</summary>
        public static bool TryFitInScene(out string msg)
        {
            msg = "";
            var go = GameObject.Find(CraneName);
            if (go == null)
            {
                var c = Object.FindFirstObjectByType<StsCrane>();
                go = c != null ? c.gameObject : null;
            }
            if (go == null) { msg = $"'{CraneName}'(StsCrane)를 못 찾음."; return false; }
            var gantry = go.GetComponent<GantryMover>();
            if (gantry == null) { msg = $"'{go.name}'에 GantryMover가 없음."; return false; }
            return Apply(go, gantry, out msg);
        }

        /// <summary>
        /// 크레인 바퀴가 레일을 안 벗어나는 '최대 대칭 주행범위'를 계산해 GantryMover.Configure로 적용. 성공 시 true.
        /// 크레인/부두/플레이어를 재생성·이동하지 않고 GantryMover의 Min/Max만 바꾼다. (에디터/런타임 공용)
        /// </summary>
        public static bool Apply(GameObject crane, GantryMover gantry, out string msg)
        {
            msg = "";
            if (crane == null || gantry == null) { msg = "crane/gantry 인자가 null."; return false; }

            var quay = GameObject.Find(StsPartNames.QuayGround);
            if (quay == null)
            {
                msg = $"'{StsPartNames.QuayGround}'가 없습니다. 먼저 부두 바닥을 생성하세요.";
                return false;
            }

            // ★ 한계 기준 = 노란 차선(Lane) 안쪽. 차선은 회색 레일보다 짧게 깔려 바퀴가 노란선 안에 들어오게 한다.
            //   (차선을 못 찾으면 회색 레일 QuayRail로 폴백.)
            string limName = "Lane";
            if (!TryChildBoundsZ(quay, "Lane", out float limMinZ, out float limMaxZ))
            {
                limName = StsPartNames.QuayRail;
                if (!TryChildBoundsZ(quay, StsPartNames.QuayRail, out limMinZ, out limMaxZ))
                {
                    msg = $"'{StsPartNames.QuayGround}' 안에서 'Lane'/'QuayRail'을 못 찾았습니다.";
                    return false;
                }
            }

            float craneZ     = crane.transform.position.z;                    // 크레인 중심(월드 Z)
            float reach      = Mathf.Min(limMaxZ - craneZ, craneZ - limMinZ); // 중심→가까운 노란선 끝(대칭 보장)
            float wheelHalfZ = WheelHalfZ(crane, craneZ);                     // 중심→바깥 바퀴(실측)
            float halfTravel = Mathf.Max(0.05f, reach - wheelHalfZ);          // 바깥 바퀴가 노란선 끝에 닿는 지점까지만

            float homeLocalZ = crane.transform.localPosition.z;
            gantry.Configure(homeLocalZ - halfTravel, homeLocalZ + halfTravel);

            msg = $"주행반경 ±{halfTravel:F3} (총 {halfTravel * 2f:F3}) = {limName}도달 {reach:F3} − 바퀴반경 {wheelHalfZ:F3}. " +
                  $"{limName} [{limMinZ:F3}~{limMaxZ:F3}], 바깥 바퀴가 노란선 안쪽 끝에 닿고 안 벗어남.";
            return true;
        }

        // 크레인 'Wheel' 오브젝트들의 월드 Z 바운즈로 '중심→바깥 바퀴' 거리 측정. 없으면 게이지 기반 폴백.
        static float WheelHalfZ(GameObject crane, float craneZ)
        {
            float half = 0f; bool any = false;
            foreach (var t in crane.GetComponentsInChildren<Transform>(true))
            {
                if (!t.name.StartsWith("Wheel")) continue;          // "Wheel", "Wheel_Hub"
                var r = t.GetComponent<Renderer>();
                if (r == null) continue;
                half = Mathf.Max(half, Mathf.Max(r.bounds.max.z - craneZ, craneZ - r.bounds.min.z));
                any = true;
            }
            if (any) return half;
            // 폴백(바퀴 못 찾을 때): 게이지/2 + 보기 전장/2 = (16/24)/2 + 0.104/2 ≈ 0.385  [H2] SSOT 참조
            return (StsConfig.GantryBaseZMeters * StsConfig.ModelScale) * 0.5f + StsConfig.BogieLengthZ * 0.5f;
        }

        // parent 안에서 이름이 prefix로 시작하는 렌더러들의 월드 Z min/max
        static bool TryChildBoundsZ(GameObject parent, string prefix, out float minZ, out float maxZ)
        {
            minZ = maxZ = 0f;
            bool any = false;
            foreach (var t in parent.GetComponentsInChildren<Transform>(true))
            {
                if (!t.name.StartsWith(prefix)) continue;
                var r = t.GetComponent<Renderer>();
                if (r == null) continue;
                float lo = r.bounds.min.z, hi = r.bounds.max.z;
                if (!any) { minZ = lo; maxZ = hi; any = true; }
                else { minZ = Mathf.Min(minZ, lo); maxZ = Mathf.Max(maxZ, hi); }
            }
            return any;
        }
    }
}
