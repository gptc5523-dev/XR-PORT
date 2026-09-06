using UnityEngine;

namespace Container.Crane.Sts
{
    /// <summary>
    /// RTG(FBX) 스프레더 텔레스코픽 — 20 / 40 / 45ft. STS <see cref="SpreaderTelescope"/>와 달리
    /// **빔을 스케일(늘이기)하지 않고 슬라이드(밀기)** 한다. FBX 계층이 TeleBeam_F/B → EndBeam →
    /// 트위스트락·플리퍼로 물려 있어, TeleBeam만 신축축으로 밀면 끝빔·트위스트락이 **왜곡 없이** 딸려온다.
    ///
    /// 원칙(요청 스펙):
    ///  • 신축축은 두 빔의 위치차(restF−restB)에서 **자동 검출** — Blender(Z-up)→Unity(Y-up) 임포트로
    ///    축이 Y→Z 등으로 바뀌어도 안전(Vector3.up/right 하드코딩 금지).
    ///  • 후퇴/신장량은 미터 하드코딩 대신 **측정된 두 빔 간격 × ISO 규격 비율**로 산출 → FBX 임포트
    ///    단위(m/cm)·루트 스케일에 무관하게 정확.
    ///  • MainFrame(센터 슬리브)·EndBeam·트위스트락·플리퍼는 **직접 이동 금지**(TeleBeam만 움직임).
    /// </summary>
    [AddComponentMenu("Container/STS Crane/RTG Spreader Telescope")]
    [DisallowMultipleComponent]
    [ExecuteAlways]   // 에디터에서도 Size 토글 시 신축이 보이도록(Play 없이 확인 가능)
    public sealed class RtgSpreaderTelescope : MonoBehaviour
    {
        public enum Size { Ft20, Ft40, Ft45 }

        [Tooltip("좌/우 텔레스코핑 빔 — 끝빔·트위스트락·플리퍼를 자식으로 물고 있는 노드(Spreader_TeleBeam_F/B).")]
        [SerializeField] Transform teleBeamF, teleBeamB;
        [Tooltip("40ft(임포트) 기준자세의 두 빔 로컬위치 — 직렬화로 도메인리로드/Play 진입에도 보존.")]
        [SerializeField] Vector3 restF, restB;

        [Tooltip("끝빔(Spreader_EndBeam_F/B). TeleBeam의 자식이면 자동 추종하므로 건드리지 않고, " +
                 "임포트로 계층이 형제로 풀려 있으면 명시적으로 같이 슬라이드(EndBeam_B 안 움직임 방지).")]
        [SerializeField] Transform endBeamF, endBeamB;
        [SerializeField] Vector3 restEndF, restEndB;
        [SerializeField] bool endFIsChild, endBIsChild;   // TeleBeam 자식 여부(자식이면 명시이동 안 함)
        [Tooltip("현재 규격. 인스펙터에서 직접 바꾸면 [ExecuteAlways]로 즉시 반영.")]
        [SerializeField] Size size = Size.Ft40;
        [Tooltip("슬라이드 속도(로컬 유닛/s). 0이면 즉시 전환.")]
        [SerializeField] float speed = 0f;

        // ISO 1496-1 코너캐스팅 종방향 중심 간격(m) → 반길이. 20 / 40 / 45ft.
        const float Half20 = 5.853f  * 0.5f;   // 2.9265
        const float Half40 = 11.985f * 0.5f;   // 5.9925 (기준자세)
        const float Half45 = 13.716f * 0.5f;   // 6.858
        // Blender 40ft 기준자세에서 두 TeleBeam의 축방향 간격(m) — 실측 loc.y ±2.893 → 5.786.
        //   (측정 간격[Unity]) / 5.786 = 임포트 스케일 k. 빔 변위를 이 k로 환산해 단위 무관하게.
        //   ※ 옛 값 5.80은 반올림 추정이라 k에 0.24% 오차가 났다 — 문서/크레인_동적데이터/RTG_크레인_동적데이터.md §7.
        const float SpanBlender40 = 5.786f;

        float cur;   // 현재 F빔의 축방향 변위(Unity 로컬단위). 40ft=0, +=신장 / −=후퇴.

        public Size Current => size;
        public bool IsConfigured => (restF - restB).sqrMagnitude > 1e-9f;

        static float HalfOf(Size s) => s == Size.Ft20 ? Half20 : s == Size.Ft45 ? Half45 : Half40;

        /// <summary>에디터 배선툴이 빔 참조·기준자세(현재=40ft 임포트 포즈)를 주입.</summary>
        public void Configure(Transform beamF, Transform beamB, Size start = Size.Ft40)
        {
            teleBeamF = beamF; teleBeamB = beamB;
            restF = beamF.localPosition; restB = beamB.localPosition;   // 임포트 포즈 = 40ft 기준

            // 끝빔 캡처 — TeleBeam 자식이면 자동 추종, 형제로 풀려 있으면 명시 이동 대상.
            endBeamF = FindDeep(transform, "Spreader_EndBeam_F");
            endBeamB = FindDeep(transform, "Spreader_EndBeam_B");
            if (endBeamF != null) { restEndF = endBeamF.localPosition; endFIsChild = endBeamF.IsChildOf(beamF); }
            if (endBeamB != null) { restEndB = endBeamB.localPosition; endBIsChild = endBeamB.IsChildOf(beamB); }

            size = start;
            cur = DeltaFor(start);
            Apply();
#if UNITY_EDITOR
            Debug.Log($"[RTG-Tele] 끝빔 계층 — EndBeam_F={Desc(endBeamF, endFIsChild)} · EndBeam_B={Desc(endBeamB, endBIsChild)}");
#endif
        }

#if UNITY_EDITOR
        static string Desc(Transform t, bool isChild) =>
            t == null ? "없음(경고!)" : isChild ? "TeleBeam자식→자동추종" : "형제→명시슬라이드";
#endif

        static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform c in root) { var r = FindDeep(c, name); if (r != null) return r; }
            return null;
        }

        public void SetSize(Size s) => size = s;

        /// <summary>에디터 메뉴/테스트에서 즉시 반영(Update 틱 안 기다림). Size 설정 + 빔 슬라이드 적용.</summary>
        public void SetSizeNow(Size s) { size = s; cur = DeltaFor(s); Apply(); }

        [ContextMenu("Cycle 20 → 40 → 45ft")]
        public void Cycle() => size = size == Size.Ft20 ? Size.Ft40
                                    : size == Size.Ft40 ? Size.Ft45 : Size.Ft20;

        /// <summary>빔을 40ft 기준자세로 즉시 복원(재배선/재캡처 전 호출 — 이동된 자세를 잘못 캡처 방지).</summary>
        public void RestoreRest()
        {
            if (!IsConfigured) return;
            if (teleBeamF != null) teleBeamF.localPosition = restF;
            if (teleBeamB != null) teleBeamB.localPosition = restB;
            if (endBeamF != null && !endFIsChild) endBeamF.localPosition = restEndF;
            if (endBeamB != null && !endBIsChild) endBeamB.localPosition = restEndB;
            cur = 0f; size = Size.Ft40;
        }

        // 신축축 단위벡터 — 두 빔 위치차에서 자동 검출(축 하드코딩 금지). F가 +방향.
        Vector3 AxisDir()
        {
            Vector3 d = restF - restB;
            return d.sqrMagnitude > 1e-9f ? d.normalized : Vector3.up;
        }

        // 규격별 F빔 변위(Unity 로컬단위). k = (측정 두 빔 간격)/5.80(m)로 임포트 스케일 자동반영.
        //   빔과 트위스트락은 강체(부모-자식)로 함께 이동하므로, 코너 반길이 차 = 빔 변위.
        float DeltaFor(Size s)
        {
            float span = (restF - restB).magnitude;
            float k = span > 1e-6f ? span / SpanBlender40 : 1f;
            return k * (HalfOf(s) - Half40);   // 20ft<0(후퇴) · 40ft=0 · 45ft>0(신장)
        }

        void OnEnable() { cur = DeltaFor(size); Apply(); }

        void Update()
        {
            float target = DeltaFor(size);   // 인스펙터/코드에서 size를 바꿔도 추종
            if (Mathf.Approximately(cur, target)) return;

            bool instant = speed <= 0f;
#if UNITY_EDITOR
            if (!Application.isPlaying) instant = true;   // Edit 모드: deltaTime 불안정 → 즉시 반영
#endif
            cur = instant ? target : Mathf.MoveTowards(cur, target, speed * Time.deltaTime);
            Apply();
        }

        void Apply()
        {
            if (!IsConfigured) return;
            Vector3 dir = AxisDir();
            if (teleBeamF != null) teleBeamF.localPosition = restF + dir * cur;
            if (teleBeamB != null) teleBeamB.localPosition = restB - dir * cur;
            // 끝빔이 TeleBeam 자식이 아니면(임포트로 형제로 풀림) 같은 프레임에서 명시적으로 함께 슬라이드.
            if (endBeamF != null && !endFIsChild) endBeamF.localPosition = restEndF + dir * cur;
            if (endBeamB != null && !endBIsChild) endBeamB.localPosition = restEndB - dir * cur;
        }
    }
}
