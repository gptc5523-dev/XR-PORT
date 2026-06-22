#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Container.Crane.Sts.EditorTools
{
    /// <summary>
    /// 갠트리 주행범위 맞춤의 '에디터 진입점' — 부두 바닥 생성(StsQuayGroundCreator)·크레인 생성(StsCraneCreator)이
    /// 호출하는 ApplyFit(Undo/Dirty 래퍼)만 보유한다. 실제 계산은 런타임 SSOT인 GantryRangeFit.Apply.
    ///
    /// [메뉴 삭제 2026-06-22] 수동 'Container/STS 크레인 갠트리 주행범위 맞춤' 메뉴는 제거(오너 지시).
    ///   양하 시나리오가 Play 시 GantryRangeFit로 스스로 주행범위를 계산하므로 수동 메뉴가 불필요해짐.
    /// </summary>
    public static class GantryRangeFitMenu
    {
        /// <summary>
        /// 크레인 바퀴가 레일을 안 벗어나는 '최대 대칭 주행범위'를 계산해 GantryMover에 적용. 성공 시 true.
        /// 부두 바닥 생성 직후·크레인 생성 시에도 호출(자동 맞춤).
        ///
        /// [2026-06-22] 실제 계산은 런타임 SSOT(GantryRangeFit.Apply)로 이관했다 — 양하 시나리오가
        ///   Play 시 같은 계산을 스스로 호출할 수 있도록. 여기는 에디터 전용 Undo/Dirty 처리만 감싼다.
        /// </summary>
        public static bool ApplyFit(GameObject crane, GantryMover gantry, out string msg)
        {
            if (crane == null || gantry == null) { msg = "crane/gantry 인자가 null."; return false; }
            Undo.RecordObject(gantry, "Fit Gantry Range to Rail");
            bool ok = GantryRangeFit.Apply(crane, gantry, out msg);
            if (ok) EditorUtility.SetDirty(gantry);
            return ok;
        }
    }
}
#endif
