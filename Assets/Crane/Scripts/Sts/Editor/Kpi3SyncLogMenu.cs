#if UNITY_EDITOR
using UnityEditor;
using AIXRCrane.Crane.Sts.Net;

namespace AIXRCrane.Crane.Sts.EditorTools
{
    /// <summary>지표 3 동기화 로그 켜기/끄기 — 켜 두면 다음 플레이부터 접속 중 KPI/kpi3_log_*.csv 가 쌓인다(빌드는 실행 인자 -kpi3).</summary>
    static class Kpi3SyncLogMenu
    {
        const string Path = "PLC/지표3 동기화 로그 (켜기·끄기)";

        [MenuItem(Path, false, 8)]
        static void Toggle() => EditorPrefs.SetBool(Kpi3SyncLog.EditorPrefKey, !EditorPrefs.GetBool(Kpi3SyncLog.EditorPrefKey, false));

        [MenuItem(Path, true)]
        static bool Check()
        {
            Menu.SetChecked(Path, EditorPrefs.GetBool(Kpi3SyncLog.EditorPrefKey, false));
            return true;
        }
    }
}
#endif
