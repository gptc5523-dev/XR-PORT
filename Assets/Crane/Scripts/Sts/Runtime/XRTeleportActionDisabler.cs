#if ENABLE_INPUT_SYSTEM
using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AIXRCrane.Crane.Sts
{
    /// <summary>
    /// XR 텔레포트 InputAction(이름에 "Teleport" 포함)을 Play 진입 시 자동 비활성화.
    ///   - MonoBehaviour 아님 → 씬에 컴포넌트 안 붙여도 됨
    ///   - [RuntimeInitializeOnLoadMethod(AfterSceneLoad)] 로 매 Play 시 자동 실행
    ///   - InputSystem.onActionChange 구독 → 누가 다시 enable 하면 즉시 다시 disable
    /// 시점 회전(SnapTurn/ContinuousTurn)·걷기(ContinuousMove)는 이름이 달라 영향 없음.
    /// </summary>
    public static class XRTeleportActionDisabler
    {
        const bool DebugLog = true;
        static bool _subscribed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            int n = DisableTeleportActions();
            if (DebugLog)
                Debug.Log($"[XR] Teleport InputAction 자동 비활성화 — {n}개 (이후 enable되면 자동 재차단)");

            if (!_subscribed)
            {
                InputSystem.onActionChange += OnActionChange;
                _subscribed = true;
                // 도메인 리로드 비활성 환경 대응 — 이전 등록 정리
                Application.quitting -= Cleanup;
                Application.quitting += Cleanup;
            }
        }

        static void Cleanup()
        {
            InputSystem.onActionChange -= OnActionChange;
            _subscribed = false;
        }

        static int DisableTeleportActions()
        {
            int n = 0;
            foreach (var asset in Resources.FindObjectsOfTypeAll<InputActionAsset>())
            {
                if (asset == null) continue;
                foreach (var map in asset.actionMaps)
                {
                    bool mapIsTele = NameHasTeleport(map.name);
                    foreach (var act in map.actions)
                    {
                        if (!act.enabled) continue;
                        if (mapIsTele || NameHasTeleport(act.name)) { act.Disable(); n++; }
                    }
                }
            }
            return n;
        }

        static void OnActionChange(object obj, InputActionChange change)
        {
            if (change == InputActionChange.ActionEnabled)
            {
                if (obj is InputAction act &&
                    (NameHasTeleport(act.name) || (act.actionMap != null && NameHasTeleport(act.actionMap.name))))
                    act.Disable();
            }
            else if (change == InputActionChange.ActionMapEnabled)
            {
                if (obj is InputActionMap map && NameHasTeleport(map.name))
                    foreach (var a in map.actions) if (a.enabled) a.Disable();
            }
        }

        static bool NameHasTeleport(string s)
            => !string.IsNullOrEmpty(s) && s.IndexOf("Teleport", StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
#endif
