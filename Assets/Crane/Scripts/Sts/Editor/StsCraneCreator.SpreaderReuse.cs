#if UNITY_EDITOR
using UnityEngine;

namespace AIXRCrane.Crane.Sts.EditorTools
{
    /// <summary>STS 스프레더·운전실을 다른 크리에이터가 복사해 쓰는 공개 진입점. 모델 단위(1/24)로 짓고 스케일은 안 넣는다
    /// — 실척+루트1/24인 RTG는 holder.localScale=24로 상쇄한 뒤 넘길 것.</summary>
    public static partial class StsCraneCreator
    {
        /// <summary>holder 아래에 STS 40ft 스프레더를 헤드블록까지 통째로 생성.
        /// 헤드블록 소켓(실척 x±1.2, z±1.8, y9.392)이 RTG 로프점·Spreader_Hose 종단과 정합.</summary>
        public static void BuildSpreaderForReuse(Transform holder)
            => BuildSpreaderVisual(holder, SpreaderHalf40);

        /// <summary>holder 아래에 STS 운전실을 생성(홀더-로컬 자체 오프셋, RTG는 localScale=24 상쇄 후 위치 이동).
        /// mountTopY = 상부 결합면 Y(cab-local model, 기본 −0.05=STS 박스 하단, RTG −0.072).</summary>
        public static void BuildOperatorCabForReuse(Transform holder, float mountTopY = -0.05f)
            => BuildOperatorCab(holder, mountTopY);
    }
}
#endif
