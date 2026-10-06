using UnityEngine;

namespace AIXRCrane
{
    /// <summary>컨테이너 색상 풀(실제 선사 컬러 기반). 색상만 정의 → 런타임 MaterialPropertyBlock 으로 적용.</summary>
    [CreateAssetMenu(fileName = "ContainerColorPalette", menuName = "Container/Color Palette")]
    public class ContainerColorPalette : ScriptableObject
    {
        [System.Serializable]
        public struct ColorEntry
        {
            public string name;
            [ColorUsage(showAlpha: false)] public Color color;
        }

        [SerializeField]
        ColorEntry[] colors = new ColorEntry[]
        {
            new ColorEntry { name = "White",          color = new Color(0.92f, 0.92f, 0.90f) },
            new ColorEntry { name = "Maersk Navy",    color = new Color(0.06f, 0.18f, 0.42f) },
            new ColorEntry { name = "Maersk Sky",     color = new Color(0.26f, 0.69f, 0.83f) },
            new ColorEntry { name = "MSC Beige",      color = new Color(0.85f, 0.78f, 0.58f) },
            new ColorEntry { name = "CMA CGM Red",    color = new Color(0.75f, 0.10f, 0.13f) },
            new ColorEntry { name = "Hapag Orange",   color = new Color(0.95f, 0.45f, 0.10f) },
            new ColorEntry { name = "Evergreen",      color = new Color(0.13f, 0.45f, 0.30f) },
            new ColorEntry { name = "ONE Magenta",    color = new Color(0.91f, 0.12f, 0.39f) },
            new ColorEntry { name = "COSCO Red",      color = new Color(0.83f, 0.18f, 0.18f) },
            new ColorEntry { name = "HMM Orange",     color = new Color(1.00f, 0.62f, 0.20f) },
            new ColorEntry { name = "Rust Brown",     color = new Color(0.45f, 0.30f, 0.22f) },
            new ColorEntry { name = "Steel Gray",     color = new Color(0.55f, 0.55f, 0.58f) },
            // 추가 색 — 빈 색조 보강, 전부 기존색과 ΔE2000 ≥ 12(중복 없음).
            new ColorEntry { name = "Hanjin Blue",     color = new Color(0.09f, 0.33f, 0.60f) },  // 미드 로열블루
            new ColorEntry { name = "Yang Ming Teal",  color = new Color(0.16f, 0.46f, 0.46f) },  // 청록
            new ColorEntry { name = "Sea Green",       color = new Color(0.27f, 0.60f, 0.42f) },  // 밝은 시그린
            new ColorEntry { name = "Bottle Green",    color = new Color(0.09f, 0.27f, 0.19f) },  // 진한 보틀그린
            new ColorEntry { name = "Lime Olive",      color = new Color(0.52f, 0.62f, 0.16f) },  // 옐로그린/라임
            new ColorEntry { name = "Golden Yellow",   color = new Color(0.94f, 0.78f, 0.12f) },  // 선명한 노랑
            new ColorEntry { name = "Olive Drab",      color = new Color(0.42f, 0.44f, 0.27f) },  // 군용 올리브
            new ColorEntry { name = "Khaki Tan",       color = new Color(0.66f, 0.58f, 0.40f) },  // 탁한 카키탄
            new ColorEntry { name = "Burgundy",        color = new Color(0.39f, 0.12f, 0.18f) },  // 진한 적갈(마룬)
            new ColorEntry { name = "Plum",            color = new Color(0.42f, 0.22f, 0.44f) },  // 탁한 보라
            new ColorEntry { name = "Charcoal",        color = new Color(0.25f, 0.26f, 0.28f) },  // 거의 검정 중성
            new ColorEntry { name = "Pale Sky",        color = new Color(0.72f, 0.81f, 0.84f) }   // 밝은 한색 그레이
        };

        public int Count => colors == null ? 0 : colors.Length;
        public ColorEntry Get(int index)
        {
            if (colors == null || colors.Length == 0)
                return new ColorEntry { name = "White", color = UnityEngine.Color.white };
            return colors[index];
        }

        public ColorEntry Random(System.Random rng = null)
        {
            if (colors == null || colors.Length == 0)
                return new ColorEntry { name = "White", color = UnityEngine.Color.white };
            rng ??= new System.Random();
            return colors[rng.Next(colors.Length)];
        }
    }
}
