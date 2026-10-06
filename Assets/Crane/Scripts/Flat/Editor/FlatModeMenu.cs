using UnityEditor;
using UnityEngine;

namespace AIXRCrane.Crane.Flat.EditorTools
{
    /// <summary>평면/VR 모드 강제 지정 메뉴(기본 자동). PlayerPrefs 저장 — 빌드에도 적용, 실행 인자 -flat/-vr 이 우선.</summary>
    static class FlatModeMenu
    {
        const string Root = "Tool/평면 모드 (XREAL·모니터)/";
        const string ItemAuto = Root + "자동 — 헤드셋 있으면 VR";
        const string ItemFlat = Root + "평면 강제";
        const string ItemVr = Root + "VR 강제";

        // PlayerPrefs 값: 0=자동, 1=평면 강제, 2=VR 강제 (FlatModeBootstrap.ResolveForce 와 동일 규약)
        static int Current => PlayerPrefs.GetInt(FlatModeBootstrap.ForcePrefKey, 0);

        static void Set(int v)
        {
            PlayerPrefs.SetInt(FlatModeBootstrap.ForcePrefKey, v);
            PlayerPrefs.Save();
            Debug.Log($"[FlatMode] 실행 모드 → {(v == 1 ? "평면 강제" : v == 2 ? "VR 강제" : "자동(헤드셋 감지)")}");
        }

        [MenuItem(ItemAuto, priority = 100)] static void SetAuto() => Set(0);
        [MenuItem(ItemFlat, priority = 101)] static void SetFlat() => Set(1);
        [MenuItem(ItemVr, priority = 102)] static void SetVr() => Set(2);

        // 현재 항목에만 ✓. validate 는 항상 true(항목 활성 유지).
        [MenuItem(ItemAuto, validate = true)]
        static bool ValidateAuto() { Menu.SetChecked(ItemAuto, Current == 0); return true; }

        [MenuItem(ItemFlat, validate = true)]
        static bool ValidateFlat() { Menu.SetChecked(ItemFlat, Current == 1); return true; }

        [MenuItem(ItemVr, validate = true)]
        static bool ValidateVr() { Menu.SetChecked(ItemVr, Current == 2); return true; }
    }
}
