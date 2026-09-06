using UnityEngine;

namespace Container.Crane.Sts
{
    /// <summary>
    /// 단일 축 무버 공통 베이스 — Gantry(Z)·Trolley(X)·SpreaderHoist(Y)가 공유.
    /// 클램프·이동·정규화·기즈모 골격을 한곳에 모으고, 파생 클래스는 '어느 축인지'(읽기/쓰기)와
    /// 표시 색만 제공한다. min/max 직렬화 필드는 의도적으로 파생 클래스에 남겨 둔다
    /// (각자의 기본값·Header·기존 인스펙터/프리팹 값 보존 → 직렬화 변화 없음).
    /// </summary>
    public abstract class AxisMoverBase : MonoBehaviour, IAxisMover
    {
        public abstract float Min { get; }
        public abstract float Max { get; }
        public float Current => ReadAxis();

        /// <summary>이동 가능한 하한 — 기본은 Min. SpreaderHoist가 floorOffset 반영 위해 override.</summary>
        protected virtual float LowerLimit => Min;

        /// <summary>마지막 이동 시도에서 장애물에 막혔는지(충돌 정지). 충돌 정지는 GantryMover만 override하므로 사실상 갠트리용.</summary>
        public bool IsBlocked { get; private set; }

        /// <summary>현재 상한 끝단 도달(가동 범위의 1% 이내).</summary>
        public bool AtUpperLimit => Max > LowerLimit && (Max - Current) <= (Max - LowerLimit) * LimitFrac;
        /// <summary>현재 하한 끝단 도달(가동 범위의 1% 이내).</summary>
        public bool AtLowerLimit => Max > LowerLimit && (Current - LowerLimit) <= (Max - LowerLimit) * LimitFrac;
        const float LimitFrac = 0.01f;

        public void MoveTo(float value)
        {
            float clamped = Mathf.Clamp(value, LowerLimit, Max);
            IsBlocked = IsBlockedToward(clamped);
            if (IsBlocked) return;   // 진행 방향에 장애물(컨테이너 등) — 이동 정지(밀지 않음)
            WriteAxis(clamped);
            OnMoved(clamped);
        }

        /// <summary>진행 방향(target)으로 가는 길에 장애물이 있으면 true → 그 방향 이동을 멈춘다.
        /// 기본은 막힘 없음 — 충돌 정지가 필요한 축(GantryMover)만 override.</summary>
        protected virtual bool IsBlockedToward(float target) => false;

        public void MoveToNormalized(float t01) => MoveTo(Mathf.Lerp(Min, Max, Mathf.Clamp01(t01)));

        /// <summary>자신의 로컬 축(x/y/z) 현재값을 읽는다.</summary>
        protected abstract float ReadAxis();
        /// <summary>자신의 로컬 축(x/y/z)에 클램프된 값을 쓴다.</summary>
        protected abstract void WriteAxis(float clamped);
        /// <summary>이동 직후 훅 — TrolleyMover가 스프레더 X 동기화에 사용. 기본은 아무것도 안 함.</summary>
        protected virtual void OnMoved(float clamped) { }

#if UNITY_EDITOR
        /// <summary>기즈모를 그릴 로컬 축 인덱스(0=x, 1=y, 2=z).</summary>
        protected abstract int GizmoAxis { get; }
        protected abstract Color GizmoColor { get; }

        /// <summary>Min/Max 축값을 기즈모용 월드 점으로 변환. 기본은 '부모 로컬 축' 해석 —
        /// 월드 절대값으로 구동하는 축(SpreaderHoist.worldVertical)은 반드시 override할 것.</summary>
        protected virtual Vector3 GizmoPointAt(float axisValue)
        {
            Vector3 l = transform.localPosition;
            l[GizmoAxis] = axisValue;
            // localPosition이 사는 공간 = 부모 공간. 부모가 없으면 그게 곧 월드다.
            // (옛 폴백 parent=transform은 자기 TRS를 자기 localPosition에 다시 곱해
            //  루트에 붙는 GantryMover의 범위선을 스케일 배만큼 날려버렸다.)
            Transform parent = transform.parent;
            return parent != null ? parent.TransformPoint(l) : l;
        }

        void OnDrawGizmosSelected()
        {
            Vector3 a = GizmoPointAt(Min);
            Vector3 b = GizmoPointAt(Max);
            Gizmos.color = GizmoColor;
            Gizmos.DrawLine(a, b);
            Gizmos.DrawWireSphere(a, 0.04f);
            Gizmos.DrawWireSphere(b, 0.04f);
        }
#endif
    }
}
