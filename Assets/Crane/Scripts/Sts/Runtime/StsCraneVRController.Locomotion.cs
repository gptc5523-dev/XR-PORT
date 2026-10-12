using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning;

namespace AIXRCrane.Crane.Sts
{
    public sealed partial class StsCraneVRController
    {
        // 로코모션은 '이동모드 + 높이조절 중 아님'일 때만 켠다. 캐시된 프로바이더에 매 프레임
        // 목표 상태를 직접 강제하는 선언적 방식(토글 큐 방식은 상태가 어긋남).
        void EnforceLocomotion()
        {
            EnsureLocoProviders();
            bool locoOn = (mode == Mode.Move) && !ViewHeightActive();
            foreach (var b in locoProviders)
                if (b != null && b.enabled != locoOn) b.enabled = locoOn;
            if (suppressWhileControlling != null)
                foreach (var b in suppressWhileControlling)
                    if (b != null && b.enabled != locoOn) b.enabled = locoOn;
        }

        // XR 로코모션 프로바이더를 한 번 찾아 캐시(리그가 늦게 뜰 수 있어 찾을 때까지 재시도).
        void EnsureLocoProviders()
        {
            if (locoCached) return;
            foreach (var b in FindObjectsByType<LocomotionProvider>(FindObjectsInactive.Include))
            {
                // SnapTurn(45° 점프)은 항상 끄고 토글 목록에서도 제외 — 안 그러면 이동모드 진입마다 부활한다.
                // 같은 오른손 스틱의 ContinuousTurn 이 부드러운 회전을 이어받는다.
                if (b is SnapTurnProvider) { b.enabled = false; continue; }
                if (!locoProviders.Contains(b)) locoProviders.Add(b);
            }
            if (locoProviders.Count > 0) locoCached = true;
        }

        // CraneViewHeightAdjuster 가 높이 조절 중이면 걷기 정지 + 왼손 스틱 양보(입력 협조만).
        bool ViewHeightActive()
        {
            if (viewHeight == null) viewHeight = FindAnyObjectByType<CraneViewHeightAdjuster>();
            return viewHeight != null && viewHeight.HeightHold;
        }

        /// <summary>리그 스케일 변경 뒤 걷기 속도 재적용(호환용 — 스케일 무관, walkSpeed 그대로).</summary>
        public void ReapplyWalkSpeed() => ApplyWalkSpeed();

        // 연속 이동 프로바이더(ContinuousMove·DynamicMove)에 걷기 속도 적용.
        void ApplyWalkSpeed()
        {
            if (walkSpeed <= 0f) return;
            // rigScale 을 곱하지 않는다 — XRI ContinuousMoveProvider 가 이미 리그 스케일을 곱한다.
            // 또 곱하면 1/24 × 1/24 = 1/576 이라 걷기가 사실상 0 이 된다. walkSpeed 그대로 넣는다.
            float effective = walkSpeed;
            int n = 0;
            foreach (var o in FindObjectsByType<ContinuousMoveProvider>(FindObjectsInactive.Exclude))
            {
                float old = o.moveSpeed;   // 설정 전 기존 속도(빠른지/느린지 판단용)
                o.moveSpeed = effective;
                n++;
                if (debugLog)
                    Debug.Log($"[Crane] 걷기 속도: '{o.name}' 기존 {old} → {effective} m/s (walkSpeed 그대로, 리그 스케일은 XRI가 반영)");
            }
            if (debugLog && n == 0)
                Debug.LogWarning("[Crane] 걷기 속도 적용 실패 — moveSpeed 가진 프로바이더 없음(리그에 ContinuousMove/DynamicMoveProvider 확인).");
        }
    }
}
