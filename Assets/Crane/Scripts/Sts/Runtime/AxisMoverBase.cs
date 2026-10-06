using UnityEngine;

namespace AIXRCrane.Crane.Sts
{
    /// <summary>단일 축 무버 공통 베이스 — Gantry(Z)·Trolley(X)·SpreaderHoist(Y)가 공유. 클램프·이동·정규화·기즈모 골격을 제공.
    /// min/max 직렬화 필드는 파생 클래스에 남긴다(각자 기본값·직렬화 보존).</summary>
    public abstract class AxisMoverBase : MonoBehaviour, IAxisMover
    {
        public abstract float Min { get; }
        public abstract float Max { get; }
        public float Current => ReadAxis();

        // 축은 부모 로컬에 쓴다 — 부모의 회전·스케일을 그대로 탄다. 부모 없으면(갠트리 루트) 로컬 = 월드.
        public virtual Vector3 WorldAxis => transform.parent != null ? transform.parent.TransformVector(LocalAxis) : LocalAxis;
        public float WorldPerUnit => WorldAxis.magnitude;
        /// <summary>이 무버가 읽고 쓰는 로컬 축(단위벡터).</summary>
        protected abstract Vector3 LocalAxis { get; }

        /// <summary>이동 가능한 하한 — 기본은 Min. SpreaderHoist가 floorOffset 반영 위해 override.</summary>
        protected virtual float LowerLimit => Min;

        /// <summary>마지막 이동 시도에서 장애물에 막혔는지(충돌 정지, 사실상 GantryMover용).</summary>
        public bool IsBlocked { get; private set; }

        /// <summary>PLC 구동 중(PlcBridge 가 켬): 3D 막힘·바닥 한계는 판정만 하고 이동은 막지 않는다 — 화면은 PLC 위치를 그대로 따른다.</summary>
        public bool FollowOnly { get; set; }

        /// <summary>현재 상한 끝단 도달(가동 범위의 1% 이내).</summary>
        public bool AtUpperLimit => Max > LowerLimit && (Max - Current) <= (Max - LowerLimit) * LimitFrac;
        /// <summary>현재 하한 끝단 도달(가동 범위의 1% 이내).</summary>
        public bool AtLowerLimit => Max > LowerLimit && (Current - LowerLimit) <= (Max - LowerLimit) * LimitFrac;
        const float LimitFrac = 0.01f;

        public void MoveTo(float value)
        {
            float clamped = Mathf.Clamp(value, FollowOnly ? Min : LowerLimit, Max);
            IsBlocked = IsBlockedToward(clamped);
            if (IsBlocked && !FollowOnly) return;   // 진행 방향에 장애물(컨테이너 등) — 이동 정지(밀지 않음)
            WriteAxis(clamped);
            OnMoved(clamped);
        }

        /// <summary>진행 방향(target) 길에 장애물이 있으면 true → 이동 정지. 기본은 막힘 없음(GantryMover만 override).</summary>
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

        /// <summary>Min/Max 축값 → 기즈모용 월드 점(기본: 부모 로컬 축 해석).
        /// ★ 월드 절대값으로 구동하는 축(SpreaderHoist.worldVertical)은 반드시 override.</summary>
        protected virtual Vector3 GizmoPointAt(float axisValue)
        {
            Vector3 l = transform.localPosition;
            l[GizmoAxis] = axisValue;
            // localPosition이 사는 공간 = 부모 공간 — 부모 없으면 그게 곧 월드다(parent=transform 폴백 금지).
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
