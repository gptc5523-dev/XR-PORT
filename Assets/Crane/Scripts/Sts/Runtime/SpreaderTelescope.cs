using UnityEngine;

namespace Container.Crane.Sts
{
    /// <summary>
    /// 스프레더 텔레스코픽 — 좌/우 암(끝빔 + 트위스트락)을 20ft↔40ft 위치로 슬라이드하고,
    /// 중앙 고정부(20ft) 끝과 암 사이의 텔레스코핑 빔을 함께 신축시킨다(실제 텔레스코픽 스프레더).
    /// 20ft면 빔이 수축(거의 사라짐), 40ft면 신장한다. Set40()/Toggle()로 전환, speed&gt;0이면 부드럽게.
    /// </summary>
    [AddComponentMenu("Container/STS Crane/Spreader Telescope")]
    [DisallowMultipleComponent]
    [ExecuteAlways]   // 에디터에서도 토글 시 신축이 보이도록(Play 없이 확인 가능)
    public sealed class SpreaderTelescope : MonoBehaviour
    {
        [Tooltip("왼쪽(-X)/오른쪽(+X) 텔레스코픽 암 (끝빔 + 트위스트락)")]
        [SerializeField] Transform armL, armR;
        [Tooltip("좌/우 텔레스코핑 빔 — X 스케일이 신축됨")]
        [SerializeField] Transform teleL, teleR;

        [Header("반길이 (로컬 X, m)")]
        [Tooltip("중앙 고정부 끝(=20ft 반길이) — 텔레스코핑 빔의 안쪽 고정단")]
        [SerializeField] float centerHalf = 0.126f;
        [SerializeField] float half20 = 0.126f;
        [SerializeField] float half40 = 0.254f;

        [Header("동작")]
        [Tooltip("슬라이드 속도(m/s). 0이면 즉시 전환")]
        [SerializeField] float speed = 0.25f;
        [Tooltip("현재 40ft 상태(끄면 20ft) — 인스펙터에서 직접 토글 가능")]
        [SerializeField] bool is40 = true;

        const float Overlap = 0.02f;   // 빔 양끝이 중앙 빔·끝빔과 살짝 겹쳐 단절 방지
        float current;

        public bool Is40 => is40;

        /// <summary>현재 반길이(로컬 X, m) — 신축 중 보간값. SpreaderPusher 박스 동적 크기 산정용.</summary>
        public float CurrentHalf => current;

        /// <summary>Builder가 암·텔레빔 참조, 반길이, 시작 사이즈를 한 번에 주입.</summary>
        public void Configure(Transform armLeft, Transform armRight,
                              Transform teleLeft, Transform teleRight,
                              float centerHalfLen, float h20, float h40, bool start40)
        {
            armL = armLeft; armR = armRight;
            teleL = teleLeft; teleR = teleRight;
            centerHalf = centerHalfLen;
            half20 = h20; half40 = h40;
            is40 = start40;
            current = start40 ? half40 : half20;
            Apply();
        }

        /// <summary>40ft(true)/20ft(false)로 전환.</summary>
        public void Set40(bool value) => is40 = value;

        /// <summary>20ft↔40ft 토글.</summary>
        public void Toggle() => is40 = !is40;

        void OnEnable()
        {
            current = is40 ? half40 : half20;
            Apply();
        }

        void Update()
        {
            float target = is40 ? half40 : half20;   // 인스펙터/코드에서 is40을 바꿔도 따라감
            if (Mathf.Approximately(current, target)) return;

            bool instant = speed <= 0f;
#if UNITY_EDITOR
            if (!Application.isPlaying) instant = true;   // Edit 모드: deltaTime 불안정 → 즉시 반영
#endif
            current = instant ? target : Mathf.MoveTowards(current, target, speed * Time.deltaTime);
            Apply();
        }

        void Apply()
        {
            // 암: 끝빔 + 트위스트락을 current 위치로 슬라이드
            if (armL != null) { var p = armL.localPosition; p.x = -current; armL.localPosition = p; }
            if (armR != null) { var p = armR.localPosition; p.x =  current; armR.localPosition = p; }

            // 텔레스코핑 빔: 중앙 끝(centerHalf)↔암(current)의 신장분만큼 X 스케일 신축.
            // 신장분이 0(=20ft)이면 빔을 숨겨 완전히 수축한 것처럼 보이게 한다.
            float ext = current - centerHalf;     // 신장량(20ft≈0, 40ft≈0.128)
            bool show = ext > 0.002f;
            float len = ext + Overlap;
            float mid = (centerHalf + current) * 0.5f;
            SetBeam(teleL, -mid, len, show);
            SetBeam(teleR,  mid, len, show);
        }

        // 신장분 0이면 숨기고, 아니면 베벨 큐브의 X 스케일·X 위치만 바꿔 길이 조절(Y·Z는 생성값 유지)
        static void SetBeam(Transform t, float posX, float lenX, bool show)
        {
            if (t == null) return;
            if (t.gameObject.activeSelf != show) t.gameObject.SetActive(show);
            if (!show) return;
            var p = t.localPosition; p.x = posX; t.localPosition = p;
            var s = t.localScale;    s.x = lenX; t.localScale = s;
        }

        [ContextMenu("Toggle 20ft / 40ft")]
        void ContextToggle() => Toggle();
    }
}
