using UnityEditor;
using UnityEngine;

namespace AIXRCrane.Crane.Flat.EditorTools
{
    /// <summary>평면 모드(XREAL One Pro·모니터) 강제 지정 메뉴 — 기본은 자동(헤드셋 붙어 있으면 VR).
    /// PlayerPrefs 저장이라 빌드 실행에도 적용되며, 실행 인자 -flat/-vr 이 이 값보다 우선한다.</summary>
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

        // 체크 표시 — 현재 선택된 항목에만 ✓. validate 함수는 항상 true 를 반환해 항목을 활성 상태로 둔다.
        [MenuItem(ItemAuto, validate = true)]
        static bool ValidateAuto() { Menu.SetChecked(ItemAuto, Current == 0); return true; }

        [MenuItem(ItemFlat, validate = true)]
        static bool ValidateFlat() { Menu.SetChecked(ItemFlat, Current == 1); return true; }

        [MenuItem(ItemVr, validate = true)]
        static bool ValidateVr() { Menu.SetChecked(ItemVr, Current == 2); return true; }
    }
}
