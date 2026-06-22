using System.Collections.Generic;
using UnityEngine;

namespace Container.Crane.Sts
{
    /// <summary>
    /// 폭풍 계류 결박 봉(Tiedown_Rod)을 갠트리 주행에 연동한다.
    ///   - 갠트리가 움직이면 봉을 살짝 들어올려 부두 앵커에서 분리(결박 해제)
    ///   - 멈추면 다시 내려와 박힘(결박)
    /// 봉만 움직이고 러그(크레인쪽)·앵커(부두쪽)는 고정. 동작은 SmoothDamp로 부드럽게.
    /// 참조를 비워두면 Start에서 같은 오브젝트의 StsCrane + 자식 중 "Tiedown_Rod*"를 자동 수집한다.
    /// </summary>
    [AddComponentMenu("Container/STS Crane/Tiedown Controller")]
    public sealed class TiedownController : MonoBehaviour
    {
        [Tooltip("결박 봉들(Tiedown_Rod). 비우면 Start에서 자식 중 이름으로 자동 수집.")]
        [SerializeField] Transform[] rods;
        [Tooltip("갠트리 위치를 읽을 크레인. 비우면 같은 오브젝트의 StsCrane.")]
        [SerializeField] StsCrane crane;

        [Header("동작")]
        [Tooltip("주행 시 봉이 위로 들리는 높이(로컬 단위). (a) 앵커에서 살짝 떠 분리되는 정도.")]
        [SerializeField] float liftHeight = 0.012f;
        [Tooltip("주행 판정 속도(갠트리 Current 변화/초). 이보다 빠르면 '이동 중'으로 본다.")]
        [SerializeField] float moveThreshold = 0.0008f;
        [Tooltip("올림/내림 지연 시간(클수록 느긋하게 추종). SmoothDamp.")]
        [SerializeField] float smoothTime = 0.22f;

        float[] baseY, curY, vel;
        float prevGantry;
        bool ready;

        void Start()
        {
            if (crane == null) crane = GetComponent<StsCrane>();

            if (rods == null || rods.Length == 0)
            {
                var found = new List<Transform>();
                foreach (var t in GetComponentsInChildren<Transform>(true))
                    if (t.name.StartsWith(StsPartNames.TiedownRodPrefix)) found.Add(t);
                rods = found.ToArray();
            }

            int n = rods.Length;
            if (n == 0) { enabled = false; return; }   // 결박 봉이 없으면 비활성

            baseY = new float[n]; curY = new float[n]; vel = new float[n];
            for (int i = 0; i < n; i++)
                if (rods[i] != null) { baseY[i] = rods[i].localPosition.y; curY[i] = baseY[i]; }

            if (crane != null && crane.Gantry != null) prevGantry = crane.Gantry.Current;
            ready = true;
        }

        void LateUpdate()
        {
            if (!ready) return;

            bool moving = false;
            var g = crane != null ? crane.Gantry : null;
            if (g != null)
            {
                float now = g.Current;
                float speed = Mathf.Abs(now - prevGantry) / Mathf.Max(Time.deltaTime, 1e-4f);
                prevGantry = now;
                moving = speed > moveThreshold;
            }

            float lift = moving ? liftHeight : 0f;
            for (int i = 0; i < rods.Length; i++)
            {
                if (rods[i] == null) continue;
                float target = baseY[i] + lift;
                curY[i] = Mathf.SmoothDamp(curY[i], target, ref vel[i], smoothTime);
                var lp = rods[i].localPosition;
                lp.y = curY[i];
                rods[i].localPosition = lp;
            }
        }
    }
}
