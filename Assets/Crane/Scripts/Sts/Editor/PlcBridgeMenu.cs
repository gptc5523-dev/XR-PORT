#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using Container.Crane.Sts;
using Container.Crane.Sts.Plc;

namespace Container.Crane.Sts.EditorTools
{
    /// <summary>
    /// 가상 PLC 시연용 에디터 메뉴 — 씬의 StsCrane에 <see cref="PlcBridge"/>를 부착·구동/원복한다.
    /// 부착·구동은 Active=true(가상 PLC가 3축 자동 운전 + PlcDriven=true, 가속알람 1021/2021/3021 유효),
    /// 원복은 컴포넌트 제거(직접조종 VR 복귀). 비파괴 — 언제든 원복 가능.
    /// </summary>
    public static class PlcBridgeMenu
    {
        [MenuItem("PLC/가상 PLC 부착·구동", false, 2)]
        public static void AttachAndDrive()
        {
            var crane = Object.FindFirstObjectByType<StsCrane>();
            if (crane == null)
            {
                Debug.LogWarning("[PlcBridgeMenu] 씬에 StsCrane이 없습니다 — 먼저 'Object/크레인/STS 크레인 생성' 실행.");
                return;
            }

            var bridge = crane.GetComponent<PlcBridge>();
            if (bridge == null)
                bridge = Undo.AddComponent<PlcBridge>(crane.gameObject);

            Undo.RecordObject(bridge, "Configure PLC Bridge");
            bridge.EditorConfigureVirtual(false);
            EditorUtility.SetDirty(bridge);
            EditorPrefs.SetBool("PlcBridge.forceReplay", false);   // Virtual 모드 — 복원 비활성

            Selection.activeGameObject = crane.gameObject;
            Debug.Log("[PlcBridgeMenu] PlcBridge 부착·Active ON. ▶Play 진입 → 3축이 가상 PLC로 양하 사이클 자동 운전. " +
                      "Inspector에서 'injectAggressive'를 켜면 가속알람(1021/2021/3021) 자연발생. " +
                      "원복은 Inspector에서 PlcBridge 컴포넌트 제거(PlcDriven 자동 복원).");
        }

        [MenuItem("PLC/가상 PLC 부착·구동 (CSV 재생)", false, 3)]
        public static void AttachAndReplayCsv()
        {
            var crane = Object.FindFirstObjectByType<StsCrane>();
            if (crane == null)
            {
                Debug.LogWarning("[PlcBridgeMenu] 씬에 StsCrane이 없습니다 — 먼저 'Object/크레인/STS 크레인 생성' 실행.");
                return;
            }

            // PlcSim/output 기본 폴더에서 재생할 CSV 선택
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string defaultDir = Path.Combine(projectRoot, "PlcSim", "output", "S02");
            if (!Directory.Exists(defaultDir)) defaultDir = Path.Combine(projectRoot, "PlcSim");
            string csv = EditorUtility.OpenFilePanel("재생할 PLC CSV 선택 (PlcSim/output)", defaultDir, "csv");
            if (string.IsNullOrEmpty(csv)) return;   // 취소

            var bridge = crane.GetComponent<PlcBridge>();
            if (bridge == null) bridge = Undo.AddComponent<PlcBridge>(crane.gameObject);

            Undo.RecordObject(bridge, "Configure PLC Bridge (CSV)");
            bridge.EditorConfigureReplay(csv);
            EditorUtility.SetDirty(bridge);
            // 도메인 리로드로 인스펙터가 리셋돼도 Awake가 복원하도록 EditorPrefs에 기록.
            EditorPrefs.SetBool("PlcBridge.forceReplay", true);
            EditorPrefs.SetString("PlcBridge.csvPath", csv);

            Selection.activeGameObject = crane.gameObject;
            Debug.Log($"[PlcBridgeMenu] PlcBridge 부착·CsvReplay ON → {Path.GetFileName(csv)}. " +
                      "▶Play 진입 → 기록된 시나리오대로 크레인이 재현됩니다. 원복은 Inspector에서 PlcBridge 제거.");
        }

        // [메뉴 삭제 2026-06-17] '가상 PLC 분리(직접조종 복귀)' 메뉴 제거(오너 지시).
        //   분리는 Inspector에서 PlcBridge 컴포넌트를 떼면 된다 — PlcBridge.OnDisable이 PlcDriven=false를
        //   자동 복원하므로 가속 오경보 잔류 없이 안전(과거 이 메뉴가 하던 정리를 컴포넌트에 내재화).
    }
}
#endif
