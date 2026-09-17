using System;
using System.Reflection;
using UnityEngine;

namespace AIXRCrane.Crane.Sts
{
    /// <summary>
    /// 시점변경(걷기) 모드에서 오른손 스틱을 '아래'로 밀면 180° 뒤도는 동작(Turn Around)만 끈다.
    ///   - 좌우 회전(SnapTurn/ContinuousTurn 좌우)은 그대로 둠 → Turn InputAction을 통째로 끄지 않음
    ///   - 180° 뒤돌기는 별도 액션이 아니라 회전 Provider의 enableTurnAround 플래그라서, 그 플래그만 false로
    ///   - MonoBehaviour 아님 → 씬에 부착 불필요. [RuntimeInitializeOnLoadMethod] 로 매 Play 시 자동 실행.
    /// XRI 3.x: ...Locomotion.Turning.SnapTurnProvider / ContinuousTurnProvider,
    /// XRI 2.x(레거시): ...SnapTurnProviderBase 모두 public bool enableTurnAround 를 가짐(리플렉션으로 처리).
    /// </summary>
    public static class XRTurnAroundDisabler
    {
        const bool DebugLog = true;

        // 회전 Provider 후보 타입 (네임스페이스 버전차 대응 — 없으면 무시)
        static readonly string[] TypeNames =
        {
            "UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning.SnapTurnProvider, Unity.XR.Interaction.Toolkit",
            "UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning.ContinuousTurnProvider, Unity.XR.Interaction.Toolkit",
            "UnityEngine.XR.Interaction.Toolkit.SnapTurnProviderBase, Unity.XR.Interaction.Toolkit",          // 레거시
            "UnityEngine.XR.Interaction.Toolkit.ActionBasedSnapTurnProvider, Unity.XR.Interaction.Toolkit",   // 레거시
        };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            int n = DisableTurnAround();
            if (DebugLog)
                Debug.Log($"[XR] 스틱 아래 180° 뒤돌기(enableTurnAround) 비활성화 — Provider {n}개 처리 (좌우 회전은 유지)");
        }

        static int DisableTurnAround()
        {
            int n = 0;
            foreach (var typeName in TypeNames)
            {
                var t = Type.GetType(typeName);
                if (t == null) continue;

                var prop = t.GetProperty("enableTurnAround",
                    BindingFlags.Public | BindingFlags.Instance);
                if (prop == null || !prop.CanWrite) continue;

                // 비활성 오브젝트까지 포함해 모두 끔
                foreach (var o in UnityEngine.Object.FindObjectsByType(
                             t, FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    prop.SetValue(o, false);
                    n++;
                }
            }
            return n;
        }
    }
}
