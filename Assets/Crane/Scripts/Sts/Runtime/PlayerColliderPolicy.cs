using UnityEngine;

namespace AIXRCrane.Crane.Sts
{
    /// <summary>로컬 XR 리그가 야드 컨테이너·크레인을 물리로 밀지 않게 한다 — 손/컨트롤러(트리거)만 남기고
    /// 몸의 솔리드 콜라이더(캡슐·CharacterController)는 끈다. 트리거는 안 건드림(끄면 손 집기/포크 UI 깨짐), 네트워크 동기화 불필요.</summary>
    [AddComponentMenu("AI-XR Crane/STS Crane/Player Collider Policy")]
    [DisallowMultipleComponent]
    public sealed class PlayerColliderPolicy : MonoBehaviour
    {
        [Tooltip("끄면 원복 — 모든 리그 콜라이더를 그대로 둔다(플레이어가 다시 컨테이너를 밀게 됨).")]
        [SerializeField] bool passThroughBody = true;
        [Tooltip("재확인 주기(초). 리그/콜라이더가 늦게 뜨거나 XRI가 다시 켜도 다음 스캔에서 재차단.")]
        [SerializeField] float scanInterval = 0.5f;
        [SerializeField] bool debugLog = true;

        Transform rig;
        int noRigFrames;
        float nextScan;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoSpawn() => CraneHud.EnsureSpawned<PlayerColliderPolicy>("PlayerColliderPolicy");

        void Update()
        {
            if (!passThroughBody) return;
            if (Time.unscaledTime < nextScan) return;
            nextScan = Time.unscaledTime + Mathf.Max(0.1f, scanInterval);

            if (rig == null) rig = FindRig();
            if (rig == null)
            {
                if (debugLog && (noRigFrames++ % 10) == 0)
                    Debug.LogWarning("[PlayerCollider] XR 리그를 못 찾음 — 재시도 중(씬에 XR Origin 확인).");
                return;
            }
            noRigFrames = 0;

            int disabled = 0;
            foreach (var c in rig.GetComponentsInChildren<Collider>(true))
            {
                // 트리거(손/컨트롤러 XR 인터랙터)는 유지 — 상호작용용. 솔리드(몸·캡슐·CharacterController)만 끈다.
                //   CharacterController도 Collider 하위라 여기에 잡힘(isTrigger=false → 비활성 대상).
                if (c == null || c.isTrigger || !c.enabled) continue;
                c.enabled = false;
                disabled++;
            }
            if (debugLog && disabled > 0)
                Debug.Log($"[PlayerCollider] 리그 솔리드 콜라이더 {disabled}개 비활성 — 몸은 통과, 손/컨트롤러(트리거)만 인식.");
        }

        // 로컬 리그 탐색 — 카메라 루트 우선, 없으면 XROrigin(태그/계층 무관). CranePlayerRigScale와 동일 사상.
        static Transform FindRig()
        {
            var cam = Camera.main;
            if (cam != null) return cam.transform.root;
            var t = System.Type.GetType("Unity.XR.CoreUtils.XROrigin, Unity.XR.CoreUtils");
            if (t != null && FindAnyObjectByType(t) is Component xr) return xr.transform.root;
            return null;
        }
    }
}
