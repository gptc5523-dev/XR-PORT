using UnityEditor;
using UnityEngine;
using Container.Crane.Flat;

namespace Container.Crane.Flat.EditorTools
{
    /// <summary>
    /// 평면 모드(XREAL One Pro·모니터) 강제 지정 메뉴 — 기본은 '자동'(헤드셋이 붙어 있으면 VR).
    ///
    /// 에디터에서 Play 를 눌렀을 때 어느 경로로 갈지 강제할 때 쓴다. 값은 PlayerPrefs 에 저장되므로
    /// 같은 PC 의 빌드 실행에도 그대로 적용된다. 빌드 실행 인자 -flat / -vr 이 이 값보다 우선한다.
    ///
    /// 메뉴 위치는 프로젝트 규약(Tool/)을 따른다.
    /// </summary>
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
