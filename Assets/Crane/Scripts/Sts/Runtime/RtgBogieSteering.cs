using UnityEngine;

namespace Container.Crane.Sts
{
    /// <summary>
    /// RTG 보기(Bogie) 스티어링 — `Bogie_LF/LB/RF/RB` 를 킹핀 축 기준으로 0° ↔ 90° 회전.
    /// RTG는 레일이 아니라 고무 타이어로 달리므로 보기를 꺾어 주행 방향 자체를 바꾼다.
    ///
    ///  • 0°(주행)  — 스택 길이방향. Blender Y = Unity 로컬 Z.
    ///  • 90°(레인) — 레인 간 이동. Blender X = Unity 로컬 X.
    ///
    /// 실측 근거: 문서/크레인_동적데이터/RTG_크레인_동적데이터.md §6 —
    /// 킹핀(`BogieKingpin_*`)의 XY 중심이 Bogie EMPTY loc과 **정확히 일치**하므로,
    /// Bogie EMPTY를 수직축으로 돌리면 킹핀 축과 맞는다(피벗 보정 불필요).
    ///
    /// 주행축 전환은 **회전이 끝난 순간에만** 한다 — 꺾는 도중에 축을 바꾸면 타이어가 진행방향을
    /// 안 보는 채로 옆으로 미끄러진다. 꺾는 동안은 <see cref="GantryMover.TravelLocked"/>로 주행을 막는다.
    /// </summary>
    [AddComponentMenu("Container/STS Crane/RTG Bogie Steering")]
    [DisallowMultipleComponent]
    [ExecuteAlways]   // 에디터 메뉴로 Play 없이 0°/90° 확인 가능하게
    public sealed class RtgBogieSteering : MonoBehaviour
    {
        /// <summary>Travel=0°(로컬 Z 주행) · Lane=90°(로컬 X 레인 이동).</summary>
        public enum Mode { Travel, Lane }

        [Tooltip("보기 4개(Bogie_LF/LB/RF/RB). 배선툴이 자동 탐색해 주입.")]
        [SerializeField] Transform[] bogies;
        [Tooltip("임포트 기준자세(0°)의 로컬 회전 — 직렬화로 도메인리로드/Play 진입에도 보존.")]
        [SerializeField] Quaternion[] restRot;

        [Tooltip("현재 지령. 인스펙터에서 바꾸면 [ExecuteAlways]로 즉시 반영.")]
        [SerializeField] Mode mode = Mode.Travel;
        [Tooltip("보기 회전 속도(도/초). 실기 RTG의 90° 전환은 수십 초 — 기본 30°/s ≈ 3초.")]
        [SerializeField] float speed = 30f;

        [Tooltip("주행축을 넘겨받을 갠트리. 90° 완료 시 로컬 X로 전환.")]
        [SerializeField] GantryMover gantry;

        [SerializeField] float angle;   // 현재 보기 각(도). 0 또는 90으로 수렴.

        const float LaneAngle = 90f;

        /// <summary>현재 지령 모드(목표). HUD/라벨 표시용.</summary>
        public Mode Current => mode;
        /// <summary>실제 보기 각(도) — 0..90. 전환 중 중간값.</summary>
        public float Angle => angle;
        /// <summary>보기가 꺾이는 중(주행 잠금 상태)인지.</summary>
        public bool IsTurning => !Mathf.Approximately(angle, TargetAngle);
        /// <summary>배선된 보기 수 — 정상은 4. 배선툴 로그 확인용.</summary>
        public int BogieCount => bogies == null ? 0 : bogies.Length;

        /// <summary>배선된 보기 트랜스폼 — 에디터 메뉴가 Undo 기록에 사용.</summary>
        public Transform[] GetBogies() => bogies ?? System.Array.Empty<Transform>();
        public bool IsConfigured => bogies != null && bogies.Length > 0 && restRot != null && restRot.Length == bogies.Length;

        float TargetAngle => mode == Mode.Lane ? LaneAngle : 0f;

        /// <summary>에디터 배선툴이 보기 참조·기준자세를 주입.</summary>
        public void Configure(Transform root, GantryMover g)
        {
            // 재배선 전 0°로 되돌린다 — 90°로 꺾어둔 채 다시 배선하면 그 자세를 기준자세로 캡처해
            //   보기가 영구히 90° 틀어진다(RtgSpreaderTelescope.RestoreRest와 같은 이유).
            RestoreRest();

            gantry = g;
            var found = new System.Collections.Generic.List<Transform>();
            foreach (var n in new[] { "Bogie_LF", "Bogie_LB", "Bogie_RF", "Bogie_RB" })
            {
                var t = FindDeep(root, n);
                if (t != null) found.Add(t);
            }
            bogies = found.ToArray();
            restRot = new Quaternion[bogies.Length];
            for (int i = 0; i < bogies.Length; i++) restRot[i] = bogies[i].localRotation;

            mode = Mode.Travel;
            angle = 0f;
            Apply();
            SyncGantry();
        }

        static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform c in root) { var r = FindDeep(c, name); if (r != null) return r; }
            return null;
        }

        public void SetMode(Mode m) => mode = m;

        /// <summary>에디터 메뉴/테스트에서 즉시 반영(회전 애니 없이 최종 상태로 스냅).</summary>
        public void SetModeNow(Mode m)
        {
            mode = m;
            angle = TargetAngle;
            Apply();
            SyncGantry();
        }

        [ContextMenu("Toggle 0° ↔ 90°")]
        public void Toggle() => mode = mode == Mode.Travel ? Mode.Lane : Mode.Travel;

        /// <summary>보기를 0°(주행) 기준자세로 즉시 복원 — 재배선/재캡처 전 호출.</summary>
        public void RestoreRest()
        {
            if (!IsConfigured) return;
            for (int i = 0; i < bogies.Length; i++)
                if (bogies[i] != null) bogies[i].localRotation = restRot[i];
            angle = 0f;
            mode = Mode.Travel;
        }

        void OnEnable() { angle = TargetAngle; Apply(); SyncGantry(); }

        bool synced;   // 현재 각도로 갠트리 축을 이미 인계했는지(도달한 프레임에만 넘기려고).

        void Update()
        {
            float target = TargetAngle;
            if (Mathf.Approximately(angle, target))
            {
                if (!synced) SyncGantry();   // 방금 도달 → 주행축 인계 + 잠금 해제
                return;
            }

            // 꺾는 중엔 주행 금지 + 축은 아직 옛 축 그대로.
            synced = false;
            if (gantry != null) gantry.TravelLocked = true;

            bool instant = speed <= 0f;
#if UNITY_EDITOR
            if (!Application.isPlaying) instant = true;   // Edit 모드: deltaTime 불안정 → 즉시 반영
#endif
            angle = instant ? target : Mathf.MoveTowards(angle, target, speed * Time.deltaTime);
            Apply();
            if (Mathf.Approximately(angle, target)) SyncGantry();
        }

        // 회전이 끝난 상태에서만 호출 — 주행축을 현재 모드에 맞추고 잠금 해제.
        void SyncGantry()
        {
            synced = true;
            if (gantry == null) return;
            gantry.SetTravelAxis(mode == Mode.Lane ? GantryMover.TravelAxis.X : GantryMover.TravelAxis.Z);
            gantry.TravelLocked = false;
        }

        void Apply()
        {
            if (!IsConfigured) return;
            for (int i = 0; i < bogies.Length; i++)
            {
                var t = bogies[i];
                if (t == null) continue;
                // 월드 수직(킹핀 축)을 부모 로컬로 변환해 그 축으로 회전 — FBX는 부모에 축변환이 걸려 있어
                //   로컬 Y/Z가 수직과 어긋난다(SpreaderLockAnimator.worldVertical과 같은 이유·같은 방식).
                //   순서 중요: AngleAxis를 왼쪽에 둬야(프리곱) rest가 identity가 아닐 때도 축이 안 틀어진다.
                Vector3 lu = t.parent != null ? t.parent.InverseTransformDirection(Vector3.up) : Vector3.up;
                if (lu.sqrMagnitude < 1e-8f) lu = Vector3.up; else lu.Normalize();
                t.localRotation = Quaternion.AngleAxis(angle, lu) * restRot[i];
            }
        }
    }
}
