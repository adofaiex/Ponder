using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using GDMiniJSON;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ADOFAI;

namespace Ponder
{
    public class PonderSceneView : MonoBehaviour
    {
        private const string Cyan = "#5FE0FF";
        private const string Gray = "#8A8A8A";
        private static readonly Color DimColor = new Color(0f, 0f, 0f, 0.55f);
        private static readonly Color ButtonColor = new Color(0.08f, 0.11f, 0.16f, 0.95f);
        private static readonly Color CloseColor = new Color(0.85f, 0.2f, 0.2f, 0.65f);
        // 关闭按钮 hover 高亮 alpha (Color != Color 会触发 Image 重建，Color 字段改值不会)
        private static readonly Color CloseColorHover = new Color(0.95f, 0.25f, 0.25f, 1f);
        private static readonly Color ActiveChapterColor = new Color(0.13f, 0.42f, 0.5f, 0.95f);

        public static PonderSceneView? Instance { get; private set; }

        private readonly PonderPreview _preview = new PonderPreview();
        private PonderSceneDef? _scene;
        private int _chapterIndex;
        private PonderSceneLogic? _logic;

        // 自由编辑 (MC Ponder 风格万物皆可 Ponder)：设计师写在 JSON 里的 edits + 沙盒内
        // 拖动产生的 edits 全部入栈。PlayCurrentChapterLogic 在每章节启动时 replay 至该章节；
        // Close 时 RollbackAll 清空栈并把沙盒恢复到 Build 后的基准态。
        private readonly List<PonderEdit> _editStack = new List<PonderEdit>();

        private RawImage? previewImage;
        private TMP_Text? titleText;
        private TMP_Text? chapterText;
        private TMP_Text? descText;
        private RectTransform? closeBtn;
        private Image? closeHover;
        private RectTransform? prevBtn;
        private RectTransform? nextBtn;
        private RectTransform? backBtn;

        private Image? bgImage;
        private Texture2D? _bgTexture;

        private bool _dragActive;
        private Vector3 _lastDragPos;

        // 打开 Ponder 时临时禁用 scnEditor 整套 Update (含鼠标拖动/滚轮缩放等 raw input)，
        // 否则 Ponder 沙盒视角与编辑器视角会被同时驱动、互相打架。仅 enabled=false 不够——
        // scnEditor 同 GameObject 上其他 MonoBehaviour 仍可能在 Update 里继续拖动 camera，
        // 所以把整 GameObject 一起 SetActive(false)，Ponder 关闭时再 SetActive(true) 恢复。
        private scnEditor? _suppressedEditor;
        private bool _suppressedEditorWasEnabled;
        private GameObject? _suppressedEditorGo;
        private bool _suppressedGoWasActive;

        private RectTransform? chapterBar;
        private readonly List<RectTransform> chapterButtons = new List<RectTransform>();
        private readonly List<int> chapterButtonIndexes = new List<int>();
        private RectTransform? chapterTipPanel;
        private TMP_Text? chapterTip;

        private GameObject? entryRoot;
        private RectTransform? entryPanel;
        private readonly List<RectTransform> entryButtons = new List<RectTransform>();
        private readonly List<PonderSceneDef> entryScenes = new List<PonderSceneDef>();
        private TMP_FontAsset font;

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
            this.font = font;
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30001;

            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;

            // 让 EventSystem 命中本画布：既能响应点击，也能拦截下方编辑器按钮的点击
            gameObject.AddComponent<GraphicRaycaster>();

            var bg = new GameObject("Background", typeof(RectTransform));
            bg.transform.SetParent(transform, false);
            var bgRT = (RectTransform)bg.transform;
            Stretch(bgRT);
            var bgImg = bg.AddComponent<Image>();
            bgImg.color = DimColor;
            bgImage = bgImg;

            // 毛玻璃上再盖一层半透明暗色，保证文字可读（不拦点击，由 bg 拦截）
            var dimGo = new GameObject("BackgroundDim", typeof(RectTransform));
            dimGo.transform.SetParent(transform, false);
            var dimRT = (RectTransform)dimGo.transform;
            Stretch(dimRT);
            var dimImg = dimGo.AddComponent<Image>();
            dimImg.color = new Color(0f, 0f, 0f, 0.38f);
            dimImg.raycastTarget = false;

            // ---- 左上信息块（Create 风格） ----
            titleText = AddText(transform, font, "", 40f);
            var titleRT = (RectTransform)titleText.rectTransform;
            titleRT.anchorMin = new Vector2(0f, 1f);
            titleRT.anchorMax = new Vector2(0f, 1f);
            titleRT.pivot = new Vector2(0f, 1f);
            titleRT.anchoredPosition = new Vector2(70f, -36f);
            titleRT.sizeDelta = new Vector2(1400f, 56f);
            titleText.alignment = TextAlignmentOptions.Left;

            chapterText = AddText(transform, font, "", 30f);
            var chapterRT = (RectTransform)chapterText.rectTransform;
            chapterRT.anchorMin = new Vector2(0f, 1f);
            chapterRT.anchorMax = new Vector2(0f, 1f);
            chapterRT.pivot = new Vector2(0f, 1f);
            chapterRT.anchoredPosition = new Vector2(72f, -96f);
            chapterRT.sizeDelta = new Vector2(1400f, 84f);
            chapterText.alignment = TextAlignmentOptions.Left;

            descText = AddText(transform, font, "", 22f);
            var descRT = (RectTransform)descText.rectTransform;
            descRT.anchorMin = new Vector2(0f, 0f);
            descRT.anchorMax = new Vector2(0f, 0f);
            descRT.pivot = new Vector2(0f, 0f);
            descRT.anchoredPosition = new Vector2(70f, 150f);
            descRT.sizeDelta = new Vector2(1700f, 40f);
            descText.alignment = TextAlignmentOptions.Left;

            // ---- 中央预览区 ----
            var previewArea = new GameObject("PreviewArea", typeof(RectTransform));
            previewArea.transform.SetParent(transform, false);
            var areaRT = (RectTransform)previewArea.transform;
            areaRT.anchorMin = Vector2.zero;
            areaRT.anchorMax = Vector2.one;
            areaRT.offsetMin = new Vector2(80f, 150f);
            areaRT.offsetMax = new Vector2(-80f, -130f);

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

            // ---- 底部章节目录条 ----
            var barGo = new GameObject("ChapterBar", typeof(RectTransform));
            barGo.transform.SetParent(transform, false);
            chapterBar = (RectTransform)barGo.transform;
            var barRT = (RectTransform)chapterBar.transform;
            barRT.anchorMin = new Vector2(0.5f, 0f);
            barRT.anchorMax = new Vector2(0.5f, 0f);
            barRT.pivot = new Vector2(0.5f, 0.5f);
            barRT.anchoredPosition = new Vector2(0f, 60f);

            // 章节悬浮提示：鼠标悬停步骤按钮时显示该章详细说明
            var tipGo = new GameObject("ChapterTip", typeof(RectTransform));
            tipGo.transform.SetParent(transform, false);
            chapterTipPanel = (RectTransform)tipGo.transform;
            var tipRT = (RectTransform)chapterTipPanel.transform;
            tipRT.anchorMin = new Vector2(0.5f, 0f);
            tipRT.anchorMax = new Vector2(0.5f, 0f);
            tipRT.pivot = new Vector2(0.5f, 0.5f);
            tipRT.anchoredPosition = new Vector2(0f, 140f);
            tipRT.sizeDelta = new Vector2(560f, 56f);
            var tipImg = tipGo.AddComponent<Image>();
            tipImg.sprite = PonderSprites.Rounded;
            tipImg.type = Image.Type.Sliced;
            tipImg.color = new Color(0.04f, 0.05f, 0.06f, 0.95f);
            tipImg.raycastTarget = false;
            chapterTip = AddText(tipGo.transform, font, "", 22f);
            var tipTextRT = (RectTransform)chapterTip.rectTransform;
            Stretch(tipTextRT);
            chapterTip.alignment = TextAlignmentOptions.Center;
            tipGo.SetActive(false);

            prevBtn = AddButton(transform, font, "◀", ButtonColor, 30f);
            var prevRT = (RectTransform)prevBtn;
            prevRT.anchorMin = new Vector2(0.5f, 0f);
            prevRT.anchorMax = new Vector2(0.5f, 0f);
            prevRT.pivot = new Vector2(0.5f, 0.5f);
            prevRT.anchoredPosition = new Vector2(-270f, 48f);
            prevRT.sizeDelta = new Vector2(90f, 52f);

            nextBtn = AddButton(transform, font, "▶", ButtonColor, 30f);
            var nextRT = (RectTransform)nextBtn;
            nextRT.anchorMin = new Vector2(0.5f, 0f);
            nextRT.anchorMax = new Vector2(0.5f, 0f);
            nextRT.pivot = new Vector2(0.5f, 0.5f);
            nextRT.anchoredPosition = new Vector2(270f, 48f);
            nextRT.sizeDelta = new Vector2(90f, 52f);

            closeBtn = AddButton(transform, font, "✕", CloseColor, 36f);
            ((RectTransform)closeBtn).anchorMin = new Vector2(1f, 1f);
            ((RectTransform)closeBtn).anchorMax = new Vector2(1f, 1f);
            ((RectTransform)closeBtn).pivot = new Vector2(1f, 1f);
            ((RectTransform)closeBtn).anchoredPosition = new Vector2(-32f, -32f);
            ((RectTransform)closeBtn).sizeDelta = new Vector2(44f, 44f);
            closeHover = closeBtn.GetComponent<Image>();

            backBtn = AddButton(transform, font, PonderLang.Get("ui.backToList", "◀ 场景列表"), ButtonColor, 24f);
            var backRT = (RectTransform)backBtn;
            backRT.anchorMin = new Vector2(0f, 0f);
            backRT.anchorMax = new Vector2(0f, 0f);
            backRT.pivot = new Vector2(0f, 0f);
            backRT.anchoredPosition = new Vector2(70f, 30f);
            backRT.sizeDelta = new Vector2(180f, 44f);
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

        public void Show(List<PonderSceneDef> scenes)
        {
            gameObject.SetActive(true);
            _dragActive = false;
            SuppressEditorInput();
            CaptureBackground();
            if (scenes == null || scenes.Count == 0)
            {
                Close();
                return;
            }
            if (scenes.Count == 1)
            {
                entryScenes.Clear();
                OpenScene(scenes[0]);
                return;
            }
            ShowEntry(scenes);
        }

        /// <summary>
        /// Ponder 打开期间禁用 scnEditor 整 GameObject：scnEditor.Update 用 Input.* 读
        /// raw input 拖动 camera，同 GameObject 上其他 MonoBehaviour 也会继续处理 input；
        /// 仅 enabled=false 不够，必须 SetActive(false) 才能彻底停掉视角/砖块/装饰的拖动。
        /// LateUpdate 也不再刷新 levelEditorCanvas.enabled / FloorMesh，Ponder 沙盒
        /// 自己有 RenderTexture + FloorMesh，不依赖 scnEditor 行为。
        /// </summary>
        private void SuppressEditorInput()
        {
            if (_suppressedEditor != null)
            {
                return;
            }
            var ed = ADOBase.editor;
            if (ed == null)
            {
                return;
            }
            _suppressedEditor = ed;
            _suppressedEditorWasEnabled = ed.enabled;
            ed.enabled = false;
            _suppressedEditorGo = ed.gameObject;
            _suppressedGoWasActive = ed.gameObject.activeSelf;
            if (_suppressedGoWasActive)
            {
                ed.gameObject.SetActive(false);
            }
            if (Main.Settings != null && Main.Settings.enableDebugLogs)
            {
                Main.Handler?.Log($"Ponder: suppressed scnEditor GO (wasEnabled={_suppressedEditorWasEnabled}, wasActive={_suppressedGoWasActive})");
            }
        }

        /// <summary>恢复 scnEditor 整 GameObject 之前的状态，Ponder 关闭时调用。</summary>
        private void RestoreEditorInput()
        {
            if (_suppressedEditor == null)
            {
                return;
            }
            if (_suppressedGoWasActive && _suppressedEditorGo != null)
            {
                _suppressedEditorGo.SetActive(true);
            }
            _suppressedEditor.enabled = _suppressedEditorWasEnabled;
            _suppressedEditor = null;
            _suppressedEditorGo = null;
        }

        private void OpenScene(PonderSceneDef scene)
        {
            HideEntry();
            _scene = scene;
            _chapterIndex = 0;
            _preview.SetFont(font);
            if (!_preview.Build(scene))
            {
                Main.Handler?.Error("PonderSceneView: preview build failed");
                return;
            }
            // 收集场景里的静态 edits (设计师写在 JSON 里的) 入栈；运行时的拖动 edits 由 RecordEdit 加进来。
            _editStack.Clear();
            CollectStaticEdits(scene);
            if (previewImage != null && _preview.Texture != null)
            {
                previewImage.texture = _preview.Texture;
            }
            ApplySceneText();
            RefreshChapterBar();
            if (chapterText != null)
            {
                var ch = _scene.ChapterAt(0);
                chapterText.text = ch != null
                    ? $"<size=0.9em><color={Cyan}>{ChapterLabel(1, _scene.Chapters.Count)}</color></size>\n" +
                      $"<size=1.0em>{ch.Text}</size>"
                    : string.Empty;
            }
            SetSceneMode(true);
            _logic = new PonderSceneLogic(_preview, scene);
            _open = true;

            // 默认整体取景：自动把当前章节的全部内容（砖块+装饰+指点）放进视野
            _preview.FitTrack();

            if (scene.Logic.Count > 0)
            {
                _logic.Play(scene.Logic, PlayCurrentChapterLogic);
            }
            else
            {
                PlayCurrentChapterLogic();
            }
        }

        private void ShowEntry(List<PonderSceneDef> scenes)
        {
            var copy = new List<PonderSceneDef>(scenes);
            entryScenes.Clear();
            entryScenes.AddRange(copy);
            if (entryRoot == null)
            {
                BuildEntry();
            }
            entryRoot!.SetActive(true);

            var active = Mathf.Min(entryScenes.Count, entryButtons.Count);
            if (entryPanel != null)
            {
                entryPanel.sizeDelta = new Vector2(700f, 40f + active * 82f);
            }
            for (var i = 0; i < entryButtons.Count; i++)
            {
                var btn = entryButtons[i];
                if (i < entryScenes.Count)
                {
                    var t = btn.GetComponentInChildren<TMP_Text>();
                    if (t != null)
                    {
                        t.enableWordWrapping = true;
                        t.alignment = TextAlignmentOptions.Center;
                        var title = entryScenes[i].Title;
                        var desc = entryScenes[i].Description;
                        t.text = string.IsNullOrEmpty(desc)
                            ? $"<size=1.1em><b>{title}</b></size>"
                            : $"<size=1.1em><b>{title}</b></size>\n<size=0.78em><color={Gray}>{desc}</color></size>";
                    }
                    btn.gameObject.SetActive(true);
                }
                else
                {
                    btn.gameObject.SetActive(false);
                }
            }
            SetSceneMode(false);
            _open = true;
        }

        private void BuildEntry()
        {
            entryRoot = new GameObject("EntryList", typeof(RectTransform));
            entryRoot.transform.SetParent(transform, false);
            var rootRT = (RectTransform)entryRoot.transform;
            Stretch(rootRT);

            var panel = new GameObject("Panel", typeof(RectTransform));
            panel.transform.SetParent(entryRoot.transform, false);
            entryPanel = (RectTransform)panel.transform;
            entryPanel.anchorMin = new Vector2(0.5f, 0.5f);
            entryPanel.anchorMax = new Vector2(0.5f, 0.5f);
            entryPanel.pivot = new Vector2(0.5f, 0.5f);
            entryPanel.anchoredPosition = Vector2.zero;
            entryPanel.sizeDelta = new Vector2(700f, 360f);
            // 灰黑容器去掉：毛玻璃背景自带层次，不再需要深色面板，空间显得更大。
            // 保留 Panel 作为子元素（标题/按钮）的布局锚点，但不画任何底。 

            var header = AddText(panel.transform, font, $"<size=1.2em><color=#5FE0FF><b>{PonderLang.Get("ui.selectScene", "选择演示场景")}</b></color></size>", 26f);
            var headerRT = (RectTransform)header.rectTransform;
            headerRT.anchorMin = new Vector2(0.5f, 1f);
            headerRT.anchorMax = new Vector2(0.5f, 1f);
            headerRT.pivot = new Vector2(0.5f, 1f);
            headerRT.anchoredPosition = new Vector2(0f, -16f);
            headerRT.sizeDelta = new Vector2(620f, 40f);

            for (var i = 0; i < 8; i++)
            {
                var btn = AddButton(panel.transform, font, "", ButtonColor, 24f);
                var rt = (RectTransform)btn;
                rt.anchorMin = new Vector2(0.5f, 1f);
                rt.anchorMax = new Vector2(0.5f, 1f);
                rt.pivot = new Vector2(0.5f, 1f);
                rt.anchoredPosition = new Vector2(0f, -66f - i * 82f);
                rt.sizeDelta = new Vector2(640f, 74f);
                entryButtons.Add(rt);
            }
        }

        private void HideEntry()
        {
            if (entryRoot != null)
            {
                entryRoot.SetActive(false);
            }
        }

        /// <summary>场景模式：显示/隐藏预览相关控件（Create 布局：目录条 + 导航 + 返回）。</summary>
        private void SetSceneMode(bool inScene)
        {
            if (prevBtn != null)
            {
                prevBtn.gameObject.SetActive(inScene);
            }
            if (nextBtn != null)
            {
                nextBtn.gameObject.SetActive(inScene);
            }
            if (chapterBar != null)
            {
                chapterBar.gameObject.SetActive(inScene && _scene != null && _scene.Chapters.Count > 0);
            }
            if (chapterTipPanel != null && !inScene)
            {
                chapterTipPanel.gameObject.SetActive(false);
            }
            if (backBtn != null)
            {
                backBtn.gameObject.SetActive(inScene && entryScenes.Count > 1);
            }
        }

        private void PlayCurrentChapterLogic()
        {
            if (_logic == null || _scene == null)
            {
                return;
            }
            _logic.Stop();
            _preview.ResetTransforms();
            var chapter = _scene.ChapterAt(_chapterIndex);
            _preview.ShowNotes(chapter?.Notes);
            // 在 ResetTransforms 之后、Logic 之前 replay 栈中所有属于该章节及之前章节的 edits，
            // 这样 Logic 触发的视觉变化叠加在 edits 已应用的状态上。
            ReplayEditsForChapter(_chapterIndex);
            // 重放后取景自动适配本章全部内容（砖块+装饰+指点），由章节内容本身决定相机
            _preview.FitTrack();
            if (chapter != null && chapter.Logic.Count > 0)
            {
                _logic.Play(chapter.Logic);
            }
        }

        // ================= 自由编辑栈 =================

        /// <summary>把一条 edit 立即作用到 Ponder 沙盒并入栈。沙盒内拖动产生的 edits 走这里。</summary>
        public void RecordEdit(PonderEdit edit)
        {
            if (edit == null)
            {
                return;
            }
            _editStack.Add(edit);
            _preview.ApplyEdit(edit);
        }

        /// <summary>从 base 状态重新 apply 栈中所有 Chapter &lt;= chapterIndex 的 edits。Chapter=-1 的 edit 视为全程生效。</summary>
        private void ReplayEditsForChapter(int chapterIndex)
        {
            _preview.ResetEdits();
            for (var i = 0; i < _editStack.Count; i++)
            {
                var e = _editStack[i];
                if (e.Chapter < 0 || e.Chapter <= chapterIndex)
                {
                    _preview.ApplyEdit(e);
                }
            }
        }

        /// <summary>关闭 Ponder 时调用：清空栈并把 Ponder 沙盒重置回 Build 后的基准态。</summary>
        private void RollbackAll()
        {
            _editStack.Clear();
            _preview.ResetEdits();
        }

        /// <summary>把 scene 顶层 / 各 chapter 内 writes "edits" 字段的 JSON 解析入栈。</summary>
        private void CollectStaticEdits(PonderSceneDef scene)
        {
            // 顶层 edits (Chapter=-1 视为全程生效)
            foreach (var e in ReadEditsFromRaw(scene.Folder))
            {
                _editStack.Add(e);
            }
            // 每个 chapter 的 edits (字段写在 chapter 内的 "edits"，或单独 chapter 文件夹里的 edits.json)
            if (scene.Chapters != null)
            {
                for (var i = 0; i < scene.Chapters.Count; i++)
                {
                    var chEdits = ReadEditsFromRaw(Path.Combine(scene.Folder, "chapter_" + (i + 1).ToString()));
                    foreach (var e in chEdits)
                    {
                        if (e.Chapter < 0)
                        {
                            e.Chapter = i;
                        }
                        _editStack.Add(e);
                    }
                }
            }
        }
        /// <summary>从场景文件夹里读 "edits.json"（数组，每项一个 PonderEdit）。缺省返回空。</summary>
        private static List<PonderEdit> ReadEditsFromRaw(string folder)
        {
            var result = new List<PonderEdit>();
            try
            {
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                {
                    return result;
                }
                var path = Path.Combine(folder, "edits.json");
                if (!File.Exists(path))
                {
                    return result;
                }
                var list = PonderJson.GetList(Json.Deserialize(File.ReadAllText(path)));
                if (list == null)
                {
                    return result;
                }
                foreach (var item in list)
                {
                    var d = PonderJson.GetDict(item);
                    if (d == null)
                    {
                        continue;
                    }
                    var edit = new PonderEdit
                    {
                        Kind = PonderJson.GetString(d, "kind"),
                        Chapter = PonderJson.GetInt(d, "chapter", -1),
                        At = PonderJson.GetFloat(d, "at", -1f),
                        Value = ParseV2(d.GetValueOrDefault("value")),
                        ColorValue = ParseColor(d.GetValueOrDefault("color")),
                        IntValue = PonderJson.GetInt(d, "int"),
                    };
                    if (PonderJson.GetDict(d.GetValueOrDefault("target")) is { } t)
                    {
                        ParseEditTarget(t, edit.Target);
                    }
                    if (edit.Kind.Length > 0)
                    {
                        result.Add(edit);
                    }
                }
            }
            catch (Exception ex)
            {
                Main.Handler?.Error($"Ponder: read edits from {folder} failed\n{ex}");
            }
            return result;
        }

        /// <summary>解析 PonderEdit.target 字段到 PonderSelector (简化版，不支持 type=event/setting/decoration/floor 简写)。</summary>
        private static void ParseEditTarget(Dictionary<string, object> dict, PonderSelector selector)
        {
            selector.Name = PonderJson.GetString(dict, "name");
            selector.NameContains = PonderJson.GetString(dict, "nameContains");
            selector.Tag = PonderJson.GetString(dict, "tag");
            selector.DecoTag = PonderJson.GetString(dict, "decoTag");
            selector.Component = PonderJson.GetString(dict, "component");
            selector.Layer = PonderJson.GetString(dict, "layer");
            selector.Event = PonderJson.GetString(dict, "event");
            selector.Setting = PonderJson.GetString(dict, "setting");
            selector.Image = PonderJson.GetString(dict, "image");
            selector.Floor = PonderJson.GetBool(dict, "floor", false);
            selector.Tile = PonderJson.GetInt(dict, "tile");
        }

        private static Vector2 ParseV2(object? v)
        {
            if (PonderJson.GetList(v) is { } list && list.Count >= 2)
            {
                return new Vector2(
                    (float)Convert.ToDouble(list[0]),
                    (float)Convert.ToDouble(list[1]));
            }
            return Vector2.zero;
        }

        private static Color ParseColor(object? v)
        {
            if (v is string s && ColorUtility.TryParseHtmlString(s, out var c))
            {
                return c;
            }
            if (PonderJson.GetList(v) is { } list && list.Count >= 3)
            {
                return new Color(
                    Mathf.Clamp01((float)Convert.ToDouble(list[0])),
                    Mathf.Clamp01((float)Convert.ToDouble(list[1])),
                    Mathf.Clamp01((float)Convert.ToDouble(list[2])),
                    list.Count >= 4 ? Mathf.Clamp01((float)Convert.ToDouble(list[3])) : 1f);
            }
            return Color.white;
        }

        private void ApplySceneText()
        {
            if (_scene == null)
            {
                return;
            }
            if (titleText != null)
            {
                titleText.text = $"<size=1.5em><color={Cyan}><b>{_scene.Title}</b></color></size>";
            }
            if (descText != null)
            {
                descText.text = string.IsNullOrEmpty(_scene.Description)
                    ? string.Empty
                    : $"<size=0.9em><color={Gray}>{_scene.Description}</color></size>";
            }
        }

        private static string ChapterLabel(int index, int count)
        {
            var fmt = PonderLang.Get("ui.chapter", "第 {index} / {count} 章");
            return fmt.Replace("{index}", index.ToString()).Replace("{count}", count.ToString());
        }

        private void ApplyChapter()
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
                        $"<size=0.9em><color={Cyan}>{ChapterLabel(_chapterIndex + 1, _scene.Chapters.Count)}</color></size>\n" +
                        $"<size=1.0em>{chapter.Text}</size>";
                }
                else
                {
                    chapterText.text = string.Empty;
                }
            }
            RefreshChapterBar();
        }

        /// <summary>重建底部步骤条：最多显示 3 项（上一个、当前、下一个），按钮只显示 [n]，详细说明靠悬浮提示。</summary>
        private void RefreshChapterBar()
        {
            if (chapterBar == null)
            {
                return;
            }
            foreach (var b in chapterButtons)
            {
                if (b != null)
                {
                    Destroy(b.gameObject);
                }
            }
            chapterButtons.Clear();
            chapterButtonIndexes.Clear();

            if (_scene == null || _scene.Chapters.Count == 0)
            {
                chapterBar.gameObject.SetActive(false);
                return;
            }

            var count = _scene.Chapters.Count;
            var indexes = new List<int>();
            if (_chapterIndex - 1 >= 0)
            {
                indexes.Add(_chapterIndex - 1);
            }
            indexes.Add(_chapterIndex);
            if (_chapterIndex + 1 < count)
            {
                indexes.Add(_chapterIndex + 1);
            }

            const float spacing = 10f;
            var widths = new float[indexes.Count];
            var total = 0f;
            for (var i = 0; i < indexes.Count; i++)
            {
                var idx = indexes[i];
                var btn = AddButton(chapterBar.transform, font, "", idx == _chapterIndex ? ActiveChapterColor : ButtonColor, 20f);
                var rt = (RectTransform)btn;
                var t = btn.GetComponentInChildren<TMP_Text>();
                widths[i] = 56f;
                if (t != null)
                {
                    t.fontSize = 20f;
                    t.overflowMode = TextOverflowModes.Ellipsis;
                    t.enableWordWrapping = false;
                    t.text = idx == _chapterIndex
                        ? $"<color={Cyan}>[{idx + 1}]</color>"
                        : $"[{idx + 1}]";
                    widths[i] = Mathf.Clamp(t.GetPreferredValues(t.text).x + 26f, 48f, 64f);
                }
                chapterButtons.Add(rt);
                chapterButtonIndexes.Add(idx);
                total += widths[i];
            }
            total += spacing * (indexes.Count - 1);

            var x = -total / 2f;
            for (var i = 0; i < chapterButtons.Count; i++)
            {
                var rt = chapterButtons[i];
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = new Vector2(x + widths[i] / 2f, 0f);
                rt.sizeDelta = new Vector2(widths[i], 48f);
                x += widths[i] + spacing;
            }
            chapterBar.gameObject.SetActive(true);

            // 箭头排在三项左右两侧
            if (prevBtn != null)
            {
                prevBtn.anchoredPosition = new Vector2(-total / 2f - 64f, 60f);
            }
            if (nextBtn != null)
            {
                nextBtn.anchoredPosition = new Vector2(total / 2f + 64f, 60f);
            }
        }

        /// <summary>悬浮提示：鼠标悬停步骤按钮时显示该章详细说明，否则隐藏。</summary>
        private void UpdateChapterTip()
        {
            if (chapterTip == null || chapterTipPanel == null || _scene == null)
            {
                return;
            }
            for (var i = 0; i < chapterButtons.Count; i++)
            {
                if (chapterButtons[i].gameObject.activeSelf && IsPointIn(chapterButtons[i]))
                {
                    var idx = chapterButtonIndexes[i];
                    var chapter = _scene.ChapterAt(idx);
                    chapterTip.text = chapter != null ? chapter.Text : string.Empty;
                    var pref = chapterTip.GetPreferredValues(chapterTip.text).x;
                    chapterTipPanel.sizeDelta = new Vector2(Mathf.Clamp(pref + 48f, 240f, 1500f), 56f);
                    chapterTipPanel.gameObject.SetActive(true);
                    return;
                }
            }
            chapterTipPanel.gameObject.SetActive(false);
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

        /// <summary>
        /// 鼠标悬停时把按钮 Image 颜色切到 hover 色，离开时回到常态色。
        /// 仅在颜色真的改变时赋值，避免每帧 set dirty。
        /// </summary>
        private static void UpdateButtonHover(RectTransform rt, Image? img, Color normal, Color hover)
        {
            if (img == null)
            {
                return;
            }
            var want = IsPointIn(rt) ? hover : normal;
            if (img.color != want)
            {
                img.color = want;
            }
        }

        public void Tick()
        {
            if (!_open)
            {
                return;
            }
            UpdateButtonHover(closeBtn, closeHover, CloseColor, CloseColorHover);
            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
            {
                Close();
                return;
            }
            if (Input.GetMouseButtonDown(0))
            {
                if (entryRoot != null && entryRoot.activeSelf)
                {
                    for (var i = 0; i < entryButtons.Count; i++)
                    {
                        if (entryButtons[i].gameObject.activeSelf && IsPointIn(entryButtons[i]))
                        {
                            if (i < entryScenes.Count)
                            {
                                OpenScene(entryScenes[i]);
                            }
                            return;
                        }
                    }
                    Close();
                    return;
                }
                if (IsPointIn(closeBtn))
                {
                    Close();
                }
                else if (IsPointIn(backBtn))
                {
                    ShowEntry(entryScenes);
                }
                else if (IsPointIn(prevBtn))
                {
                    PrevChapter();
                }
                else if (IsPointIn(nextBtn))
                {
                    NextChapter();
                }
                else
                {
                    var hitButton = false;
                    for (var i = 0; i < chapterButtons.Count; i++)
                    {
                        if (chapterButtons[i].gameObject.activeSelf && IsPointIn(chapterButtons[i]))
                        {
                            var idx = chapterButtonIndexes[i];
                            if (idx != _chapterIndex)
                            {
                                _chapterIndex = idx;
                                ApplyChapter();
                                PlayCurrentChapterLogic();
                            }
                            hitButton = true;
                            break;
                        }
                    }
                    if (!hitButton)
                    {
                        // 点空白处：开始拖拽平移沙盒视角
                        _dragActive = true;
                        _lastDragPos = Input.mousePosition;
                    }
                    return;
                }
            }
            else if (Input.GetMouseButton(0) && _dragActive)
            {
                var cur = Input.mousePosition;
                var deltaPx = cur - _lastDragPos;
                _lastDragPos = cur;
                if (_preview != null && _open && Mathf.Abs(deltaPx.x) + Mathf.Abs(deltaPx.y) > 0.5f)
                {
                    var ortho = _preview.CurrentOrtho;
                    var viewH = (float)Screen.height;
                    if (previewImage != null && previewImage.canvas != null)
                    {
                        var rectH = previewImage.rectTransform.rect.height * previewImage.canvas.scaleFactor;
                        if (rectH > 1f)
                        {
                            viewH = rectH;
                        }
                    }
                    var worldPerPx = 2f * ortho / Mathf.Max(1f, viewH);
                    _preview.PanCamera(new Vector2(-deltaPx.x, -deltaPx.y) * worldPerPx);
                }
            }
            else
            {
                _dragActive = false;
            }
            var wheel = Input.mouseScrollDelta.y;
            if (wheel != 0f && _preview != null && previewImage != null && IsPointIn(previewImage.rectTransform))
            {
                _preview.ZoomBy(Mathf.Pow(0.9f, wheel));
            }
            _logic?.Update(Time.deltaTime);
            _preview.UpdateVisuals(Time.deltaTime);
            UpdateChapterTip();
        }

        public void Close()
        {
            _open = false;
            HideEntry();
            _logic?.Stop();
            _logic = null;
            // 关闭 Ponder 时丢弃所有 edits (静态 + 沙盒内拖动) 并把沙盒回滚到 base 状态。
            RollbackAll();
            _preview.Clear();
            _scene = null;
            RestoreEditorInput();
            if (_bgTexture != null)
            {
                UnityEngine.Object.Destroy(_bgTexture);
                _bgTexture = null;
            }
            if (bgImage != null)
            {
                bgImage.sprite = null;
                bgImage.color = DimColor;
            }
            if (gameObject.activeSelf)
            {
                gameObject.SetActive(false);
            }
        }

        /// <summary>毛玻璃背景：隐藏本画布一帧捕获干净的编辑器画面，缩小 + 多次模糊后贴满全屏。</summary>
        private void CaptureBackground()
        {
            if (bgImage == null)
            {
                return;
            }
            StopAllCoroutines();
            StartCoroutine(CaptureBgRoutine());
        }

        private IEnumerator CaptureBgRoutine()
        {
            var canvas = GetComponent<Canvas>();
            if (canvas == null || bgImage == null)
            {
                yield break;
            }
            canvas.enabled = false;
            yield return new WaitForEndOfFrame();
            Texture2D? full = null;
            try
            {
                var w = Screen.width;
                var h = Screen.height;
                if (w <= 4 || h <= 4)
                {
                    canvas.enabled = true;
                    yield break;
                }
                full = new Texture2D(w, h, TextureFormat.RGB24, false);
                full.ReadPixels(new Rect(0f, 0f, w, h), 0, 0);
                full.Apply();

                var smallW = Mathf.Max(64, w / 6);
                var smallH = Mathf.Max(64, h / 6);
                var src = full.GetPixels();
                var sc = new Color[smallW * smallH];
                for (var y = 0; y < smallH; y++)
                {
                    var sy = Mathf.Min(h - 1, (int)((y + 0.5f) * h / smallH));
                    for (var x = 0; x < smallW; x++)
                    {
                        var sx = Mathf.Min(w - 1, (int)((x + 0.5f) * w / smallW));
                        sc[y * smallW + x] = src[sy * w + sx];
                    }
                }
                var small = new Texture2D(smallW, smallH, TextureFormat.RGB24, false);
                small.SetPixels(sc);
                small.Apply();
                BlurTexture(small, 3, 2);

                if (_bgTexture != null)
                {
                    UnityEngine.Object.Destroy(_bgTexture);
                }
                _bgTexture = small;
                bgImage.sprite = Sprite.Create(small, new Rect(0f, 0f, small.width, small.height), new Vector2(0.5f, 0.5f));
                bgImage.type = Image.Type.Simple;
                bgImage.preserveAspect = false;
                bgImage.color = Color.white;
            }
            catch (Exception ex)
            {
                Main.Handler?.Error("Ponder: background blur capture failed\n" + ex);
            }
            finally
            {
                canvas.enabled = true;
                if (full != null)
                {
                    UnityEngine.Object.Destroy(full);
                }
            }
        }

        /// <summary>CPU 分离式框模糊（先水平再垂直），对低分辨率小图足够快。</summary>
        private static void BlurTexture(Texture2D tex, int radius, int passes)
        {
            var w = tex.width;
            var h = tex.height;
            var src = tex.GetPixels();
            var dst = new Color[src.Length];
            var weights = new float[radius * 2 + 1];
            var sum = 0f;
            for (var i = -radius; i <= radius; i++)
            {
                weights[i + radius] = 1f / (i * i + 1f);
                sum += weights[i + radius];
            }
            for (var i = 0; i < weights.Length; i++)
            {
                weights[i] /= sum;
            }
            for (var pass = 0; pass < passes; pass++)
            {
                for (var y = 0; y < h; y++)
                {
                    for (var x = 0; x < w; x++)
                    {
                        var acc = new Color(0f, 0f, 0f, 0f);
                        for (var k = -radius; k <= radius; k++)
                        {
                            acc += src[y * w + Mathf.Clamp(x + k, 0, w - 1)] * weights[k + radius];
                        }
                        dst[y * w + x] = acc;
                    }
                }
                (dst, src) = (src, dst);
                for (var y = 0; y < h; y++)
                {
                    for (var x = 0; x < w; x++)
                    {
                        var acc = new Color(0f, 0f, 0f, 0f);
                        for (var k = -radius; k <= radius; k++)
                        {
                            acc += src[Mathf.Clamp(y + k, 0, h - 1) * w + x] * weights[k + radius];
                        }
                        dst[y * w + x] = acc;
                    }
                }
                (dst, src) = (src, dst);
            }
            tex.SetPixels(src);
            tex.Apply();
        }
    }
}
