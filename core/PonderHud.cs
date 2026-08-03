using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ponder
{
    public class PonderHud : MonoBehaviour
    {
        private const string Cyan = "#5FE0FF";
        private const string White = "#FFFFFF";
        private const string Gray = "#9E9E9E";

        private static readonly Vector2 PanelSize = new Vector2(320f, 0f);

        private Canvas canvas;
        private RectTransform panel;
        private RectTransform textRT;
        private TMP_Text text;
        private RectTransform bar;
        private Image barFill;
        private Sprite fillSprite;
        private Sprite ringSprite;
        private PonderSubject subject;
        private bool dirty;

        public TMP_FontAsset Font { get; private set; }

        public static PonderHud Create(TMP_FontAsset font)
        {
            GameObject go = new GameObject("PonderHud");
            DontDestroyOnLoad(go);
            PonderHud hud = go.AddComponent<PonderHud>();
            hud.Build(font);
            go.SetActive(false);
            return hud;
        }

        private void Build(TMP_FontAsset font)
        {
            Font = font;
            if (Font == null)
            {
                Font = TMP_Settings.defaultFontAsset;
            }

            EnsureSprites();

            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30000;

            CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;

            GameObject panelGo = new GameObject("Panel", typeof(RectTransform));
            panelGo.transform.SetParent(transform, false);
            panel = (RectTransform)panelGo.transform;
            panel.anchorMin = Vector2.zero;
            panel.anchorMax = Vector2.zero;
            panel.pivot = new Vector2(0f, 0f);
            panel.sizeDelta = PanelSize;

            Image bg = panelGo.AddComponent<Image>();
            bg.sprite = fillSprite;
            bg.type = Image.Type.Sliced;
            bg.color = new Color(0.020f, 0.028f, 0.042f, 0.86f);
            bg.raycastTarget = false;

            Image border = CreateChildImage(panelGo, "Border", ringSprite, new Color(1f, 1f, 1f, 0.90f));
            border.type = Image.Type.Sliced;
            RectTransform borderRect = (RectTransform)border.transform;
            borderRect.anchorMin = Vector2.zero;
            borderRect.anchorMax = Vector2.one;
            borderRect.pivot = new Vector2(0.5f, 0.5f);
            borderRect.offsetMin = Vector2.zero;
            borderRect.offsetMax = Vector2.zero;

            GameObject textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(panelGo.transform, false);
            textRT = (RectTransform)textGo.transform;
            textRT.anchorMin = new Vector2(0f, 1f);
            textRT.anchorMax = new Vector2(1f, 1f);
            textRT.pivot = new Vector2(0f, 1f);
            textRT.anchoredPosition = new Vector2(16f, -12f);
            textRT.sizeDelta = new Vector2(PanelSize.x - 32f, 0f);

            text = textGo.AddComponent<TextMeshProUGUI>();
            text.font = Font;
            text.fontSize = 16f;
            text.color = new Color(0.95f, 0.97f, 1f, 1f);
            text.enableWordWrapping = true;
            text.richText = true;
            text.raycastTarget = false;

            GameObject barGo = new GameObject("Bar", typeof(RectTransform));
            barGo.transform.SetParent(panelGo.transform, false);
            bar = (RectTransform)barGo.transform;
            bar.anchorMin = new Vector2(0f, 0f);
            bar.anchorMax = new Vector2(1f, 0f);
            bar.pivot = new Vector2(0.5f, 0.5f);
            bar.anchoredPosition = new Vector2(0f, 8f);
            bar.sizeDelta = new Vector2(-32f, 6f);

            Image barBg = barGo.AddComponent<Image>();
            barBg.sprite = fillSprite;
            barBg.type = Image.Type.Sliced;
            barBg.color = new Color(0f, 0f, 0f, 0.55f);
            barBg.raycastTarget = false;

            GameObject fillGo = new GameObject("Fill", typeof(RectTransform));
            fillGo.transform.SetParent(barGo.transform, false);
            RectTransform fillRT = (RectTransform)fillGo.transform;
            fillRT.anchorMin = Vector2.zero;
            fillRT.anchorMax = Vector2.one;
            fillRT.offsetMin = Vector2.zero;
            fillRT.offsetMax = Vector2.zero;

            barFill = fillGo.AddComponent<Image>();
            barFill.sprite = fillSprite;
            barFill.type = Image.Type.Filled;
            barFill.fillMethod = Image.FillMethod.Horizontal;
            barFill.fillOrigin = (int)Image.OriginHorizontal.Left;
            barFill.color = new Color(0.37f, 0.88f, 1f);
            barFill.raycastTarget = false;

            barGo.SetActive(false);
        }

        private Image CreateChildImage(GameObject parent, string name, Sprite sprite, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent.transform, false);
            Image image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        public void SetSubject(PonderSubject sub)
        {
            if (sub == subject)
            {
                return;
            }
            subject = sub;
            if (sub != null && text != null)
            {
                text.text =
                    $"<size=1.15em><color={Cyan}><b>{sub.Title}</b></color></size>\n" +
                    $"<color={White}>{sub.Description}</color>\n" +
                    $"<color={Gray}>{PonderLang.Get("ui.holdHint", "按住 [Alt] 开始思索")}</color>";
            }
            dirty = true;
        }

        public void SetCharging(bool visible, float fill)
        {
            if (bar.gameObject.activeSelf != visible)
            {
                bar.gameObject.SetActive(visible);
                dirty = true;
            }
            if (barFill != null)
            {
                barFill.fillAmount = Mathf.Clamp01(fill);
            }
        }

        public void ShowAt(Vector2 screenPos)
        {
            if (!gameObject.activeSelf)
            {
                gameObject.SetActive(true);
            }
            if (dirty)
            {
                Resize();
            }

            RectTransform canvasRT = (RectTransform)canvas.transform;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRT, screenPos, null, out Vector2 local))
            {
                Rect r = canvasRT.rect;
                float w = panel.rect.width;
                float h = panel.rect.height;
                Vector2 bl = local - r.min;
                float x = Mathf.Clamp(bl.x + 14f, 4f, Mathf.Max(4f, r.width - w - 4f));
                float y = Mathf.Clamp(bl.y + 18f, 4f, Mathf.Max(4f, r.height - h - 4f));
                panel.anchoredPosition = new Vector2(x, y);
            }
            dirty = false;
        }

        public void Hide()
        {
            if (gameObject.activeSelf)
            {
                gameObject.SetActive(false);
            }
        }

        private void Resize()
        {
            if (text == null)
            {
                return;
            }
            Canvas.ForceUpdateCanvases();
            float ph = text.preferredHeight;
            bool showBar = bar != null && bar.gameObject.activeSelf;
            float h = 24f + ph + (showBar ? 20f : 0f);
            panel.sizeDelta = new Vector2(PanelSize.x, h);
            textRT.sizeDelta = new Vector2(PanelSize.x - 32f, ph);
        }

        // ---------- ADOFAI 风格圆角贴图（参考 Jade） ----------

        private void EnsureSprites()
        {
            if (fillSprite != null)
            {
                return;
            }

            const int size = 64;
            const float radius = 14f;
            Texture2D fillTex = CreateRoundedTexture(size, radius, 0f);
            Texture2D ringTex = CreateRoundedTexture(size, radius, 3f);
            Vector4 border = new Vector4(radius + 4f, radius + 4f, radius + 4f, radius + 4f);
            fillSprite = Sprite.Create(fillTex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
            ringSprite = Sprite.Create(ringTex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
        }

        private static Texture2D CreateRoundedTexture(int size, float radius, float ringThickness)
        {
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            float half = size * 0.5f;
            float box = half - radius - 1.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Abs(x + 0.5f - half) - box;
                    float dy = Mathf.Abs(y + 0.5f - half) - box;
                    float ax = Mathf.Max(dx, 0f);
                    float ay = Mathf.Max(dy, 0f);
                    float dist = Mathf.Sqrt(ax * ax + ay * ay) + Mathf.Min(Mathf.Max(dx, dy), 0f) - radius;

                    float alpha = ringThickness <= 0f
                        ? Mathf.Clamp01(0.5f - dist)
                        : Mathf.Clamp01(Mathf.Min(0.5f - dist, dist + ringThickness + 0.5f));
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }

            texture.Apply();
            return texture;
        }
    }
}
