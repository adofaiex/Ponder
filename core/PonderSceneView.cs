using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ponder
{
    public class PonderSceneView : MonoBehaviour
    {
        private const string Cyan = "#5FE0FF";
        private const string Gray = "#8A8A8A";
        private static readonly Color DimColor = new Color(0f, 0f, 0f, 0.5f);
        private static readonly Color ButtonColor = new Color(0.08f, 0.11f, 0.16f, 0.95f);
        private static readonly Color CloseColor = new Color(0.85f, 0.2f, 0.2f, 0.95f);

        public static PonderSceneView? Instance { get; private set; }

        private readonly PonderPreview _preview = new PonderPreview();
        private PonderSceneDef? _scene;
        private int _chapterIndex;
        private PonderSceneLogic? _logic;

        private RawImage? previewImage;
        private TMP_Text? titleText;
        private TMP_Text? chapterText;
        private TMP_Text? descText;
        private RectTransform? closeBtn;
        private RectTransform? prevBtn;
        private RectTransform? nextBtn;

        private bool _open;

        public bool IsOpen => _open;

        public static PonderSceneView Create(TMP_FontAsset fallbackFont)
        {
            var go = new GameObject("PonderSceneView");
            DontDestroyOnLoad(go);
            var view = go.AddComponent<PonderSceneView>();
            view.Build(PonderPage.PickFont(fallbackFont));
            go.SetActive(false);
            Instance = view;
            return view;
        }

        private void Build(TMP_FontAsset font)
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30001;

            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;

            var bg = new GameObject("Background", typeof(RectTransform));
            bg.transform.SetParent(transform, false);
            var bgRT = (RectTransform)bg.transform;
            Stretch(bgRT);
            var bgImg = bg.AddComponent<Image>();
            bgImg.color = DimColor;

            titleText = AddText(transform, font, "", 42f);
            var titleRT = (RectTransform)titleText.rectTransform;
            titleRT.anchorMin = new Vector2(0f, 1f);
            titleRT.anchorMax = new Vector2(0f, 1f);
            titleRT.pivot = new Vector2(0f, 1f);
            titleRT.anchoredPosition = new Vector2(70f, -44f);
            titleRT.sizeDelta = new Vector2(1200f, 60f);
            titleText.alignment = TextAlignmentOptions.Left;

            chapterText = AddText(transform, font, "", 30f);
            var chapterRT = (RectTransform)chapterText.rectTransform;
            chapterRT.anchorMin = new Vector2(0f, 1f);
            chapterRT.anchorMax = new Vector2(0f, 1f);
            chapterRT.pivot = new Vector2(0f, 1f);
            chapterRT.anchoredPosition = new Vector2(72f, -118f);
            chapterRT.sizeDelta = new Vector2(1200f, 90f);
            chapterText.alignment = TextAlignmentOptions.Left;

            var previewArea = new GameObject("PreviewArea", typeof(RectTransform));
            previewArea.transform.SetParent(transform, false);
            var areaRT = (RectTransform)previewArea.transform;
            areaRT.anchorMin = Vector2.zero;
            areaRT.anchorMax = Vector2.one;
            areaRT.offsetMin = new Vector2(120f, 150f);
            areaRT.offsetMax = new Vector2(-120f, -210f);

            var previewGo = new GameObject("Preview", typeof(RectTransform));
            previewGo.transform.SetParent(previewArea.transform, false);
            var previewRT = (RectTransform)previewGo.transform;
            previewRT.anchorMin = new Vector2(0.5f, 0.5f);
            previewRT.anchorMax = new Vector2(0.5f, 0.5f);
            previewRT.pivot = new Vector2(0.5f, 0.5f);
            previewRT.sizeDelta = Vector2.zero;
            var fitter = previewGo.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fitter.aspectRatio = 1280f / 720f;
            var raw = previewGo.AddComponent<RawImage>();
            raw.color = Color.white;
            previewImage = raw;

            descText = AddText(transform, font, "", 24f);
            var descRT = (RectTransform)descText.rectTransform;
            descRT.anchorMin = new Vector2(0f, 0f);
            descRT.anchorMax = new Vector2(0f, 0f);
            descRT.pivot = new Vector2(0f, 0f);
            descRT.anchoredPosition = new Vector2(70f, 66f);
            descRT.sizeDelta = new Vector2(1600f, 60f);
            descText.alignment = TextAlignmentOptions.Left;

            closeBtn = AddButton(transform, font, "✕", CloseColor, 64f);
            ((RectTransform)closeBtn).anchorMin = new Vector2(1f, 1f);
            ((RectTransform)closeBtn).anchorMax = new Vector2(1f, 1f);
            ((RectTransform)closeBtn).pivot = new Vector2(1f, 1f);
            ((RectTransform)closeBtn).anchoredPosition = new Vector2(-48f, -44f);
            ((RectTransform)closeBtn).sizeDelta = new Vector2(56f, 56f);

            prevBtn = AddButton(transform, font, "◀ 上一章", ButtonColor, 26f);
            var prevRT = (RectTransform)prevBtn;
            prevRT.anchorMin = new Vector2(0.5f, 0f);
            prevRT.anchorMax = new Vector2(0.5f, 0f);
            prevRT.pivot = new Vector2(0.5f, 0f);
            prevRT.anchoredPosition = new Vector2(-140f, 48f);
            prevRT.sizeDelta = new Vector2(220f, 52f);

            nextBtn = AddButton(transform, font, "下一章 ▶", ButtonColor, 26f);
            var nextRT = (RectTransform)nextBtn;
            nextRT.anchorMin = new Vector2(0.5f, 0f);
            nextRT.anchorMax = new Vector2(0.5f, 0f);
            nextRT.pivot = new Vector2(0.5f, 0f);
            nextRT.anchoredPosition = new Vector2(140f, 48f);
            nextRT.sizeDelta = new Vector2(220f, 52f);
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static TMP_Text AddText(Transform parent, TMP_FontAsset font, string content, float fontSize)
        {
            var go = new GameObject("TMP", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.font = font;
            t.fontSize = fontSize;
            t.color = Color.white;
            t.alignment = TextAlignmentOptions.Center;
            t.enableWordWrapping = true;
            t.richText = true;
            t.text = content;
            t.raycastTarget = false;
            return t;
        }

        private static RectTransform AddButton(Transform parent, TMP_FontAsset font, string label, Color color, float fontSize)
        {
            var go = new GameObject("Button", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            var img = go.AddComponent<Image>();
            img.sprite = PonderSprites.Rounded;
            img.type = Image.Type.Sliced;
            img.color = color;
            img.raycastTarget = true;

            var textGo = new GameObject("TMP", typeof(RectTransform));
            textGo.transform.SetParent(go.transform, false);
            var textRT = (RectTransform)textGo.transform;
            Stretch(textRT);
            var t = textGo.AddComponent<TextMeshProUGUI>();
            t.font = font;
            t.fontSize = fontSize;
            t.color = Color.white;
            t.alignment = TextAlignmentOptions.Center;
            t.enableWordWrapping = false;
            t.richText = true;
            t.text = label;
            t.raycastTarget = false;
            return rt;
        }

        public void Show(PonderSceneDef scene)
        {
            _scene = scene;
            _chapterIndex = 0;
            if (!_preview.Build(scene))
            {
                Main.Handler?.Error("PonderSceneView: preview build failed");
                return;
            }
            if (previewImage != null && _preview.Texture != null)
            {
                previewImage.texture = _preview.Texture;
            }
            _preview.FitTrack();
            ApplySceneText();
            ApplyChapter(applyFocus: false);
            _logic = new PonderSceneLogic(_preview, scene);
            _open = true;
            gameObject.SetActive(true);

            if (scene.Logic.Count > 0)
            {
                _logic.Play(scene.Logic, loop: false, PlayCurrentChapterLogic);
            }
            else
            {
                PlayCurrentChapterLogic();
            }
        }

        private void PlayCurrentChapterLogic()
        {
            if (_logic == null || _scene == null)
            {
                return;
            }
            var chapter = _scene.ChapterAt(_chapterIndex);
            if (chapter != null && chapter.Logic.Count > 0)
            {
                _preview.ResetTransforms();
                _logic.Play(chapter.Logic, loop: true);
            }
        }

        private void ApplySceneText()
        {
            if (_scene == null)
            {
                return;
            }
            if (titleText != null)
            {
                titleText.text = $"<size=1.6em><color={Cyan}><b>{_scene.Title}</b></color></size>";
            }
            if (descText != null)
            {
                descText.text = string.IsNullOrEmpty(_scene.Description)
                    ? string.Empty
                    : $"<size=0.9em><color={Gray}>{_scene.Description}</color></size>";
            }
        }

        private void ApplyChapter(bool applyFocus = true)
        {
            if (_scene == null)
            {
                return;
            }
            var chapter = _scene.ChapterAt(_chapterIndex);
            if (chapterText != null)
            {
                if (chapter != null)
                {
                    chapterText.text =
                        $"<size=0.9em><color={Cyan}>第 {_chapterIndex + 1} / {_scene.Chapters.Count} 章</color></size>\n" +
                        $"<size=1.0em>{chapter.Text}</size>";
                }
                else
                {
                    chapterText.text = string.Empty;
                }
            }
            if (applyFocus && chapter != null)
            {
                _preview.FocusFloor(chapter.Floor);
            }
        }

        private void PrevChapter()
        {
            if (_scene == null || _scene.Chapters.Count == 0)
            {
                return;
            }
            _chapterIndex = (_chapterIndex - 1 + _scene.Chapters.Count) % _scene.Chapters.Count;
            ApplyChapter();
            PlayCurrentChapterLogic();
        }

        private void NextChapter()
        {
            if (_scene == null || _scene.Chapters.Count == 0)
            {
                return;
            }
            _chapterIndex = (_chapterIndex + 1) % _scene.Chapters.Count;
            ApplyChapter();
            PlayCurrentChapterLogic();
        }

        private static bool IsPointIn(RectTransform rt)
        {
            if (rt == null)
            {
                return false;
            }
            return RectTransformUtility.RectangleContainsScreenPoint(rt, Input.mousePosition, null);
        }

        public void Tick()
        {
            if (!_open)
            {
                return;
            }
            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
            {
                Close();
                return;
            }
            if (Input.GetMouseButtonDown(0))
            {
                if (IsPointIn(closeBtn))
                {
                    Close();
                }
                else if (IsPointIn(prevBtn))
                {
                    PrevChapter();
                }
                else if (IsPointIn(nextBtn))
                {
                    NextChapter();
                }
            }
            _logic?.Update(Time.deltaTime);
        }

        public void Close()
        {
            _open = false;
            _logic?.Stop();
            _logic = null;
            _preview.Clear();
            _scene = null;
            if (gameObject.activeSelf)
            {
                gameObject.SetActive(false);
            }
        }
    }
}
