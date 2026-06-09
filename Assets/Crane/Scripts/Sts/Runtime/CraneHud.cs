using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Container.Crane.Sts
{
    /// <summary>
    /// 크레인 HUD들이 공유하는 생성 유틸. 4개 HUD(상태/모드선택/조작안내/부위라벨)가
    /// 각자 복붙하던 ▸한글 폰트 후보 배열 ▸world-space Canvas+배경+Text 생성 ▸자동 스폰을 한 곳에 모음.
    /// 각 HUD는 자신만의 BuildText()와 배치 오프셋만 가지면 됨.
    /// </summary>
    internal static class CraneHud
    {
        // 한글 표시 위해 legacy UI.Text가 쓸 시스템 폰트 후보(플랫폼별 첫 매치 사용).
        static readonly string[] KoreanFonts =
        {
            "Noto Sans CJK KR", "NotoSansCJKkr-Regular", "Noto Sans KR",
            "Malgun Gothic", "맑은 고딕",
            "Apple SD Gothic Neo", "AppleGothic",
            "Arial Unicode MS", "Arial"
        };

        // 동적 폰트는 크기별로 1개만 만들어 공유 — 예전엔 Text마다 새로 만들어(HUD ~9개) 폰트 인스턴스·
        // 글리프 아틀라스가 따로 떠 Quest에서 메모리·텍스처 리빌드 낭비였다.
        static readonly Dictionary<int, Font> _fontCache = new();

        public static Font CreateKoreanFont(int size)
        {
            if (_fontCache.TryGetValue(size, out var f) && f != null) return f;
            f = Font.CreateDynamicFontFromOSFont(KoreanFonts, size);
            // OS에 후보 폰트가 하나도 없으면 null → 한글이 빈칸으로 렌더되므로 내장 폰트로 폴백.
            if (f == null) f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _fontCache[size] = f;
            return f;
        }

        /// <summary>
        /// world-space 패널 1개 생성: Canvas + 반투명 BG Image + 안쪽 여백(inset) 둔 Text.
        /// 반환=Canvas, out=Text. inset은 대칭(좌우 inset.x, 상하 inset.y).
        /// fitToText=false: BG가 panelPixels 고정 크기로 꽉 참(기존 동작).
        /// fitToText=true : BG가 글자 크기에 맞춰 자동 축소 — inset이 배경~글자 사이 여백(padding)이 됨.
        ///                  panelPixels는 무시(중심 기준으로 자라므로 배치 오프셋은 그대로 유지).
        /// </summary>
        public static Canvas BuildPanel(
            Transform parent, string canvasName, Vector2 panelPixels, float worldScale,
            Color bgColor, int fontSize, Color textColor, TextAnchor align, Vector2 inset,
            out Text text, bool fitToText = false)
        {
            var canvasGO = new GameObject(canvasName);
            canvasGO.transform.SetParent(parent, worldPositionStays: false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvasGO.AddComponent<CanvasScaler>();
            // GraphicRaycaster 없음 — HUD라 인터랙션 불필요(성능)

            var canvasRT = canvas.GetComponent<RectTransform>();
            canvasRT.sizeDelta = panelPixels;
            canvasRT.localScale = Vector3.one * worldScale;

            if (fitToText)
            {
                // BG가 글자 분량에 맞춰 줄어들게: 패널에 VerticalLayoutGroup(padding=inset) + ContentSizeFitter.
                // 중심(0.5,0.5) 기준으로 자라 배치 위치(컨트롤러 오프셋)는 글자량과 무관하게 고정.
                var panelGO = new GameObject("BG", typeof(Image));
                panelGO.transform.SetParent(canvasGO.transform, worldPositionStays: false);
                var panelRT = panelGO.GetComponent<RectTransform>();
                panelRT.anchorMin = panelRT.anchorMax = panelRT.pivot = new Vector2(0.5f, 0.5f);
                panelRT.anchoredPosition = Vector2.zero;
                StyleBg(panelGO.GetComponent<Image>(), bgColor);

                var layout = panelGO.AddComponent<VerticalLayoutGroup>();
                layout.padding = new RectOffset((int)inset.x, (int)inset.x, (int)inset.y, (int)inset.y);
                layout.childControlWidth = layout.childControlHeight = true;
                layout.childForceExpandWidth = layout.childForceExpandHeight = false;
                layout.childAlignment = TextAnchor.UpperLeft;

                var fitter = panelGO.AddComponent<ContentSizeFitter>();
                fitter.horizontalFit = fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

                var txtGO = new GameObject("Text", typeof(Text));
                txtGO.transform.SetParent(panelGO.transform, worldPositionStays: false);
                text = txtGO.GetComponent<Text>();
                ConfigText(text, fontSize, textColor, align);
                return canvas;
            }

            var bgGO = new GameObject("BG", typeof(Image));
            bgGO.transform.SetParent(canvasGO.transform, worldPositionStays: false);
            var bgRT = bgGO.GetComponent<RectTransform>();
            bgRT.anchorMin = Vector2.zero; bgRT.anchorMax = Vector2.one;
            bgRT.offsetMin = Vector2.zero; bgRT.offsetMax = Vector2.zero;
            StyleBg(bgGO.GetComponent<Image>(), bgColor);

            var txtGO2 = new GameObject("Text", typeof(Text));
            txtGO2.transform.SetParent(canvasGO.transform, worldPositionStays: false);
            var txtRT = txtGO2.GetComponent<RectTransform>();
            txtRT.anchorMin = Vector2.zero; txtRT.anchorMax = Vector2.one;
            txtRT.offsetMin = new Vector2(inset.x, inset.y);
            txtRT.offsetMax = new Vector2(-inset.x, -inset.y);
            text = txtGO2.GetComponent<Text>();
            ConfigText(text, fontSize, textColor, align);
            return canvas;
        }

        static void ConfigText(Text text, int fontSize, Color textColor, TextAnchor align)
        {
            text.font = CreateKoreanFont(fontSize);
            text.fontSize = fontSize;
            text.color = textColor;
            text.alignment = align;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.supportRichText = true;

            // 가독성 — 어두운 외곽선 + 그림자(legacy Text도 또렷하게 보이도록)
            var outline = text.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.95f);
            outline.effectDistance = new Vector2(1.6f, -1.6f);
            var shadow = text.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.55f);
            shadow.effectDistance = new Vector2(2.4f, -2.4f);
        }

        // 배경 이미지 스타일 — 둥근 사각 스프라이트(9-슬라이스)로 모서리를 둥글게. 라이브러리 불필요.
        static void StyleBg(Image img, Color color)
        {
            img.color = color;
            var sp = RoundedBgSprite();
            if (sp != null) { img.sprite = sp; img.type = Image.Type.Sliced; }
        }

        static Sprite _roundedBg;

        // 둥근 사각 배경 스프라이트를 절차 생성(한 번 만들어 캐시).
        //   유니티 내장 "UI/Skin/Background.psd"는 빌트인 'extra' 리소스라 런타임 Resources.GetBuiltinResource로
        //   못 읽고 "could not be loaded" 오류를 뱉었다(에디터 전용 AssetDatabase로만 접근). → 직접 만들어 의존 제거.
        static Sprite RoundedBgSprite()
        {
            if (_roundedBg != null) return _roundedBg;
            const int N = 32;
            const float R = 8f;   // 모서리 반경(px)
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false)
            { name = "CraneHud_RoundedBg", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                // 모서리 4곳만 곡선, 나머지는 직선. 모서리 안쪽 중심에서의 거리로 알파(안티앨리어싱).
                float dx = Mathf.Max(R - (x + 0.5f), (x + 0.5f) - (N - R));
                float dy = Mathf.Max(R - (y + 0.5f), (y + 0.5f) - (N - R));
                float a = (dx <= 0f || dy <= 0f)
                    ? 1f                                                 // 가장자리 직선 영역
                    : Mathf.Clamp01(R - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);  // 모서리 곡선(AA)
                px[y * N + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply(false);
            // 9-슬라이스: 모서리 R는 안 늘이고 가운데만 늘여 어떤 크기에도 둥근 모서리 유지.
            _roundedBg = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f),
                                       100f, 0, SpriteMeshType.FullRect, new Vector4(R, R, R, R));
            return _roundedBg;
        }

        /// <summary>
        /// 컨트롤러 등 앵커 '위'(월드 up 방향 worldHeight m)에 패널을 띄우고, 항상 카메라를 정면으로 향하게(빌보드).
        /// 컨트롤러를 손으로 기울여도 패널은 안 꺾이고 사용자를 바라본다. 위치는 매 프레임 앵커 기준 재계산.
        /// </summary>
        public static void FaceCameraAbove(Transform canvas, Transform anchor, float worldHeight, Camera cam)
        {
            if (canvas == null || anchor == null || cam == null) return;
            // 리그가 1/24로 축소되면 anchor(손)도 축소되므로, 띄울 높이도 같은 비율로 줄여 손 위 같은 상대위치 유지.
            //   (스케일 1이면 lossyScale=1이라 기존과 동일 — 하위호환.) 캔버스 크기는 부모(축소된 손) 상속으로 자동 비례.
            float s = anchor.lossyScale.y;
            canvas.position = anchor.position + Vector3.up * (worldHeight * s);
            Vector3 dir = canvas.position - cam.transform.position;
            if (dir.sqrMagnitude > 1e-6f)
                canvas.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
        }

        /// <summary>카메라(부모)의 자식인 캔버스가 카메라를 정면으로 향하게 하는 '로컬' 회전을 설정.
        /// localOffset이 고정이면 결과가 상수이므로 부착 시 1회만 호출하면 된다(매 프레임 재계산 불필요).
        /// 180° Y플립으로 거울 효과 해소, tilt로 시야각 기울임. (앵커를 따라다니는 경우는 FaceCameraAbove 사용.)</summary>
        public static void FaceCameraChild(Transform canvas, Vector3 localOffset, float tiltPitch = 0f, float tiltYaw = 0f)
        {
            if (canvas == null) return;
            Vector3 toCam = -localOffset;
            if (toCam.sqrMagnitude < 1e-6f) return;
            canvas.localRotation =
                Quaternion.LookRotation(toCam.normalized, Vector3.up)
                * Quaternion.Euler(0f, 180f, 0f)
                * Quaternion.Euler(tiltPitch, tiltYaw, 0f);
        }

        /// <summary>HUD 텍스트 갱신 주기(Hz) 공통값 — 매 프레임 문자열 생성/캔버스 리빌드 대신 이 빈도로 갱신.</summary>
        public const float TextHz = 8f;

        /// <summary>next 시각이 지났으면 true + next를 1/hz 뒤로 민다. hz≤0이면 항상 true(매 프레임). HUD 공용 스로틀.</summary>
        public static bool Due(ref float next, float hz)
        {
            if (hz <= 0f) return true;
            if (Time.unscaledTime < next) return false;
            next = Time.unscaledTime + 1f / hz;
            return true;
        }

        /// <summary>문자열이 바뀐 경우에만 Text.text에 대입(불필요한 캔버스 리빌드 방지). HUD 공용 dirty-check.</summary>
        public static void SetTextIfChanged(Text t, ref string last, string s)
        {
            if (t == null || s == last) return;
            t.text = s;
            last = s;
        }

        /// <summary>파츠 이름 끝의 "_번호" 접미사를 떼고 원래 이름을 반환(StsCraneCreator가 부품을 _1,_2…로 번호매김 →
        /// 이름 '==' 비교가 깨지지 않게). "Twistlock_Cone_3" → "Twistlock_Cone". 숫자 접미사가 없으면 그대로 반환.</summary>
        public static string BaseName(string n)
        {
            if (string.IsNullOrEmpty(n)) return n;
            int i = n.LastIndexOf('_');
            if (i > 0 && i < n.Length - 1)
            {
                for (int k = i + 1; k < n.Length; k++)
                    if (!char.IsDigit(n[k])) return n;   // 끝이 순수 숫자가 아니면 접미사 아님
                return n.Substring(0, i);
            }
            return n;
        }

        /// <summary>씬에 T가 없으면 자동 스폰(이미 있으면 스킵). 각 HUD의 RuntimeInitializeOnLoadMethod 본문 공용화.</summary>
        public static void EnsureSpawned<T>(string logTag) where T : MonoBehaviour
        {
            if (Object.FindAnyObjectByType<T>() != null) return;
            var go = new GameObject($"{typeof(T).Name} (auto)");
            go.AddComponent<T>();
            Debug.Log($"[{logTag}] 자동 스폰 — '{go.name}' 생성");
        }

        /// <summary>
        /// 이름으로 좌/우 컨트롤러 Transform 탐색 — ArrowHUD·ModeSelectorHUD 공유.
        /// 전역 스캔 '첫 매치'는 'Left Controller Stabilized' 같은 정적 보조 객체나 비활성 데모를
        /// 먼저 잡아 HUD가 바닥에 깔리거나 안 보이던 원인이었다. 그래서:
        ///   ① 카메라 리그(Camera.main.root) 하위로 범위를 좁히고,
        ///   ② side+"controller" & "hand" 없음 & 원점 아님 후보 중 이름이 가장 짧은 것(=컨트롤러 본체)을 고른다.
        /// side 는 "left" 또는 "right".
        /// </summary>
        public static Transform FindController(string side)
        {
            Transform rig = Camera.main != null ? Camera.main.transform.root : null;
            return PickController(side, requireHandless: true, rig)
                ?? PickController(side, requireHandless: true, null)
                ?? PickController(side, requireHandless: false, null);   // 폴백: "LeftHand Controller" 등도 허용
        }

        static Transform PickController(string side, bool requireHandless, Transform scope)
        {
            Transform best = null;
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!NameHas(t.name, side)) continue;
                // 'controller' 또는 손 추적 앵커('Left Hand'/'Right Hand' 리그) 둘 다 컨트롤러 후보로 인정.
                //   XR Origin Hands 리그는 컨트롤러 객체 이름이 'Right Hand'라 'controller' 단어가 없다.
                if (!NameHas(t.name, "controller") && !NameHas(t.name, "hand")) continue;
                if (requireHandless && NameHas(t.name, "hand")) continue;   // 1·2차는 controller 전용, 3차 폴백서 hand 허용
                // 미추적 컨트롤러는 리그 로컬 원점(=리그 루트 위치)에 머문다. 리그가 1/24로 축소되면 추적된 손도
                //   리그에 바짝 붙으므로, 월드 원점이 아니라 '리그 루트로부터의 거리'를 스케일에 맞춰 본다.
                //   (scope=리그, 스케일 1이면 0.2m로 기존과 동일 — 하위호환.)
                float minD = 0.2f * (scope != null ? Mathf.Max(scope.lossyScale.x, 1e-4f) : 1f);
                Vector3 refP = scope != null ? scope.position : Vector3.zero;
                if ((t.position - refP).sqrMagnitude < minD * minD) continue;   // 리그 원점 근처(미추적) 제외
                if (scope != null && !t.IsChildOf(scope)) continue;
                if (best == null || t.name.Length < best.name.Length) best = t;
            }
            return best;
        }

        static bool NameHas(string name, string sub) => name.IndexOf(sub, System.StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
