using System.Collections;
using UnityEngine;

namespace Container.Crane.Sts
{
    /// <summary>
    /// 자동 시나리오 공통 베이스 — VR 직접조종 양보/복귀, 트위스트락 시간 설정, 시작 지연,
    /// 그리고 ★ 모든 시나리오가 공유하는 '알람 자동정지(AutoStop) 게이트'를 한곳에 둔다.
    ///
    /// [설계 의도 — 문제 해결]
    ///   "경고가 떠도 정지하지 않고 무시함"은, 시나리오가 축을 직접 구동만 하고 CraneFault.Evaluate()를
    ///   한 번도 보지 않으면 생긴다. → CheckAutoStop()으로 코드북 AutoStop 알람(과부하·충돌·Snag·끝단)이
    ///   활성화되면 즉시 autoStopped=true로 모든 축 구동을 멈추고 사이클을 종료한다(알람에 복종, 무시 금지).
    ///
    /// ※ QA 자동 검증(CraneQaScenario)은 FindObjectsByType&lt;CraneScenarioBase&gt;로 자동 시나리오를 모두
    ///    꺼서 단독으로 돈다 — 모든 자동 시나리오는 이 베이스를 상속해야 그 대상이 된다.
    /// </summary>
    public abstract class CraneScenarioBase : MonoBehaviour
    {
        [Header("실행")]
        [SerializeField] protected bool runOnStart = true;
        [SerializeField] protected float startDelay = 0.5f;
        [Tooltip("트위스트락 잠금/해제 1회 소요(초) — 현실 ~1.5~2s.")]
        [SerializeField] protected float lockSeconds = 1.5f;

        protected StsCrane crane;
        protected SpreaderGrabber grabber;
        protected StsCraneVRController vr;
        protected SpreaderLockAnimator lockAnim;   // 트위스트락 잠금/해제 애니메이션 완료 대기용
        protected SpreaderTelescope telescope;     // 20/40 사이즈 변경 완료 대기용

        /// <summary>시나리오 구동 중 — VR 직접조종을 강제 OFF(동시구동 차단)하는 동안 true.</summary>
        protected bool running;
        /// <summary>★ 알람 자동정지가 한 번이라도 발동했는가 — true면 모든 축 구동이 즉시 멈춘다(래치).</summary>
        protected bool autoStopped;

        /// <summary>콘솔 로그 접두("양하" 등).</summary>
        protected abstract string Tag { get; }
        /// <summary>실제 시나리오 본문 — 하위 클래스가 구현. autoStopped를 주기적으로 확인해 협조한다.</summary>
        protected abstract IEnumerator RunScenario();

        protected virtual void Awake()
        {
            crane = GetComponent<StsCrane>();
            grabber = GetComponent<SpreaderGrabber>();
            vr = GetComponent<StsCraneVRController>();
            lockAnim = GetComponentInChildren<SpreaderLockAnimator>(true);
            telescope = GetComponentInChildren<SpreaderTelescope>(true);
        }

        // 시나리오 제거/Play 종료 시 VR 직접조종 복귀(꺼둔 채 남기지 않게).
        // enabled=false로는 IEnumerator Start가 안 멈추는 알려진 이슈 → 먼저 코루틴 정지 후 VR 복귀.
        protected virtual void OnDisable()
        {
            StopAllCoroutines();
            if (vr != null) vr.enabled = true;
        }

        // 시나리오 동안 VR 강제 OFF(재활성·동시구동 차단)
        protected virtual void LateUpdate()
        {
            if (running && vr != null && vr.enabled) vr.enabled = false;
        }

        IEnumerator Start()
        {
            if (!runOnStart) yield break;
            running = true;
            autoStopped = false;
            if (vr != null) vr.enabled = false;
            if (lockAnim != null) lockAnim.SetLockSeconds(lockSeconds);   // 트위스트락 현실 시간(~1.5s)으로

            yield return null;
            yield return new WaitForSeconds(startDelay);

            yield return RunScenario();

            running = false;
            if (vr != null) vr.enabled = true;   // 끝 → VR 직접조종 복귀
        }

        /// <summary>
        /// ★ 알람 자동정지 게이트 — 코드북 AutoStop 알람이 활성이면 래치(autoStopped=true)하고 true 반환.
        /// 한 번 발동하면 이후 모든 축 구동이 즉시 빠져나간다. CraneFault.Evaluate는 우리 데이터
        /// (과부하 SWL·축 충돌·Snag·끝단)로 자연 판정 + 주입 결함을 모두 본다.
        /// </summary>
        protected bool CheckAutoStop()
        {
            if (autoStopped) return true;
            var f = CraneFault.Evaluate(crane);
            // ★ 끝단(soft travel limit)은 자동 사이클에선 자동정지에서 제외한다 — 자동 운전은 픽업/적치 때
            //   의도적으로 축을 끝까지 보낸다(바닥 적치=권상 하한). 끝단은 '결함'이 아니라 정상 경계 신호다.
            //   (실제 결함: 과부하 3012·축 충돌 1012/2012·Snag 3013·E-stop·가속한계 등은 그대로 정지.)
            if (f.IsValid && f.AutoStop && !IsSoftTravelLimit(f.Code))
            {
                autoStopped = true;
                Debug.LogWarning($"[{Tag}] 🛑 자동정지 — 알람 {CraneFault.Format(f)} (심각도 {CraneFault.SevLabel(f.Sev)}). " +
                                 "축 구동 중단 · 자동 사이클 종료(알람 무시 금지). 운전자 확인/리셋 필요.");
                return true;
            }
            return false;
        }

        // 끝단 위치 알람(권상 상/하한·트롤리/갠트리 끝단) — 자동 사이클에선 자동정지 비대상.
        static bool IsSoftTravelLimit(int code) =>
            code == CraneFault.CodeHoistUpper  || code == CraneFault.CodeHoistLower  ||
            code == CraneFault.CodeTrolleyLand || code == CraneFault.CodeTrolleySea  ||
            code == CraneFault.CodeGantryFwd   || code == CraneFault.CodeGantryRev;
    }
}
