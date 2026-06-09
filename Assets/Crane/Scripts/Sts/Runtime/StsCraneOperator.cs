using System.Collections;
using UnityEngine;

namespace Container.Crane.Sts
{
    /// <summary>
    /// StsCrane을 자동 작업 사이클로 구동하는 데모 드라이버.
    /// 실제 PLC/입력 대신, Play 시 바다↔육지 컨테이너 이송 동작(트롤리 왕복 + 스프레더 승강)을 반복 재생한다.
    /// IAxisMover만 호출하므로 부속 내부 구현은 모른다(Facade 사용).
    /// </summary>
    [AddComponentMenu("Container/STS Crane/STS Crane Operator (Auto Cycle)")]
    [RequireComponent(typeof(StsCrane))]
    [DisallowMultipleComponent]
    public sealed class StsCraneOperator : MonoBehaviour
    {
        [Header("재생")]
        [Tooltip("Play 시 자동으로 작업 사이클 반복")]
        [SerializeField] bool autoRun = true;

        [Header("속도 (정규화 범위/초, 1=전체 범위를 1초에)")]
        [SerializeField] float trolleySpeed = 0.35f;
        [SerializeField] float hoistSpeed = 0.6f;

        [Header("정지 시간 (초) — 집기/놓기 시 머무름")]
        [SerializeField] float grabDwell = 0.6f;

        StsCrane crane;
        Coroutine loop;

        void OnEnable()
        {
            if (!Application.isPlaying || !autoRun) return;   // 에디터 생성 시엔 구동 안 함, Play에서만
            if (crane == null) crane = GetComponent<StsCrane>();
            loop = StartCoroutine(Cycle());
        }

        void OnDisable()
        {
            if (loop != null) StopCoroutine(loop);
            loop = null;
        }

        IEnumerator Cycle()
        {
            if (crane == null) yield break;
            IAxisMover trolley = crane.Trolley;
            IAxisMover hoist = crane.Spreader;
            if (trolley == null || hoist == null) yield break;

            // 시작 자세: 스프레더 올리고 바다쪽으로
            yield return MoveAxis(hoist, 1f, hoistSpeed);
            yield return MoveAxis(trolley, 1f, trolleySpeed);

            float side = 1f;   // 1=바다(outreach), 0=육지(backreach)
            while (true)
            {
                // 현재 쪽에서 내림 → 집기/놓기 머무름 → 올림
                yield return MoveAxis(hoist, 0f, hoistSpeed);
                yield return new WaitForSeconds(grabDwell);
                yield return MoveAxis(hoist, 1f, hoistSpeed);
                // 반대쪽으로 트롤리 이송
                side = 1f - side;
                yield return MoveAxis(trolley, side, trolleySpeed);
            }
        }

        /// <summary>현재 정규화 위치에서 target(0~1)까지 일정 속도로 이동.</summary>
        static IEnumerator MoveAxis(IAxisMover m, float target, float speed)
        {
            target = Mathf.Clamp01(target);                          // 정규화 범위 밖 값 방지
            float cur = Mathf.InverseLerp(m.Min, m.Max, m.Current);
            float spd = Mathf.Max(0.01f, speed);
            // MoveTowards가 target에 정확히 안착하지만, 부동소수 '!=' 대신 허용오차로 종료를 견고하게.
            while (!Mathf.Approximately(cur, target))
            {
                cur = Mathf.MoveTowards(cur, target, spd * Time.deltaTime);
                m.MoveToNormalized(cur);
                yield return null;
            }
        }
    }
}
