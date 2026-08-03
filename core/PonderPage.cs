using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ponder
{
    public class PonderPage : MonoBehaviour
    {
        private const string Cyan = "#5FE0FF";

        private static readonly Color BackgroundColor = new Color(0.05f, 0.06f, 0.075f, 0.94f);

        private TMP_Text titleText;
        private TMP_Text descText;
        private PonderSubject subject;
        private RectTransform closeBtn;
        private bool open;

        public bool IsOpen => open;

        public static PonderPage Create(TMP_FontAsset fallbackFont)
        {
            GameObject go = new GameObject("PonderPage");
            DontDestroyOnLoad(go);
            PonderPage page = go.AddComponent<PonderPage>();
            page.Build(PickFont(fallbackFont));
            go.SetActive(false);
            return page;
        }

        public static TMP_FontAsset PickFont(TMP_FontAsset fallback)
        {
            if (RDString.editorFonts != null && RDString.editorFonts.Length > 0 && RDString.editorFonts[0] != null)
            {
                return RDString.editorFonts[0];
            }
            return fallback;
        }

        private void Build(TMP_FontAsset font)
        {
            Canvas canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30001;

            CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;

            GameObject bg = new GameObject("Background", typeof(RectTransform));
            bg.transform.SetParent(transform, false);
            RectTransform bgRT = (RectTransform)bg.transform;
            bgRT.anchorMin = Vector2.zero;
            bgRT.anchorMax = Vector2.one;
            bgRT.offsetMin = Vector2.zero;
            bgRT.offsetMax = Vector2.zero;
            Image bgImg = bg.AddComponent<Image>();
            bgImg.color = BackgroundColor;

            GameObject content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(transform, false);
            RectTransform contentRT = (RectTransform)content.transform;
            contentRT.anchorMin = new Vector2(0.5f, 0.5f);
            contentRT.anchorMax = new Vector2(0.5f, 0.5f);
            contentRT.pivot = new Vector2(0.5f, 0.5f);
            contentRT.anchoredPosition = Vector2.zero;

            VerticalLayoutGroup layout = content.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 22f;
            layout.padding = new RectOffset(36, 36, 28, 28);
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            ContentSizeFitter contentFitter = content.AddComponent<ContentSizeFitter>();
            contentFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            AddText(content.transform, font, $"<size=1.3em><color=#8A8A8A>{PonderLang.Get("ui.pageTitle", "PONDER · 思索")}</color></size>", 26f);
            titleText = AddText(content.transform, font, "", 46f);
            descText = AddText(content.transform, font, "", 26f);
            AddText(content.transform, font, $"<size=1.0em><color=#8A8A8A>{PonderLang.Get("ui.closeHint", "点击任意处或按 Esc 关闭")}</color></size>", 22f);

            closeBtn = BuildCloseButton(font);
        }

        private RectTransform BuildCloseButton(TMP_FontAsset font)
        {
            var go = new GameObject("CloseButton", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(1f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-48f, -44f);
            rt.sizeDelta = new Vector2(56f, 56f);
            var img = go.AddComponent<Image>();
            img.sprite = PonderSprites.Rounded;
            img.type = Image.Type.Sliced;
            img.color = new Color(0.85f, 0.2f, 0.2f, 0.95f);
            img.raycastTarget = true;

            var textGo = new GameObject("TMP", typeof(RectTransform));
            textGo.transform.SetParent(go.transform, false);
            var textRT = (RectTransform)textGo.transform;
            textRT.anchorMin = Vector2.zero;
            textRT.anchorMax = Vector2.one;
            textRT.offsetMin = Vector2.zero;
            textRT.offsetMax = Vector2.zero;
            var t = textGo.AddComponent<TextMeshProUGUI>();
            t.font = font;
            t.fontSize = 30f;
            t.color = Color.white;
            t.alignment = TextAlignmentOptions.Center;
            t.text = "✕";
            t.raycastTarget = false;
            return rt;
        }

        private static TMP_Text AddText(Transform parent, TMP_FontAsset font, string content, float fontSize)
        {
            GameObject go = new GameObject("TMP", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(640f, 0f);

            TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
            t.font = font;
            t.fontSize = fontSize;
            t.color = Color.white;
            t.alignment = TextAlignmentOptions.Center;
            t.enableWordWrapping = true;
            t.richText = true;
            t.text = content;

            ContentSizeFitter fitter = go.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return t;
        }

        public void Open(PonderSubject sub)
        {
            subject = sub;
            if (titleText != null)
            {
                titleText.text = $"<size=1.9em><color={Cyan}><b>{sub.Title}</b></color></size>";
            }
            if (descText != null)
            {
                descText.text =
                    $"{sub.Description}\n\n" +
                    $"<size=0.9em><color=#8A8A8A>{PonderLang.Get("ui.placeholderDesc", "此处将承载关于此配置项的沉浸式思索场景（占位）")}</color></size>";
            }
            open = true;
            gameObject.SetActive(true);
        }

        public void Tick()
        {
            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(0))
            {
                if (Input.GetMouseButtonDown(0) && closeBtn != null &&
                    RectTransformUtility.RectangleContainsScreenPoint(closeBtn, Input.mousePosition, null))
                {
                    Close();
                    return;
                }
                Close();
            }
        }

        public void Close()
        {
            open = false;
            if (gameObject.activeSelf)
            {
                gameObject.SetActive(false);
            }
        }
    }
}
