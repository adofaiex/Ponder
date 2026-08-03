using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ponder
{
    public sealed class PonderPreview : IDisposable
    {
        public const int PreviewLayer = 30;

        public RenderTexture? Texture { get; private set; }
        public float WorldSize { get; private set; }
        public bool IsReady { get; private set; }
        public float TileSize => GetTileSize();
        public PonderPreviewContext Context { get; }
        public int TileCount => _tileBases.Count;

        /// <summary>Build 时的基准砖块数（AddTiles 追加的部分不算）。</summary>
        public int BaseTileCount => _baseTileCount;

        private GameObject? _root;
        private GameObject? _cameraGo;
        private Camera? _camera;
        private Bounds _bounds;

        private GameObject? _arrowCanvasGo;
        private Canvas? _arrowCanvas;

        private readonly List<scrFloor?> _tileFloors = new List<scrFloor?>();
        private readonly List<GameObject> _tileObjects = new List<GameObject>();
        private readonly List<Vector3> _tileBases = new List<Vector3>();
        // 每块砖的朝向（exitangle 方向单位向量），用于把 note 偏移转进砖块本地坐标系，
        // 保证 textY 始终垂直于轨道（转弯处不会叠到砖块上）。
        private readonly List<Vector3> _tileForwards = new List<Vector3>();
        private readonly List<Vector3> _tileExtra = new List<Vector3>();
        private readonly List<float> _tileScale = new List<float>();
        private readonly List<float> _tileScaleY = new List<float>();
        private readonly List<float> _tileRot = new List<float>();
        private readonly List<float> _tileOpacity = new List<float>();
        private readonly List<Color> _tileColors = new List<Color>();
        private readonly List<int> _tileBaseStyles = new List<int>();
        private readonly List<float> _tileSpawn = new List<float>();
        private readonly List<float> _tileSpawnDelay = new List<float>();
        private float _spawnClock;
        private float _spawnStagger = 0.15f;

        private Vector3 _globalDelta;
        private float _globalScale = 1f;

        private const float TileSpawnSpeed = 3f;

        // 平滑相机：目标点 + 逐帧追赶（Create 风格 Chaser）
        private Vector3 _cameraFocusPos;
        private int _cameraFocusFloor = -1;
        private float _targetOrtho = 4f;
        private bool _cameraSnapped = true;

        // 沙盒游标：下一块砖落点 + 前一块的 exitangle
        private Vector3 _cursorPos;
        private double _cursorExit = 4.71238898038469;

        // 每块砖的方向数据（angleData，度），用于把下一块砖的方向换算成相对转角。


        // 构建基准态：用于章节循环重放时把运行时追加的砖块/装饰物收回去
        private int _baseTileCount;
        private Vector3 _baseCursorPos;
        private double _baseCursorExit = 4.71238898038469;
        private bool _hasBaseState;

        private readonly List<GameObject> _objects = new List<GameObject>();
        private readonly List<GameObject> _notes = new List<GameObject>();
        private TMP_FontAsset? _font;

        /// <summary>设置文字字体（用于指点元素/文字标签）。</summary>
        public void SetFont(TMP_FontAsset? font)
        {
            _font = font;
        }

        private sealed class DecoObj
        {
            public GameObject? go;
            public Vector3 basePos;
            public Vector3 extra;
            public float baseScale = 1f;
            public int tile;
            public string tag = "";
            public SpriteRenderer? sr;
            public PonderArrowGraphic? graphic;
            public bool spawned;
        }
        private readonly List<DecoObj> _decos = new List<DecoObj>();

        public Vector3 GlobalDelta => _globalDelta;
        public float GlobalScale => _globalScale;

        public Vector3 TileExtraAt(int index)
        {
            return index >= 0 && index < _tileExtra.Count ? _tileExtra[index] : Vector3.zero;
        }

        public float TileScaleAt(int index)
        {
            return index >= 0 && index < _tileScale.Count ? _tileScale[index] : 1f;
        }

        public float TileScaleYAt(int index)
        {
            return index >= 0 && index < _tileScaleY.Count ? _tileScaleY[index] : 1f;
        }

        public float TileRotationAt(int index)
        {
            return index >= 0 && index < _tileRot.Count ? _tileRot[index] : 0f;
        }

        public float TileOpacityAt(int index)
        {
            return index >= 0 && index < _tileOpacity.Count ? _tileOpacity[index] : 1f;
        }

        public Color GetTileColor(int index)
        {
            return index >= 0 && index < _tileColors.Count ? _tileColors[index] : Color.white;
        }

        // ================= 自由编辑 (MC Ponder 风格万物皆可 Ponder) =================

        /// <summary>
        /// 把一次 PonderEdit 作用到当前 Ponder 沙盒上。会修改 _tileExtra/_tileScale 等运行时字段
        /// 并立刻调一次 ApplyTransforms 让 GameObject 同步。Target 解析为沙盒内 tile/deco 索引。
        /// 调用方负责记录 edit 到栈里（PonderSceneView 维护），便于章节切换时 replay / 关闭时 rollback。
        /// </summary>
        public void ApplyEdit(PonderEdit edit)
        {
            if (edit == null || string.IsNullOrEmpty(edit.Kind))
            {
                return;
            }
            switch (edit.Kind)
            {
                case "TileExtra":
                {
                    var i = ResolveTileIndex(edit.Target);
                    if (i >= 0)
                    {
                        _tileExtra[i] = new Vector3(edit.Value.x, edit.Value.y, 0f);
                    }
                    break;
                }
                case "TileScale":
                {
                    var i = ResolveTileIndex(edit.Target);
                    if (i >= 0)
                    {
                        _tileScale[i] = edit.Value.x;
                        _tileScaleY[i] = edit.Value.y;
                    }
                    break;
                }
                case "TileRot":
                {
                    var i = ResolveTileIndex(edit.Target);
                    if (i >= 0)
                    {
                        _tileRot[i] = edit.Value.x;
                    }
                    break;
                }
                case "TileOpacity":
                {
                    var i = ResolveTileIndex(edit.Target);
                    if (i >= 0)
                    {
                        _tileOpacity[i] = Mathf.Clamp01(edit.Value.x);
                        var floor = _tileFloors[i];
                        if (floor != null)
                        {
                            floor.opacity = _tileOpacity[i];
                        }
                    }
                    break;
                }
                case "TileColor":
                {
                    var i = ResolveTileIndex(edit.Target);
                    if (i >= 0 && i < _tileColors.Count)
                    {
                        _tileColors[i] = edit.ColorValue;
                    }
                    break;
                }
                case "DecoExtra":
                {
                    var deco = ResolveDeco(edit.Target);
                    if (deco != null)
                    {
                        deco.extra = new Vector3(edit.Value.x, edit.Value.y, 0f);
                    }
                    break;
                }
                case "DecoScale":
                {
                    var deco = ResolveDeco(edit.Target);
                    if (deco != null)
                    {
                        deco.baseScale = Mathf.Max(0.01f, edit.Value.x);
                    }
                    break;
                }
                case "DecoRot":
                {
                    var deco = ResolveDeco(edit.Target);
                    if (deco != null)
                    {
                        if (deco.go != null)
                        {
                            deco.go.transform.rotation = Quaternion.Euler(0f, 0f, edit.Value.x);
                        }
                    }
                    break;
                }
                case "DecoDepth":
                {
                    var deco = ResolveDeco(edit.Target);
                    if (deco != null && deco.sr != null)
                    {
                        deco.sr.sortingOrder = edit.IntValue;
                    }
                    break;
                }
                default:
                    Main.Handler?.Warning($"Ponder: unknown edit kind '{edit.Kind}'");
                    return;
            }
            ApplyTransforms();
        }

        /// <summary>把 Ponder 沙盒的所有运行时状态重置回基准态 (Build 后的状态)，丢弃所有 edits。</summary>
        public void ResetEdits()
        {
            ResetTransforms();
        }

        /// <summary>按选择器在 Ponder 沙盒内找第一个匹配的砖块索引；找不到返回 -1。</summary>
        private int ResolveTileIndex(PonderSelector sel)
        {
            if (sel == null || !sel.HasCriteria)
            {
                return -1;
            }
            // 显式指定 tile 序号时直接索引 (1-based → 0-based)
            if (sel.Tile > 0)
            {
                var idx = sel.Tile - 1;
                if (idx >= 0 && idx < _tileFloors.Count && MatchesTile(sel, idx))
                {
                    return idx;
                }
                return -1;
            }
            for (var i = 0; i < _tileFloors.Count; i++)
            {
                if (MatchesTile(sel, i))
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>按选择器在 Ponder 沙盒内找第一个匹配的装饰物；找不到返回 null。</summary>
        private DecoObj? ResolveDeco(PonderSelector sel)
        {
            if (sel == null || !sel.HasCriteria)
            {
                return null;
            }
            for (var i = 0; i < _decos.Count; i++)
            {
                var d = _decos[i];
                if (sel.DecoTag.Length > 0 && d.tag != sel.DecoTag)
                {
                    continue;
                }
                if (sel.Tag.Length > 0 && d.tag != sel.Tag)
                {
                    continue;
                }
                if (sel.Floor && d.tile < 0)
                {
                    continue;
                }
                return d;
            }
            return null;
        }

        private bool MatchesTile(PonderSelector sel, int i)
        {
            if (sel.Floor)
            {
                if (i < 0 || i >= _tileFloors.Count || _tileFloors[i] == null)
                {
                    return false;
                }
            }
            if (sel.Name.Length > 0)
            {
                var go = i < _tileObjects.Count ? _tileObjects[i] : null;
                if (go == null || go.name != sel.Name)
                {
                    return false;
                }
            }
            return true;
        }

        // ================= 指点元素（线 + 文字） =================

        private sealed class NoteObj
        {
            public PonderNoteGraphic? graphic;
            public Transform? textTf;
            public RectTransform? boxTf;
            public Vector2 boxSize;
            public int tile = -1;
            public Vector3 textOffset;
            public Vector3 startOffset;
            public Vector3 endOffset;
            // 模式 2 (世界自由定位)：用 worldAnchor/worldTarget 而非砖块基准 + 偏移。
            public bool freePositioned;
            public Vector2 worldAnchor;     // 文字框目标锚点 (世界单位)
            public Vector2 worldTarget;     // 线目标点 (世界单位)
            public Vector2 pivot = new Vector2(0.5f, 0.5f); // RectTransform pivot (0..1)
            /// <summary>为避免 note 文字框相互覆盖, 每帧由碰撞松弛推开的实际偏移 (世界单位). 0 = 未推开.</summary>
            public Vector2 shifted;
            /// <summary>本帧碰撞松弛算出的目标偏移, 每帧用 lerp 缓动到 shifted 以避免抖动.</summary>
            public Vector2 targetShifted;
        }
        private readonly List<NoteObj> _noteObjs = new List<NoteObj>();

        public PonderPreview()
        {
            Context = new PonderPreviewContext(this);
        }

        /// <summary>
        /// 展示一章的指点元素：每条 note 画一条从文字指向目标的线（终点带箭头 + 圆点）。
        /// 所有偏移为砖块单位，锚定在 note.Tile 上；线/文字与砖块绑定，砖块移动时逐帧跟随。
        /// </summary>
        public void ShowNotes(List<PonderNote> notes)
        {
            ClearNotes();
            if (notes == null || notes.Count == 0 || _arrowCanvas == null)
            {
                return;
            }
            var tileSize = GetTileSize();
            for (var i = 0; i < notes.Count; i++)
            {
                var note = notes[i];
                if (note.Tile < 0 || note.Tile >= _tileBases.Count)
                {
                    continue;
                }
                CreateNote(note, tileSize);
            }
        }

        public void ClearNotes()
        {
            foreach (var go in _notes)
            {
                if (go != null)
                {
                    UnityEngine.Object.Destroy(go);
                }
            }
            _notes.Clear();
            _noteObjs.Clear();
        }

        private void CreateNote(PonderNote note, float tileSize)
        {
            if (_arrowCanvas == null || _root == null)
            {
                return;
            }
            ColorUtility.TryParseHtmlString(note.Color, out var col);

            // 模式判断：WorldPos.x ≥ 0 表示设计师给了绝对世界坐标 + RectTransform pivot，
            // 进入模式 2 (世界自由定位)；否则走模式 1 (砖块相对)。
            var freePositioned = note.WorldPos.x >= 0f;
            var pivot = note.Pivot;
            // 缺省 pivot 中心化 (0.5, 0.5)，其他值让 worldPos 对应文字框的某角/边。
            if (pivot.x < 0f || pivot.x > 1f) pivot.x = 0.5f;
            if (pivot.y < 0f || pivot.y > 1f) pivot.y = 0.5f;

            // 模式 1 的偏移：砖块单位 × tileSize，按砖块朝向转进本地系。
            var textOffset = RotateNoteOffset(new Vector3(note.TextX, note.TextY, 0f) * tileSize, note.Tile);
            var targetOffset = RotateNoteOffset(new Vector3(note.TargetX, note.TargetY, 0f) * tileSize, note.Tile);
            var startOffset = float.IsNaN(note.LineStartX)
                ? textOffset
                : RotateNoteOffset(new Vector3(note.LineStartX, note.LineStartY, 0f) * tileSize, note.Tile);
            var endOffset = float.IsNaN(note.LineEndX)
                ? targetOffset
                : RotateNoteOffset(new Vector3(note.LineEndX, note.LineEndY, 0f) * tileSize, note.Tile);

            var go = new GameObject("Note", typeof(RectTransform));
            go.transform.SetParent(_arrowCanvas.transform, false);
            SetLayer(go, PreviewLayer);

            var line = go.AddComponent<PonderNoteGraphic>();
            line.raycastTarget = false;
            line.color = col;
            line.thickness = 0.06f * tileSize;

            var obj = new NoteObj
            {
                graphic = line,
                tile = note.Tile,
                textOffset = textOffset,
                startOffset = startOffset,
                endOffset = endOffset,
                freePositioned = freePositioned,
                worldAnchor = freePositioned ? note.WorldPos : Vector2.zero,
                worldTarget = freePositioned && note.WorldTarget.x > -999f ? note.WorldTarget : Vector2.zero,
                pivot = pivot
            };
            _notes.Add(go);

            if (note.ShowText && _font != null && note.Text.Length > 0)
            {
                // Ponder-MC 风格：文字放在深色圆角底框里，框即箭头线的尾部；
                // 线从框的边缘引出指向目标。文字用 3D TextMeshPro，挂在指点线的 transform 下。
                var textGo = new GameObject("NoteText");
                textGo.transform.SetParent(go.transform, false);
                SetLayer(textGo, PreviewLayer);
                var t = textGo.AddComponent<TextMeshPro>();
                t.font = _font;
                t.fontSize = 0.34f * tileSize;
                t.color = col;
                t.alignment = TextAlignmentOptions.Center;
                t.enableWordWrapping = false;
                t.overflowMode = TextOverflowModes.Overflow;
                t.richText = true;
                t.isOrthographic = true;
                t.text = note.Text;
                t.ForceMeshUpdate(true);
                obj.textTf = textGo.transform;
                _notes.Add(textGo);

                // 文字底框：比文字宽一点点、高一点点的圆角半透明深色框。
                var rendered = t.GetRenderedValues();
                var padX = 0.22f * tileSize;
                var padY = 0.16f * tileSize;
                obj.boxSize = new Vector2(rendered.x + padX * 2f, rendered.y + padY * 2f);

                var boxGo = new GameObject("NoteBox", typeof(RectTransform));
                boxGo.transform.SetParent(go.transform, false);
                SetLayer(boxGo, PreviewLayer);
                var box = boxGo.AddComponent<PonderBoxGraphic>();
                box.cornerRadius = 0.1f * tileSize;
                box.color = new Color(0f, 0f, 0f, 0.62f);
                box.raycastTarget = false;
                obj.boxTf = (RectTransform)boxGo.transform;
                obj.boxTf.sizeDelta = obj.boxSize;
                // 模式 2：box pivot 用 note.Pivot (默认 0.5, 0.5)，让 worldPos 对应框的指定角。
                // 模式 1：保持中心 pivot，UpdateNote 用 boxAnchor 居中放置。
                obj.boxTf.pivot = pivot;
                boxGo.transform.SetAsFirstSibling();
                _notes.Add(boxGo);
            }

            _noteObjs.Add(obj);
            UpdateNote(obj, tileSize);
        }

        /// <summary>逐帧刷新指点元素：先做文字框碰撞排斥，再让线/文字/底框按推开后的位置对齐。</summary>
        private void UpdateNotes()
        {
            if (_noteObjs.Count == 0)
            {
                return;
            }
            var tileSize = GetTileSize();
            ResolveBoxCollisions(tileSize);
            foreach (var n in _noteObjs)
            {
                UpdateNote(n, tileSize);
            }
        }

        /// <summary>
        /// Note 文字框之间的 AABB 碰撞排斥（Ponder-MC 风格：固定到屏幕的标签不需要，但
        /// 我们的 note 是场景内指点，挨着砖块，挤在一起就会重叠）。
        /// 思路：先算每条 note 的目标锚点 (模式 1 = 砖块基准 + startOffset；模式 2 = worldAnchor)，
        /// 再迭代松弛把所有重叠的框沿"重叠最小轴"互相推开；为避免飞出场景，限制单轴最大推出量；
        /// 为避免抖动，shifted 沿用上帧值并用 lerp 缓动到本帧目标。
        /// </summary>
        private void ResolveBoxCollisions(float tileSize)
        {
            var n = _noteObjs.Count;
            if (n < 2)
            {
                if (n == 1)
                {
                    _noteObjs[0].targetShifted = Vector2.zero;
                    _noteObjs[0].shifted = Vector2.Lerp(_noteObjs[0].shifted, Vector2.zero, 0.35f);
                }
                return;
            }

            var anchors = new Vector2[n];
            var sizes = new Vector2[n];
            var has = new bool[n];
            for (var i = 0; i < n; i++)
            {
                var obj = _noteObjs[i];
                if (obj.boxSize.x <= 0.0001f || obj.boxSize.y <= 0.0001f)
                {
                    has[i] = false;
                    continue;
                }
                has[i] = true;
                // 模式 1：锚点跟随砖块基准 + startOffset，砖块移动时也跟着走。
                // 模式 2：锚点直接用 worldAnchor，再把 pivot 偏移折回 box 中心用于碰撞 AABB。
                Vector2 a;
                if (obj.freePositioned)
                {
                    a = obj.worldAnchor;
                }
                else
                {
                    var p = FixedTilePos(obj.tile) + obj.startOffset;
                    a = new Vector2(p.x, p.y);
                }
                var pivotOffset = (new Vector2(0.5f, 0.5f) - obj.pivot) * obj.boxSize;
                anchors[i] = a + pivotOffset;
                sizes[i] = obj.boxSize;
            }

            var target = new Vector2[n];
            // 4 次迭代松弛足以分离大多数挤压；过多次数在大场景下反而抖动
            const int Iterations = 4;
            // 框间保留一点空气，不让两个文字框"贴"在一起
            var pad = 0.05f * tileSize;
            // 单轴最大推出量：超过这个距离后，文字会离砖块太远（设计师写的 textX 通常 1.4~1.7），
            // 所以宁可保留部分重叠也不要飞出场景。
            var maxShift = 0.9f * tileSize;

            for (var it = 0; it < Iterations; it++)
            {
                for (var i = 0; i < n; i++)
                {
                    if (!has[i])
                    {
                        continue;
                    }
                    var posI = anchors[i] + target[i];
                    var halfI = sizes[i] * 0.5f;
                    for (var j = i + 1; j < n; j++)
                    {
                        if (!has[j])
                        {
                            continue;
                        }
                        var posJ = anchors[j] + target[j];
                        var halfJ = sizes[j] * 0.5f;
                        var d = posJ - posI;
                        var overlapX = (halfI.x + halfJ.x) - Mathf.Abs(d.x);
                        var overlapY = (halfI.y + halfJ.y) - Mathf.Abs(d.y);
                        if (overlapX > 0f && overlapY > 0f)
                        {
                            // 沿较小重叠轴方向推开：分离得最快、推开量最小
                            if (overlapX < overlapY)
                            {
                                var dirX = d.x >= 0f ? 1f : -1f;
                                var push = overlapX * 0.5f + pad * 0.5f;
                                target[i].x -= dirX * push;
                                target[j].x += dirX * push;
                            }
                            else
                            {
                                var dirY = d.y >= 0f ? 1f : -1f;
                                var push = overlapY * 0.5f + pad * 0.5f;
                                target[i].y -= dirY * push;
                                target[j].y += dirY * push;
                            }
                        }
                    }
                }
            }

            for (var i = 0; i < n; i++)
            {
                if (!has[i])
                {
                    continue;
                }
                var t = target[i];
                if (t.x > maxShift) t.x = maxShift; else if (t.x < -maxShift) t.x = -maxShift;
                if (t.y > maxShift) t.y = maxShift; else if (t.y < -maxShift) t.y = -maxShift;
                var obj = _noteObjs[i];
                obj.targetShifted = t;
                // 阻尼：避免 box 突然跳一大段；lerp 因子越大越快收敛，越小越平滑
                obj.shifted = Vector2.Lerp(obj.shifted, t, 0.35f);
            }
        }

        private void UpdateNote(NoteObj n, float tileSize)
        {
            if (n.graphic == null)
            {
                return;
            }
            // 模式 1 (砖块相对)：文字锚在砖块基准位置 + startOffset + 碰撞推开 shifted；
            // 线终点跟随砖块当前位置 (PositionTrack 等动画会移动砖块，线跟着拉长/转向)。
            // 模式 2 (世界自由)：文字和线终点都是绝对世界坐标，砖块移动不影响。
            Vector3 boxAnchor;
            Vector3 movingEnd;
            if (n.freePositioned)
            {
                boxAnchor = new Vector3(
                    n.worldAnchor.x + n.shifted.x,
                    n.worldAnchor.y + n.shifted.y,
                    0f);
                movingEnd = new Vector3(n.worldTarget.x, n.worldTarget.y, 0f);
            }
            else
            {
                var fixedStart = FixedTilePos(n.tile) + n.startOffset;
                boxAnchor = new Vector3(
                    fixedStart.x + n.shifted.x,
                    fixedStart.y + n.shifted.y,
                    fixedStart.z);
                movingEnd = CurrentTilePos(n.tile) + n.endOffset;
            }

            // 线从底框边缘引出：朝目标方向的框边界点作为线起点，避免线从文字中间穿过。
            // 用 box 中心而不是 boxAnchor(pivot 点) 算边界，再偏移回 boxAnchor。
            var boxCenter = boxAnchor + new Vector3(
                (0.5f - n.pivot.x) * n.boxSize.x,
                (0.5f - n.pivot.y) * n.boxSize.y,
                0f);
            var lineStart = boxCenter;
            var delta = movingEnd - boxCenter;
            var len = delta.magnitude;
            if (n.boxTf != null && n.boxSize.x > 0.0001f && len > 0.0001f)
            {
                var dir = delta / len;
                var halfW = n.boxSize.x * 0.5f;
                var halfH = n.boxSize.y * 0.5f;
                var ax = Mathf.Abs(dir.x) > 0.0001f ? halfW / Mathf.Abs(dir.x) : float.MaxValue;
                var ay = Mathf.Abs(dir.y) > 0.0001f ? halfH / Mathf.Abs(dir.y) : float.MaxValue;
                lineStart += dir * Mathf.Min(ax, ay);
            }

            var min = Vector3.Min(lineStart, movingEnd);
            var max = Vector3.Max(lineStart, movingEnd);
            var center = (min + max) * 0.5f;
            n.graphic.transform.position = center;
            n.graphic.start = new Vector2(lineStart.x - center.x, lineStart.y - center.y);
            n.graphic.end = new Vector2(movingEnd.x - center.x, movingEnd.y - center.y);
            n.graphic.SetVerticesDirty();
            if (n.boxTf != null)
            {
                // box pivot 已经按 note.Pivot 设好，直接把 pivot 点放到 boxAnchor。
                n.boxTf.position = boxAnchor;
                n.boxTf.localScale = Vector3.one;
            }
            if (n.textTf != null)
            {
                // 文字居中于底框 (RectTransform 居中)，z 略靠前避免被砖块遮挡；
                // 缩放只跟回溯系数，不受轨道 scale/全局缩放影响。
                n.textTf.position = new Vector3(boxCenter.x, boxCenter.y, boxAnchor.z - 0.2f);
                n.textTf.localScale = Vector3.one;
            }
        }

        /// <summary>砖块基准位置（不含全局位移/该砖额外位移，供指点文字锚定，保证轨道移动时文字不动）。</summary>
        private Vector3 FixedTilePos(int tile)
        {
            if (tile < 0 || tile >= _tileBases.Count)
            {
                return Vector3.zero;
            }
            return _tileBases[tile];
        }

        /// <summary>砖块当前位置 = 基准位置 + 全局位移 + 该砖额外位移（用于指点元素锚定）。</summary>
        private Vector3 CurrentTilePos(int tile)
        {
            if (tile < 0 || tile >= _tileBases.Count)
            {
                return Vector3.zero;
            }
            var p = _tileBases[tile];
            if (tile < _tileExtra.Count)
            {
                p += _globalDelta + _tileExtra[tile];
            }
            return p;
        }

        /// <summary>
        /// 把 note 偏移从砖块本地系转成世界系：x 沿砖块朝向（轨道方向），y 垂直轨道。
        /// 水平轨道上即原样（+X 沿轨道、+Y 朝上），转弯/竖直段会随之旋转，避免文字叠到砖块上。
        /// </summary>
        private Vector3 RotateNoteOffset(Vector3 offset, int tile)
        {
            var fwd = tile >= 0 && tile < _tileForwards.Count
                ? _tileForwards[tile]
                : Vector3.right;
            if (fwd.sqrMagnitude < 0.0001f)
            {
                fwd = Vector3.right;
            }
            var up = new Vector3(-fwd.y, fwd.x, 0f);
            return offset.x * fwd + offset.y * up;
        }

        public void ResetTransforms()
        {
            _globalDelta = Vector3.zero;
            _globalScale = 1f;
            TrimToBaseState();
            for (var i = 0; i < _tileExtra.Count; i++)
            {
                _tileExtra[i] = Vector3.zero;
                _tileScale[i] = 1f;
                _tileRot[i] = 0f;
                _tileOpacity[i] = 1f;
                var floor = i < _tileFloors.Count ? _tileFloors[i] : null;
                if (floor != null)
                {
                    floor.transform.rotation = Quaternion.identity;
                    floor.opacity = 1f;
                }
            }
            ResetTileColors();
            foreach (var deco in _decos)
            {
                deco.extra = Vector3.zero;
                if (deco.sr != null)
                {
                    var c = deco.sr.color;
                    c.a = deco.spawned ? 0f : 1f;
                    deco.sr.color = c;
                }
                if (deco.graphic != null)
                {
                    var c = deco.graphic.color;
                    c.a = deco.spawned ? 0f : 1f;
                    deco.graphic.color = c;
                }
            }
            ApplyTransforms();
        }

        /// <summary>
        /// 章节循环重放：把运行时（AddTiles / AddDecoration）追加的砖块与装饰物销毁，
        /// 回到 Build 时的基准态，避免每次循环不断叠加。
        /// </summary>
        private void TrimToBaseState()
        {
            while (_tileFloors.Count > _baseTileCount)
            {
                var last = _tileFloors[_tileFloors.Count - 1];
                if (last != null)
                {
                    var go = last.gameObject;
                    if (go != null)
                    {
                        _objects.Remove(go);
                        UnityEngine.Object.Destroy(go);
                    }
                }
                _tileFloors.RemoveAt(_tileFloors.Count - 1);
                _tileBases.RemoveAt(_tileBases.Count - 1);
                _tileExtra.RemoveAt(_tileExtra.Count - 1);
                _tileScale.RemoveAt(_tileScale.Count - 1);
                _tileScaleY.RemoveAt(_tileScaleY.Count - 1);
                if (_tileRot.Count > _baseTileCount)
                {
                    _tileRot.RemoveAt(_tileRot.Count - 1);
                }
                if (_tileOpacity.Count > _baseTileCount)
                {
                _tileOpacity.RemoveAt(_tileOpacity.Count - 1);
            }
            if (_tileColors.Count > _baseTileCount)
            {
                _tileColors.RemoveAt(_tileColors.Count - 1);
            }
            if (_tileBaseStyles.Count > _baseTileCount)
            {
                _tileBaseStyles.RemoveAt(_tileBaseStyles.Count - 1);
            }
                if (_tileSpawn.Count > _baseTileCount)
                {
                    _tileSpawn.RemoveAt(_tileSpawn.Count - 1);
                }
                if (_tileSpawnDelay.Count > _baseTileCount)
                {
                    _tileSpawnDelay.RemoveAt(_tileSpawnDelay.Count - 1);
                }
                if (_tileForwards.Count > _baseTileCount)
                {
                    _tileForwards.RemoveAt(_tileForwards.Count - 1);
                }
                if (_tileObjects.Count > _baseTileCount)
                {
                    _tileObjects.RemoveAt(_tileObjects.Count - 1);
                }
            }
            if (_hasBaseState)
            {
                _cursorPos = _baseCursorPos;
                _cursorExit = _baseCursorExit;
            }
            for (var i = _decos.Count - 1; i >= 0; i--)
            {
                if (_decos[i].spawned)
                {
                    var go = _decos[i].go;
                    if (go != null)
                    {
                        _objects.Remove(go);
                        UnityEngine.Object.Destroy(go);
                    }
                    _decos.RemoveAt(i);
                }
            }
        }

        public bool Build(PonderSceneDef scene)
        {
            Clear();
            try
            {
                if (scrLevelMaker.instance == null || scrLevelMaker.instance.spriteFloor == null)
                {
                    Main.Handler?.Error("PonderPreview: no scrLevelMaker.spriteFloor");
                    return false;
                }

                _root = new GameObject("PonderPreviewRoot");
                UnityEngine.Object.DontDestroyOnLoad(_root);

                _arrowCanvas = CreateArrowCanvas();

                _spawnStagger = scene.SpawnStagger;
                var tileSize = GetTileSize();
                var floorPositions = new List<Vector3>();
                for (var i = 0; i < scene.Tiles.Count; i++)
                {
                    var tile = scene.Tiles[i];
                    var pos = AppendTile(tile.Angle, tile.Style, tile.Midspin, i == 0, i == scene.Tiles.Count - 1);
                    floorPositions.Add(pos);
                    // 开场逐块弹出：第 0 块立刻显示，其余按 spawnStagger 间隔丝滑出现
                    if (scene.SpawnStagger > 0f && _tileFloors.Count >= 1)
                    {
                        var idx = _tileFloors.Count - 1;
                        _tileSpawn[idx] = idx == 0 ? 1f : 0f;
                        _tileSpawnDelay[idx] = idx == 0 ? 0f : idx * scene.SpawnStagger;
                    }
                }
                if (floorPositions.Count == 0)
                {
                    floorPositions.Add(Vector3.zero);
                }
                // 某些上下文（非游戏场景）scrController.LateUpdate 不驱动网格重建，手动触发一次；
                // 若砖块走 FloorSpriteRenderer 则为 no-op。
                try
                {
                    FloorMesh.UpdateAllRequired();
                }
                catch (Exception ex)
                {
                    Main.Handler?.Log($"PONDER FloorMesh rebuild failed: {ex.Message}");
                }

                if (scene.Decos.Count > 0 && scrDecorationManager.instance != null)
                {
                    foreach (var deco in scene.Decos)
                    {
                        CreateDeco(scene, deco, floorPositions, tileSize);
                    }
                }
                var bounds = ComputeBounds(floorPositions, tileSize);
                WorldSize = bounds.size.magnitude;
                SetupCamera(scene, bounds);
                _baseTileCount = _tileFloors.Count;
                _baseCursorPos = _cursorPos;
                _baseCursorExit = _cursorExit;
                _hasBaseState = true;
                IsReady = true;
                return true;
            }
            catch (Exception ex)
            {
                Main.Handler?.Error($"PonderPreview.Build failed\n{ex}");
                Clear();
                return false;
            }
        }

        private Canvas? CreateArrowCanvas()
        {
            if (_root == null)
            {
                return null;
            }
            _arrowCanvasGo = new GameObject("PonderArrowCanvas");
            _arrowCanvasGo.transform.SetParent(_root.transform, false);
            SetLayer(_arrowCanvasGo, PreviewLayer);
            var canvas = _arrowCanvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 100;
            return canvas;
        }

        /// <summary>
        /// 追加一块砖（沙盒）。返回该砖的世界坐标。
        /// </summary>
        public Vector3 AppendTile(float angle, int style, bool midspin, bool isFirst, bool isLast)
        {
            if (_root == null)
            {
                return _cursorPos;
            }
            var isFirstActual = _tileFloors.Count == 0;
            if (isFirstActual)
            {
                _cursorPos = Vector3.zero;
                _cursorExit = 4.71238898038469;
            }

            var pos = _cursorPos;
            var floorGo = UnityEngine.Object.Instantiate(scrLevelMaker.instance.meshFloor, _root.transform);
            _objects.Add(floorGo);
            _tileObjects.Add(floorGo);
            SetLayer(floorGo, PreviewLayer);
            var floor = floorGo.GetComponent<scrFloor>();
            if (floor != null)
            {
                floor.styleNum = isFirstActual ? -1 : style;
                floor.entryangle = isFirstActual ? 4.71238898038469 : Mod2Pi(_cursorExit + Math.PI);
                // 末块按官方逻辑收尾（scrLevelMaker：listFloors.Last().exitangle = entryangle + π），
                // 必须在 UpdateAngle 前定好，否则精灵按旧 exitangle 渲染（末块变 360°/start=end）。
                // exitangle 归一到 [0, 2π)，否则末块 exit 为 450°/360° 而非与其他直砖一致的 90°。
                var exit = isLast
                    ? Mod2Pi(floor.entryangle + Math.PI)
                    : (-(double)angle + 90.0) * Math.PI / 180.0;
                floor.exitangle = exit;
                floor.midSpin = midspin;
                // isFake 阻止 UpdateIconSprite 把 nextfloor==null 的砖设成传送门（sprPortal）
                floor.isFake = true;
                floor.UpdateAngle(true);
                // 临时诊断：确认角度与渲染路径（sprite 还是 mesh）。
                var rnd = floor.floorRenderer;
                var rndName = rnd?.GetType().Name ?? "null";
                var sprName = rnd is FloorSpriteRenderer ? (rnd.sprite?.name ?? "null") : "-";
                var mesh = rnd as FloorMeshRenderer;
                var meshInfo = mesh != null && mesh.floorMesh != null
                    ? $"{mesh.floorMesh._angle0:F0}/{mesh.floorMesh._angle1:F0}"
                    : "-";
                Main.Handler?.Log(
                    $"PONDER-TILE idx={_tileBases.Count} angle={angle:F0} " +
                    $"entry={(float)(floor.entryangle * 57.29578):F1} exit={(float)(floor.exitangle * 57.29578):F1} " +
                    $"pos={pos} renderer={rndName} sprite={sprName} meshAngle={meshInfo}");
                _cursorExit = exit;
            }
            floorGo.transform.position = pos;
            _cursorPos = pos + GetTileSize() * Dir(_cursorExit);
            _tileFloors.Add(floor);
            _tileBases.Add(pos);
            _tileForwards.Add(Dir(_cursorExit));
            _tileExtra.Add(Vector3.zero);
            _tileScale.Add(1f);
            _tileScaleY.Add(1f);
            _tileRot.Add(0f);
            _tileOpacity.Add(1f);
            _tileColors.Add(Color.white);
            _tileBaseStyles.Add(style);
            _tileSpawn.Add(1f);
            _tileSpawnDelay.Add(0f);
            return pos;
        }

        /// <summary>追加一段砖块（沙盒 AddTiles）。fromTile &gt;= 0 时从该砖块末端起插入（可任意角度），缺省 -1 表示接在最后一块之后。</summary>
        public void AppendTiles(List<PonderSceneTile> tiles, int fromTile = -1)
        {
            if (tiles == null || tiles.Count == 0)
            {
                return;
            }
            if (fromTile >= 0 && _tileBases.Count > 0)
            {
                MoveCursorToTileEnd(Mathf.Clamp(fromTile, 0, _tileBases.Count - 1));
            }
            var count = _tileFloors.Count;
            for (var i = 0; i < tiles.Count; i++)
            {
                var tile = tiles[i];
                AppendTile(tile.Angle, tile.Style, tile.Midspin, count == 0, i == tiles.Count - 1);
            }
            for (var i = count; i < _tileSpawn.Count; i++)
            {
                _tileSpawn[i] = 0f;
                _tileSpawnDelay[i] = _spawnClock + (i - count) * _spawnStagger;
            }
        }

        /// <summary>把沙盒游标移到某砖块的末端（位置 + 出口方向），用于从中途插入新砖块。</summary>
        private void MoveCursorToTileEnd(int index)
        {
            if (index < 0 || index >= _tileBases.Count)
            {
                return;
            }
            var floor = index < _tileFloors.Count ? _tileFloors[index] : null;
            var exit = floor != null ? floor.exitangle : 4.71238898038469;
            _cursorPos = _tileBases[index] + GetTileSize() * Dir(exit);
            _cursorExit = exit;
        }

        private static bool IsArrowImage(string image)
        {
            return string.Equals(image, "arrow", StringComparison.OrdinalIgnoreCase)
                || string.Equals(image, "arrow.png", StringComparison.OrdinalIgnoreCase);
        }

        private void CreateArrowDeco(Vector3 pos, float rotation, float scale, int depth, int tile, string? tag, bool spawned)
        {
            if (_arrowCanvas == null)
            {
                return;
            }
            var go = new GameObject("Deco_arrow", typeof(RectTransform));
            go.transform.SetParent(_arrowCanvas.transform, false);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0, 0, rotation);
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(1.8f, 1.0f);
            rt.localScale = Vector3.one * scale;
            var graphic = go.AddComponent<PonderArrowGraphic>();
            graphic.raycastTarget = false;
            graphic.color = spawned ? new Color(1f, 1f, 1f, 0f) : Color.white;
            SetLayer(go, PreviewLayer);
            _objects.Add(go);

            _decos.Add(new DecoObj
            {
                go = go,
                basePos = pos,
                baseScale = scale,
                tile = tile,
                tag = tag ?? "",
                graphic = graphic,
                spawned = spawned
            });
        }

        private void CreateDeco(PonderSceneDef scene, PonderSceneDeco deco, List<Vector3> floorPositions, float tileSize)
        {
            var tile = Mathf.Clamp(deco.Tile, 0, floorPositions.Count - 1);
            var pos = floorPositions[tile];
            pos += new Vector3(deco.OffsetX, deco.OffsetY, 0f) * tileSize;
            var image = deco.Image ?? "";
            if (IsArrowImage(image))
            {
                CreateArrowDeco(pos, deco.Rotation, deco.Scale, deco.Depth, tile, deco.Tag, false);
                return;
            }
            var sprite = PonderEngine.GetImage(scene, image);
            if (sprite == null)
            {
                return;
            }
            var go = new GameObject($"Deco_{image}");
            go.transform.SetParent(_root!.transform, false);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0, 0, deco.Rotation);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = deco.Depth * 10;
            sr.color = Color.white;
            go.transform.localScale = Vector3.one * deco.Scale;
            SetLayer(go, PreviewLayer);
            _objects.Add(go);

            _decos.Add(new DecoObj
            {
                go = go,
                basePos = pos,
                baseScale = deco.Scale,
                tile = tile,
                tag = deco.Tag ?? "",
                sr = sr
            });
        }

        // ================= 命令驱动接口 =================

        public void SetGlobalDelta(Vector3 delta)
        {
            _globalDelta = delta;
        }

        public void SetGlobalScale(float scale)
        {
            _globalScale = scale;
        }

        public void SetTileExtraDelta(int from, int to, Vector3 delta)
        {
            from = Mathf.Clamp(from, 0, Mathf.Max(0, _tileExtra.Count - 1));
            to = Mathf.Clamp(to, from, Mathf.Max(from, _tileExtra.Count - 1));
            for (var i = from; i <= to; i++)
            {
                _tileExtra[i] = delta;
            }
        }

        public void SetTileScale(int from, int to, float scale)
        {
            SetTileScale(from, to, new Vector2(scale, scale));
        }

        public void SetTileScale(int from, int to, Vector2 scale)
        {
            from = Mathf.Clamp(from, 0, Mathf.Max(0, _tileScale.Count - 1));
            to = Mathf.Clamp(to, from, Mathf.Max(from, _tileScale.Count - 1));
            for (var i = from; i <= to; i++)
            {
                _tileScale[i] = scale.x;
                _tileScaleY[i] = scale.y;
            }
        }

        public void SetTileRotation(int from, int to, float rotation)
        {
            from = Mathf.Clamp(from, 0, Mathf.Max(0, _tileRot.Count - 1));
            to = Mathf.Clamp(to, from, Mathf.Max(from, _tileRot.Count - 1));
            for (var i = from; i <= to; i++)
            {
                _tileRot[i] = rotation;
            }
        }

        public void SetTileOpacity(int from, int to, float opacity)
        {
            from = Mathf.Clamp(from, 0, Mathf.Max(0, _tileOpacity.Count - 1));
            to = Mathf.Clamp(to, from, Mathf.Max(from, _tileOpacity.Count - 1));
            for (var i = from; i <= to; i++)
            {
                _tileOpacity[i] = Mathf.Clamp01(opacity);
            }
        }

        public void SetTileColorRange(int from, int to, Color color)
        {
            SetTileColorRange(from, to, color, 1);
        }

        public void SetTileColorRange(int from, int to, Color color, int step)
        {
            from = Mathf.Clamp(from, 0, Mathf.Max(0, _tileFloors.Count - 1));
            to = Mathf.Clamp(to, from, Mathf.Max(from, _tileFloors.Count - 1));
            step = Mathf.Max(1, step);
            for (var i = from; i <= to; i += step)
            {
                _tileFloors[i]?.SetTileColor(color);
                if (i < _tileColors.Count)
                {
                    _tileColors[i] = color;
                }
            }
        }

        public void SetTileColor(int index, Color color)
        {
            SetTileColorRange(index, index, color);
        }

        public void SetTileGlowRange(int from, int to, float glow, int step = 1)
        {
            from = Mathf.Clamp(from, 0, Mathf.Max(0, _tileFloors.Count - 1));
            to = Mathf.Clamp(to, from, Mathf.Max(from, _tileFloors.Count - 1));
            step = Mathf.Max(1, step);
            for (var i = from; i <= to; i += step)
            {
                var floor = _tileFloors[i];
                if (floor != null)
                {
                    floor.glowMultiplier = Mathf.Max(0f, glow);
                }
            }
        }

        public void SetTileStyleRange(int from, int to, TrackStyle style, int step = 1)
        {
            from = Mathf.Clamp(from, 0, Mathf.Max(0, _tileFloors.Count - 1));
            to = Mathf.Clamp(to, from, Mathf.Max(from, _tileFloors.Count - 1));
            step = Mathf.Max(1, step);
            for (var i = from; i <= to; i += step)
            {
                var floor = _tileFloors[i];
                if (floor == null)
                {
                    continue;
                }
                floor.styleNum = (int)style;
                floor.SetTrackStyle(style);
                floor.UpdateAngle(true);
            }
        }

        public void ResetTileColors()
        {
            foreach (var floor in _tileFloors)
            {
                floor?.SetTileColor(Color.white);
            }
            for (var i = 0; i < _tileColors.Count; i++)
            {
                _tileColors[i] = Color.white;
                var floor = i < _tileFloors.Count ? _tileFloors[i] : null;
                if (floor != null)
                {
                    floor.glowMultiplier = 1f;
                    if (i < _tileBaseStyles.Count)
                    {
                        floor.styleNum = _tileBaseStyles[i];
                        floor.SetTrackStyle((TrackStyle)_tileBaseStyles[i]);
                        floor.UpdateAngle(true);
                    }
                }
            }
        }

        public void SetTileStyleRange(int from, int to, int style)
        {
            from = Mathf.Clamp(from, 0, Mathf.Max(0, _tileFloors.Count - 1));
            to = Mathf.Clamp(to, from, Mathf.Max(from, _tileFloors.Count - 1));
            for (var i = from; i <= to; i++)
            {
                var floor = _tileFloors[i];
                if (floor == null)
                {
                    continue;
                }
                floor.styleNum = style;
                floor.UpdateAngle(true);
            }
        }

        public int AddDeco(PonderSceneDef scene, string image, int tile, float offsetX, float offsetY, float scale, float rotation, int depth, string tag)
        {
            if (_tileBases.Count == 0)
            {
                return -1;
            }
            tile = Mathf.Clamp(tile, 0, _tileBases.Count - 1);
            var tileSize = GetTileSize();
            var basePos = _tileBases[tile] + new Vector3(offsetX, offsetY, 0f) * tileSize;
            if (IsArrowImage(image))
            {
                CreateArrowDeco(basePos, rotation, scale, depth, tile, tag, true);
                return _decos.Count - 1;
            }
            var sprite = PonderEngine.GetImage(scene, image);
            if (sprite == null)
            {
                return -1;
            }
            var go = new GameObject($"DecoCmd_{image}");
            go.transform.SetParent(_root!.transform, false);
            go.transform.position = basePos;
            go.transform.rotation = Quaternion.Euler(0, 0, rotation);
            go.transform.localScale = Vector3.one * scale;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = depth * 10;
            sr.color = new Color(1f, 1f, 1f, 0f);
            SetLayer(go, PreviewLayer);
            _objects.Add(go);
            _decos.Add(new DecoObj
            {
                go = go,
                basePos = basePos,
                baseScale = scale,
                tile = tile,
                tag = tag ?? "",
                sr = sr,
                spawned = true
            });
            return _decos.Count - 1;
        }

        public void SetDecoAlpha(int index, float alpha)
        {
            if (index < 0 || index >= _decos.Count)
            {
                return;
            }
            var sr = _decos[index].sr;
            if (sr != null)
            {
                var c = sr.color;
                c.a = Mathf.Clamp01(alpha);
                sr.color = c;
            }
            var graphic = _decos[index].graphic;
            if (graphic != null)
            {
                var c = graphic.color;
                c.a = Mathf.Clamp01(alpha);
                graphic.color = c;
            }
        }

        public void SetDecoColor(int index, Color color)
        {
            if (index < 0 || index >= _decos.Count)
            {
                return;
            }
            var sr = _decos[index].sr;
            if (sr != null)
            {
                sr.color = color;
            }
            var graphic = _decos[index].graphic;
            if (graphic != null)
            {
                graphic.color = color;
            }
        }

        public void SetDecoTaggedDelta(string tag, Vector3 delta)
        {
            if (string.IsNullOrEmpty(tag))
            {
                return;
            }
            foreach (var deco in _decos)
            {
                if (deco.tag == tag)
                {
                    deco.extra = delta;
                }
            }
        }

        public void SetDecoTaggedRotation(string tag, float rotation)
        {
            if (string.IsNullOrEmpty(tag))
            {
                return;
            }
            foreach (var deco in _decos)
            {
                if (deco.tag == tag && deco.go != null)
                {
                    deco.go.transform.rotation = Quaternion.Euler(0, 0, rotation);
                }
            }
        }

        private static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            t -= 1f;
            return 1f + c3 * t * t * t + c1 * t * t;
        }

        public void ApplyTransforms()
        {
            for (var i = 0; i < _tileBases.Count; i++)
            {
                if (_root == null)
                {
                    return;
                }
                var go = _tileObjects[i];
                if (go == null)
                {
                    continue;
                }
                go.transform.position = _tileBases[i] + _globalDelta + _tileExtra[i];
                var spawn = i < _tileSpawn.Count ? EaseOutBack(_tileSpawn[i]) : 1f;
                var sx = _globalScale * _tileScale[i] * spawn;
                var sy = _globalScale * _tileScaleY[i] * spawn;
                go.transform.localScale = new Vector3(sx, sy, 1f);
                if (i < _tileRot.Count)
                {
                    go.transform.rotation = Quaternion.Euler(0f, 0f, _tileRot[i]);
                }
                var floor = _tileFloors[i];
                if (i < _tileOpacity.Count && floor != null)
                {
                    floor.opacity = Mathf.Clamp01(_tileOpacity[i]);
                }
            }
            foreach (var deco in _decos)
            {
                if (deco.go == null)
                {
                    continue;
                }
                var extra = _globalDelta + deco.extra;
                if (deco.tile >= 0 && deco.tile < _tileExtra.Count)
                {
                    extra += _tileExtra[deco.tile];
                }
                deco.go.transform.position = deco.basePos + extra;
                var ds = _globalScale * deco.baseScale;
                deco.go.transform.localScale = new Vector3(ds, ds, 1f);
            }
        }

        // ================= 相机 =================

        private static float GetTileSize()
        {
            try
            {
                if (ADOBase.controller != null)
                {
                    return ADOBase.controller.tileSize;
                }
            }
            catch
            {
            }
            return 1f;
        }

        private static double Mod2Pi(double angle)
        {
            angle %= Math.PI * 2.0;
            if (angle < 0.0)
            {
                angle += Math.PI * 2.0;
            }
            return angle;
        }

        private static Vector3 Dir(double exitAngle)
        {
            return new Vector3(Mathf.Sin((float)exitAngle), Mathf.Cos((float)exitAngle), 0f);
        }

        private static Bounds ComputeBounds(List<Vector3> floorPositions, float tileSize)
        {
            var min = new Vector3(float.MaxValue, float.MaxValue, 0f);
            var max = new Vector3(float.MinValue, float.MinValue, 0f);
            foreach (var p in floorPositions)
            {
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
            var margin = tileSize * 2f;
            min -= new Vector3(margin, margin, 0f);
            max += new Vector3(margin, margin, 0f);
            var size = max - min;
            var center = (min + max) * 0.5f;
            return new Bounds(center, size);
        }

        /// <summary>当前全部可见内容的包围盒：砖块 + 装饰物 + 当前章节的指点元素（文字框与箭头端）。</summary>
        private Bounds ComputeContentBounds()
        {
            var tileSize = GetTileSize();
            var pts = new List<Vector3>(_tileBases.Count + _decos.Count + _noteObjs.Count * 2);
            pts.AddRange(_tileBases);
            foreach (var d in _decos)
            {
                if (d.go != null)
                {
                    pts.Add(d.basePos);
                }
            }
            foreach (var n in _noteObjs)
            {
                if (n.graphic == null)
                {
                    continue;
                }
                var anchor = FixedTilePos(n.tile) + n.startOffset;
                var target = FixedTilePos(n.tile) + n.endOffset;
                pts.Add(anchor);
                pts.Add(target);
                if (n.boxSize.x > 0.0001f)
                {
                    var half = new Vector3(n.boxSize.x * 0.5f, n.boxSize.y * 0.5f, 0f);
                    pts.Add(anchor + half);
                    pts.Add(anchor - half);
                }
            }
            if (pts.Count == 0)
            {
                pts.Add(Vector3.zero);
            }
            return ComputeBounds(pts, tileSize);
        }

        private void SetupCamera(PonderSceneDef scene, Bounds bounds)
        {
            _bounds = bounds;
            Texture = new RenderTexture(1280, 720, 16, RenderTextureFormat.ARGB32);
            Texture.name = "PonderPreviewRT";

            _cameraGo = new GameObject("PonderPreviewCamera");
            _cameraGo.transform.SetParent(_root!.transform, false);
            _cameraGo.layer = PreviewLayer;
            _camera = _cameraGo.AddComponent<Camera>();
            _camera.clearFlags = CameraClearFlags.SolidColor;
            // 透明清除色：预览区让底下的毛玻璃透出，而非黑色容器
            _camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            _camera.orthographic = true;
            _camera.orthographicSize = scene.Zoom;
            _camera.targetTexture = Texture;
            _camera.cullingMask = 1 << PreviewLayer;
            _camera.nearClipPlane = -100f;
            _camera.farClipPlane = 100f;
            _cameraGo.transform.position = new Vector3(bounds.center.x, bounds.center.y, -10f);
            if (Mathf.Abs(scene.CenterX) > 0.0001f || Mathf.Abs(scene.CenterY) > 0.0001f)
            {
                _cameraGo.transform.position += new Vector3(scene.CenterX, scene.CenterY, 0f) * GetTileSize();
            }
        }

        /// <summary>整体取景：按当前全部可见内容（砖块+装饰+指点）自动计算正交尺寸与中心，一次到位（打开场景/章节切换时用）。</summary>
        public void FitTrack()
        {
            if (_camera == null || Texture == null)
            {
                return;
            }
            _bounds = ComputeContentBounds();
            var aspect = (float)Texture.width / Texture.height;
            var size = _bounds.size;
            var ortho = Mathf.Max(size.y * 0.5f, size.x / (2f * aspect)) * 1.15f;
            _camera.orthographicSize = Mathf.Max(ortho, 1.5f);
            _targetOrtho = _camera.orthographicSize;
            var center = _bounds.center;
            _cameraFocusPos = new Vector3(center.x, center.y, -10f);
            _cameraGo!.transform.position = _cameraFocusPos;
            _cameraSnapped = true;
            _cameraFocusFloor = -1;
        }

        /// <summary>平滑追焦某块砖（Create 风格：每帧追赶目标，不瞬移）。</summary>
        public void FocusFloor(int floorIndex)
        {
            if (_cameraGo == null || _tileBases.Count == 0)
            {
                return;
            }
            _cameraFocusFloor = Mathf.Clamp(floorIndex, 0, _tileBases.Count - 1);
            _cameraSnapped = false;
        }

        /// <summary>追焦时使用的正交尺寸（每帧追赶）。</summary>
        public void FocusZoom(float ortho)
        {
            _targetOrtho = ortho;
        }

        /// <summary>当前正交尺寸（用于把屏幕拖拽像素换算成世界位移）。</summary>
        public float CurrentOrtho => _camera != null ? _camera.orthographicSize : _targetOrtho;

        /// <summary>沙盒拖拽平移视角：按世界位移移动相机，并暂停追焦跟随。</summary>
        public void PanCamera(Vector2 worldDelta)
        {
            if (_cameraGo == null)
            {
                return;
            }
            _cameraFocusPos += new Vector3(worldDelta.x, worldDelta.y, 0f);
            _cameraSnapped = false;
            _cameraFocusFloor = -1;
        }

        /// <summary>鼠标滚轮缩放：缩放目标正交尺寸（钳位），相机逐帧平滑追赶。</summary>
        public void ZoomBy(float factor)
        {
            if (_camera == null)
            {
                return;
            }
            _targetOrtho = Mathf.Clamp(_targetOrtho * factor, 0.6f, 80f);
        }

        /// <summary>立即把相机切到当前追焦目标（章节跳转开播前）。</summary>
        public void SnapCameraToFocus()
        {
            RecomputeCameraFocus();
            if (_cameraGo != null)
            {
                _cameraGo.transform.position = _cameraFocusPos;
            }
            _cameraSnapped = true;
        }

        private void RecomputeCameraFocus()
        {
            if (_cameraFocusFloor < 0 || _cameraFocusFloor >= _tileBases.Count)
            {
                return;
            }
            var p = _tileBases[_cameraFocusFloor];
            if (_cameraFocusFloor < _tileExtra.Count)
            {
                p += _globalDelta + _tileExtra[_cameraFocusFloor];
            }
            _cameraFocusPos = new Vector3(p.x, p.y, -10f);
        }

        /// <summary>逐帧推进视觉：相机追赶目标点 + 砖块出生动画（丝滑）。</summary>
        public void UpdateVisuals(float dt)
        {
            RecomputeCameraFocus();
            if (_cameraGo != null && _camera != null)
            {
                if (_cameraSnapped)
                {
                    _cameraGo.transform.position = _cameraFocusPos;
                }
                else
                {
                    var s = 1f - Mathf.Exp(-6f * dt);
                    _cameraGo.transform.position = Vector3.Lerp(_cameraGo.transform.position, _cameraFocusPos, s);
                }
                _camera.orthographicSize = Mathf.Lerp(_camera.orthographicSize, _targetOrtho, 1f - Mathf.Exp(-5f * dt));
            }

            var animating = false;
            _spawnClock += dt;
            for (var i = 0; i < _tileSpawn.Count; i++)
            {
                if (_tileSpawn[i] < 1f)
                {
                    var start = i < _tileSpawnDelay.Count ? _tileSpawnDelay[i] : _spawnClock;
                    var p = Mathf.Clamp01((_spawnClock - start) * TileSpawnSpeed);
                    if (p > _tileSpawn[i])
                    {
                        _tileSpawn[i] = Mathf.Min(1f, p);
                        animating = true;
                    }
                }
            }
            if (animating)
            {
                ApplyTransforms();
            }
            UpdateNotes();
        }

        private static void SetLayer(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform)
            {
                SetLayer(child.gameObject, layer);
            }
        }

        public void Clear()
        {
            IsReady = false;
            ClearNotes();
            foreach (var go in _objects)
            {
                if (go != null)
                {
                    UnityEngine.Object.Destroy(go);
                }
            }
            _objects.Clear();
            _tileObjects.Clear();
            _decos.Clear();
            if (_arrowCanvasGo != null)
            {
                UnityEngine.Object.Destroy(_arrowCanvasGo);
                _arrowCanvasGo = null;
                _arrowCanvas = null;
            }
            _tileFloors.Clear();
            _tileBases.Clear();
            _tileForwards.Clear();
            _tileExtra.Clear();
            _tileScale.Clear();
            _tileScaleY.Clear();
            _tileRot.Clear();
            _tileOpacity.Clear();
            _tileColors.Clear();
            _tileBaseStyles.Clear();
            _tileSpawn.Clear();
            _tileSpawnDelay.Clear();
            _spawnClock = 0f;
            _spawnStagger = 0.15f;
            _baseTileCount = 0;
            _hasBaseState = false;
            _cursorPos = Vector3.zero;
            _cursorExit = 4.71238898038469;
            _globalDelta = Vector3.zero;
            _globalScale = 1f;
            _cameraFocusFloor = -1;
            _cameraSnapped = true;
            if (_cameraGo != null)
            {
                UnityEngine.Object.Destroy(_cameraGo);
                _cameraGo = null;
                _camera = null;
            }
            if (Texture != null)
            {
                Texture.Release();
                UnityEngine.Object.Destroy(Texture);
                Texture = null;
            }
            if (_root != null)
            {
                UnityEngine.Object.Destroy(_root);
                _root = null;
            }
        }

        public void Dispose()
        {
            Clear();
        }
    }
}
