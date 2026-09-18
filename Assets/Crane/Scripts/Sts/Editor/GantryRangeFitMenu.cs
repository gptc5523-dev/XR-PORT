#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace AIXRCrane.Crane.Sts.EditorTools
{
    /// <summary>갠트리 주행범위 맞춤의 에디터 진입점 — 부두 바닥 생성·크레인 생성이 호출하는
    /// ApplyFit(Undo/Dirty 래퍼)만 보유. 실제 계산은 런타임 SSOT인 GantryRangeFit.Apply.</summary>
    public static class GantryRangeFitMenu
    {
        /// <summary>크레인 바퀴가 레일을 안 벗어나는 최대 대칭 주행범위를 계산해 GantryMover에 적용, 성공 시 true.
        /// 부두 바닥 생성 직후·크레인 생성 시 호출. 계산 자체는 GantryRangeFit.Apply, 여기는 Undo/Dirty만.</summary>
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
