#if UNITY_EDITOR
using UnityEngine;

namespace AIXRCrane.Crane.Sts.EditorTools
{
    /// <summary>STS 스프레더를 다른 크리에이터가 그대로 복사해 쓰기 위한 공개 진입점(같은 partial 이라 private
    /// BuildSpreaderVisual 에 접근 가능). 모델 단위(1/24)로 짓고 스케일은 안 넣는다 — 실척+루트1/24인 RTG는
    /// holder.localScale=24 로 상쇄한 뒤 넘길 것.</summary>
    public static partial class StsCraneCreator
    {
        /// <summary>holder 아래에 STS 40ft 스프레더(헤드블록·트위스트락·텔레스코픽·부속·작업등)를 그대로 생성.</summary>
        //   includeHead:true — STS 스프레더를 헤드블록까지 통째로 복사.
        //   STS 헤드블록 소켓(실척 x±1.2,z±1.8, 중심 y9.392)이 RTG 로프점·Spreader_Hose 종단과 정합(RTG 헤드블록은 안 씀).
        public static void BuildSpreaderForReuse(Transform holder)
            => BuildSpreaderVisual(holder, SpreaderHalf40, includeHead: true);

        /// <summary>holder 아래에 STS 운전실(셸·경사창·바닥창·측창·후면 도어/발판/난간)을 그대로 생성. 홀더-로컬
        /// 자체 오프셋(x≈-0.096, y -0.077..-0.152)으로 배치 — RTG는 holder.localScale=24 로 1/24 상쇄 후 위치를 옮긴다.</summary>
        //   mountTopY: 상부 결합면 Y(cab-local model). 재사용 크레인의 상부 구조 밑면에 맞춰 넘긴다
        //     (기본 −0.05=STS 박스 하단. RTG는 프레임 밑면 대응값 −0.072).
        public static void BuildOperatorCabForReuse(Transform holder, float mountTopY = -0.05f)
            => BuildOperatorCab(holder, mountTopY);
    }
}
#endif
