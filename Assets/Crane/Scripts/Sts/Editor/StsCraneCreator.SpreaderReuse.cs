#if UNITY_EDITOR
using UnityEngine;

namespace Container.Crane.Sts.EditorTools
{
    /// <summary>
    /// STS 스프레더를 RTG 등 다른 크리에이터가 '그대로 복사(재사용)'하기 위한 공개 진입점.
    /// 같은 partial 클래스라 private BuildSpreaderVisual·const SpreaderHalf40 에 접근 가능
    /// (대용량 StsCraneCreator.cs 본문은 건드리지 않고 여기서만 노출).
    ///
    /// ※ 스케일 주의: 이 빌더는 '모델 단위(실척×ModelScale=1/24)'로 형상을 짓고, 스프레더
    ///   transform 자체엔 스케일을 넣지 않는다(=루트 스케일 1 가정). 실척 미터+루트 1/24로 짓는
    ///   RTG에서 쓰려면, 호출 측이 루트의 1/24을 상쇄하도록 holder.localScale=24 를 준 뒤 넘긴다.
    /// </summary>
    public static partial class StsCraneCreator
    {
        /// <summary>holder 아래에 STS 40ft 스프레더(헤드블록·트위스트락·텔레스코픽·부속·작업등)를 그대로 생성.</summary>
        //   [2026-07-02 오너 지시] includeHead:true — STS 스프레더를 헤드블록까지 통째로 복사.
        //   STS 헤드블록 소켓(실척 x±1.2,z±1.8, 본체중심 y9.392)이 RTG 리빙 로프점·Spreader_Hose 종단과 정합(RTG 자체 헤드블록은 폐기).
        public static void BuildSpreaderForReuse(Transform holder)
            => BuildSpreaderVisual(holder, SpreaderHalf40, includeHead: true);

        /// <summary>holder 아래에 STS 운전실(CSG 통짜 셸·전면 경사창·아래보기 바닥창·측창·후면 도어/발판/난간)을 그대로 생성.
        ///   BuildOperatorCab은 홀더-로컬에 자체 오프셋(본체 중심 model x≈-0.096, y -0.077..-0.152)으로 배치한다.
        ///   RTG(실척+루트1/24)에선 holder.localScale=24 로 루트 1/24을 상쇄하고, 원하는 실척 위치로 holder를 옮겨 쓴다.</summary>
        //   mountTopY: 상부 결합면 Y(cab-local model). 재사용 크레인의 상부 구조 밑면에 맞춰 넘긴다
        //     (기본 −0.05=STS 박스 하단. RTG는 프레임 밑면 대응값 −0.072).
        public static void BuildOperatorCabForReuse(Transform holder, float mountTopY = -0.05f)
            => BuildOperatorCab(holder, mountTopY);
    }
}
#endif
