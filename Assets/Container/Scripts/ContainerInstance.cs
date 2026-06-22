using TMPro;
using UnityEngine;

namespace ContainerProject
{
    /// <summary>
    /// 컨테이너 프리팹 인스턴스. 스폰 시점에 번호·색상을 적용한다.
    /// 메시는 자식 오브젝트에 들어가 있어야 하며, 번호 라벨은 TMP 텍스트로 구성한다.
    /// </summary>
    public class ContainerInstance : MonoBehaviour
    {
        [System.Serializable]
        public struct BodyRendererSlot
        {
            public Renderer renderer;
            [Tooltip("색상을 적용할 머티리얼 슬롯 인덱스. -1이면 renderer 전체에 적용.")]
            public int materialIndex;
        }

        [Header("외형 참조")]
        [Tooltip("본체 색상을 적용할 Renderer 슬롯들. 단일 MeshRenderer + 4 서브메시 구조에서는 slot=0(Body), slot=1(Door)만 등록.")]
        [SerializeField] BodyRendererSlot[] bodyRenderers;
        [Tooltip("컨테이너 측면/도어에 부착될 번호 텍스트들")]
        [SerializeField] TMP_Text[] idLabels;

        [Header("런타임 정보 (읽기 전용)")]
        [SerializeField] string containerId;
        [SerializeField] string displayId;
        [SerializeField] string colorName;
        [Tooltip("ID 기반 결정적 하중(톤). 스프레더가 잡으면 이 값이 권상 하중이 된다.")]
        [SerializeField] float loadTons;

        public string ContainerId => containerId;
        public string DisplayId => displayId;
        public string ColorName => colorName;
        public float LoadTons => loadTons;
        public LoadGrade LoadGrade => ContainerLoad.Grade(loadTons);

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color"); // Built-in 폴백
        static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");

        public void ApplyRandom(ContainerColorPalette palette, System.Random rng = null)
        {
            rng ??= new System.Random();

            containerId = ContainerIdGenerator.Generate(rng);
            displayId = ContainerIdGenerator.FormatForDisplay(containerId);
            loadTons = ContainerLoad.WeightTons(containerId);
            ApplyIdToLabels(displayId);

            if (palette != null && palette.Count > 0)
            {
                var entry = palette.Random(rng);
                colorName = entry.name;
                ApplyColor(entry.color);
            }
        }

        public void Apply(string id, Color color, string colorLabel = null)
        {
            containerId = id;
            displayId = ContainerIdGenerator.FormatForDisplay(id);
            loadTons = ContainerLoad.WeightTons(id);
            colorName = colorLabel;
            ApplyIdToLabels(displayId);
            ApplyColor(color);
        }

        void ApplyIdToLabels(string text)
        {
            if (idLabels == null) return;
            foreach (var label in idLabels)
            {
                if (label != null) label.text = text;
            }
        }

        void ApplyColor(Color color)
        {
            if (bodyRenderers == null) return;

            // 같은 선사색 인스턴스가 '복붙'처럼 보이지 않도록 ID 기반 결정적 미세 변주.
            //   명도·채도·광택을 살짝 흔들어 햇빛 바램/먼지/세월감을 표현(같은 ID = 항상 같은 외형, 재현 가능).
            //   bodyRenderers엔 Body·Door만 등록되므로 프레임/캐스팅 회색은 영향 없음(절차강 베이스 유지).
            float h1 = StableHash.Hash01(containerId, 0x9E3779B9u);   // 명도/광택 시드
            float h2 = StableHash.Hash01(containerId, 0x85EBCA6Bu);   // 채도 시드
            float h3 = StableHash.Hash01(containerId, 0xC2B2AE35u);   // 색조 시드
            Color.RGBToHSV(color, out float ch, out float cs, out float cv);
            cv = Mathf.Clamp01(cv * Mathf.Lerp(0.90f, 1.04f, h1));   // 명도: 주로 약간 어둡게(먼지), 가끔 밝게(바램)
            cs = Mathf.Clamp01(cs * Mathf.Lerp(0.90f, 1.02f, h2));   // 채도: 약간 빠짐(색바램)
            ch = Mathf.Repeat(ch + Mathf.Lerp(-0.014f, 0.014f, h3), 1f);   // 색조: ±~5°(선사색 정체성 유지 위해 좁게)
            Color varied = Color.HSVToRGB(ch, cs, cv);
            varied.a = color.a;
            float smoothness = Mathf.Lerp(0.22f, 0.40f, h1);          // 광택: 무광(낡음)~반광

            var mpb = new MaterialPropertyBlock();
            foreach (var slot in bodyRenderers)
            {
                if (slot.renderer == null) continue;
                if (slot.materialIndex < 0)
                {
                    slot.renderer.GetPropertyBlock(mpb);
                    mpb.SetColor(BaseColorId, varied);
                    mpb.SetColor(ColorId, varied);
                    mpb.SetFloat(SmoothnessId, smoothness);
                    slot.renderer.SetPropertyBlock(mpb);
                }
                else
                {
                    slot.renderer.GetPropertyBlock(mpb, slot.materialIndex);
                    mpb.SetColor(BaseColorId, varied);
                    mpb.SetColor(ColorId, varied);
                    mpb.SetFloat(SmoothnessId, smoothness);
                    slot.renderer.SetPropertyBlock(mpb, slot.materialIndex);
                }
            }
        }
    }
}
