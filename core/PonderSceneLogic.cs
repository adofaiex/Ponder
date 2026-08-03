using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ponder
{
    /// <summary>
    /// 沙盒逻辑执行器。支持：
    /// - 原生 ADOFAI 事件（MoveTrack / PositionTrack / RecolorTrack / MoveDecorations / AddDecoration ...），
    ///   属性名与单位均沿用官方（positionOffset 为砖块单位、trackColor 为十六进制、ease 支持官方缓动名）；
    /// - 沙盒命令（AddTiles / SetFloorStyle / ScaleTrack / Wait ...）。
    /// 每个命令按 duration + ease 平滑插值应用（丝滑演示）。
    /// </summary>
    public sealed class PonderSceneLogic
    {
        private readonly PonderPreview _preview;
        private readonly PonderPreviewContext _context;
        private readonly PonderSceneDef _scene;
        private List<PonderLogicCommand> _commands = new List<PonderLogicCommand>();
        private int _index;
        private float _state;
        private bool _playing;
        private bool _entered;
        private Action? _onComplete;

        private Vector3 _startDelta;
        private float _startScale = 1f;
        private int _decoIndex = -1;
        private float _decoTargetAlpha = 1f;
        private Color _fromColor = Color.white;
        private PonderOfficialEvent? _official;
        private int _moveStart;
        private int _moveEnd;
        private int _moveStep = 1;
        private readonly List<Vector3> _moveFromExtra = new List<Vector3>();
        private readonly List<float> _moveFromScale = new List<float>();
        private readonly List<float> _moveFromScaleY = new List<float>();
        private readonly List<float> _moveFromRot = new List<float>();
        private readonly List<float> _moveFromOpacity = new List<float>();
        private readonly List<Vector3> _positionFromExtra = new List<Vector3>();
        private readonly List<float> _positionFromScale = new List<float>();
        private readonly List<float> _positionFromScaleY = new List<float>();
        private readonly List<float> _positionFromRot = new List<float>();
        private readonly List<float> _positionFromOpacity = new List<float>();

        public bool IsPlaying => _playing;

        public PonderSceneLogic(PonderPreview preview, PonderSceneDef scene)
        {
            _preview = preview;
            _context = preview.Context;
            _scene = scene;
        }

        public void Play(List<PonderLogicCommand> commands, Action? onComplete = null)
        {
            _commands = commands ?? new List<PonderLogicCommand>();
            _index = 0;
            _state = 0f;
            _entered = false;
            _onComplete = onComplete;
            _playing = true;
        }

        public void Stop()
        {
            _playing = false;
            _onComplete = null;
        }

        public void Update(float dt)
        {
            if (!_playing)
            {
                return;
            }

            if (_index >= _commands.Count)
            {
                _playing = false;
                var done = _onComplete;
                _onComplete = null;
                done?.Invoke();
                return;
            }

            var c = _commands[_index];
            var activeDuration = Mathf.Max(c.Duration, 0.001f);
            _state += dt;

            if (_state < c.Delay)
            {
                return;
            }
            if (!_entered)
            {
                _entered = true;
                OnCommandEnter(c);
            }

            var t = Mathf.Clamp01((_state - c.Delay) / activeDuration);
            var e = Ease(t, c.Ease);
            ApplyCommand(c, t, e);

            if (_state >= c.Delay + activeDuration)
            {
                OnCommandExit(c);
                _index++;
                _state = 0f;
                _entered = false;
            }
        }

        private void OnCommandEnter(PonderLogicCommand c)
        {
            var name = c.Name;
            _official = c.EventType.Length > 0 ? _context.Decode(c) : null;
            switch (name)
            {
                case "MoveTrack":
                    _startDelta = _preview.GlobalDelta;
                    var range = _context.ResolveRange(c, _official);
                    _moveStart = range.start;
                    _moveEnd = range.end;
                    _moveStep = Mathf.Max(1, PropInt(c, "gapLength", 0) + 1);
                    CaptureMoveState();
                    break;
                case "ScaleTrack":
                    _startScale = _preview.GlobalScale;
                    break;
                case "AddTiles":
                    _preview.AppendTiles(c.Tiles, c.HasTile ? c.Tile : -1);
                    _preview.ApplyTransforms();
                    break;
                case "AddDecoration":
                {
                    var image = _official != null ? _official.Get("decorationImage", c.Image) : PropStr(c, "decorationImage", c.Image);
                    var tag = Tag(c);
                    var tile = ResolveStart(c);
                    float ox = c.OffsetX, oy = c.OffsetY;
                    if (_official != null && _official.Contains("position"))
                    {
                        var position = _official.GetVector2("position", Vector2.zero);
                        ox = position.x;
                        oy = position.y;
                    }
                    else if (c.Props != null && c.Props.TryGetValue("position", out var posObj) && posObj is List<object> posList && posList.Count >= 2)
                    {
                        ox = Convert.ToSingle(posList[0]);
                        oy = Convert.ToSingle(posList[1]);
                    }
                    var scale = _official != null ? _official.GetVector2("scale", Vector2.one * 100f).x / 100f : PropFloat(c, "scale", c.Scale);
                    var rot = _official != null ? _official.Get("rotation", c.Rotation) : PropFloat(c, "rotation", c.Rotation);
                    var depth = PropInt(c, "depth", c.Style);
                    _decoIndex = _preview.AddDeco(_scene, image, tile, ox, oy, scale, rot, depth, tag);
                    if (_decoIndex >= 0 && _official != null)
                    {
                        var color = _official.GetColor("color", Color.white);
                        _decoTargetAlpha = _official.Get("opacity", 100f) / 100f;
                        color.a = _decoTargetAlpha;
                        _preview.SetDecoColor(_decoIndex, color);
                    }
                    break;
                }
            }
            if (name == "RecolorTrack" || name == "SetTrackColor" || name == "SetFloorColor")
            {
                _fromColor = _preview.GetTileColor(ResolveStart(c));
            }
        }

        private void ApplyCommand(PonderLogicCommand c, float t, float e)
        {
            switch (c.Name)
            {
                case "MoveTrack":
                    if (c.EventType.Length > 0)
                    {
                        ApplyNativeMoveTrack(c, t, e);
                    }
                    else
                    {
                        _preview.SetGlobalDelta(Vector3.Lerp(_startDelta, _startDelta + Offset(c), e));
                    }
                    break;
                case "PositionTrack":
                {
                    var range = _context.ResolveRange(c, _official);
                    _positionFromExtra.Clear();
                    CapturePositionState(range.start, range.end);
                    ApplyPositionTrack(c, range.start, range.end, e);
                    break;
                }
                case "ScaleTrack":
                    _preview.SetGlobalScale(Mathf.Lerp(_startScale, c.Scale, e));
                    break;
                case "AddDecoration":
                    if (_decoIndex >= 0)
                    {
                        _preview.SetDecoAlpha(_decoIndex, Mathf.Lerp(0f, _decoTargetAlpha, e));
                    }
                    break;
                case "MoveDecorations":
                    _preview.SetDecoTaggedDelta(Tag(c), Offset(c) * e);
                    break;
                case "RecolorTrack":
                case "SetTrackColor":
                    if (TryColor(c, out var col))
                    {
                        ApplyTrackColor(c, Color.Lerp(_fromColor, col, e), e);
                    }
                    break;
                case "SetFloorColor":
                    if (TryColor(c, out var col2))
                    {
                        ApplyTrackColor(c, Color.Lerp(_fromColor, col2, e), e);
                    }
                    break;
            }
            _preview.ApplyTransforms();
        }

        /// <summary>按 startTile/endTile 范围上色；未指定范围时退化为整条轨道。</summary>
        private void ApplyTrackColor(PonderLogicCommand c, Color col, float progress = 1f)
        {
            var hasRange = _official != null || (c.Props != null &&
                (c.Props.ContainsKey("startTile") || c.Props.ContainsKey("endTile") || c.Props.ContainsKey("tile")));
            var range = hasRange ? _context.ResolveRange(c, _official) : (0, _preview.TileCount - 1);
            var start = range.Item1;
            var end = range.Item2;
            var step = Mathf.Max(1, PropInt(c, "gapLength", 0) + 1);
            var secondary = _official != null
                ? _official.GetColor("secondaryTrackColor", col)
                : ParseColor(c, "secondaryTrackColor", col);
            var colorType = _official != null
                ? _official.Get("trackColorType", TrackColorType.Single)
                : TrackColorType.Single;
            var pulse = _official != null
                ? _official.Get("trackColorPulse", TrackColorPulse.None)
                : TrackColorPulse.None;
            var pulseLength = _official != null
                ? Mathf.Max(1, _official.Get("trackPulseLength", 10))
                : 10;
            var style = _official != null
                ? _official.Get("trackStyle", TrackStyle.Standard)
                : TrackStyle.Standard;
            var glow = _official != null
                ? _official.Get("trackGlowIntensity", 100f) / 100f
                : 1f;

            for (var i = start; i <= end; i += step)
            {
                var pulseIndex = i;
                if (pulse == TrackColorPulse.Forward)
                {
                    pulseIndex = i + Mathf.RoundToInt((1f - progress) * pulseLength);
                }
                else if (pulse == TrackColorPulse.Backward)
                {
                    pulseIndex = i + Mathf.RoundToInt(progress * pulseLength);
                }
                var tileColor = colorType switch
                {
                    TrackColorType.Stripes => ((pulseIndex - start) % 2 == 0) ? col : secondary,
                    TrackColorType.Rainbow => Color.HSVToRGB(Mathf.Repeat(progress + i * 0.08f, 1f), 0.8f, 1f),
                    TrackColorType.Glow => Color.white,
                    _ => col
                };
                _preview.SetTileColor(i, tileColor);
                if (colorType == TrackColorType.Glow || _official?.Contains("trackGlowIntensity") == true)
                {
                    _preview.SetTileGlowRange(i, i, glow);
                }
                if (_official != null && !_official.IsDisabled("trackStyle"))
                {
                    _preview.SetTileStyleRange(i, i, style);
                }
            }
        }

        private static Color ParseColor(PonderLogicCommand c, string key, Color fallback)
        {
            if (c.Props != null && c.Props.TryGetValue(key, out var value) && value is string text &&
                ColorUtility.TryParseHtmlString(text, out var color))
            {
                return color;
            }
            return fallback;
        }

        /// <summary>捕获 MoveTrack 命令开始时范围砖块的当前状态，用于增量插值（避免连续命令起点瞬跳）。</summary>
        private void CaptureMoveState()
        {
            _moveFromExtra.Clear();
            _moveFromScale.Clear();
            _moveFromScaleY.Clear();
            _moveFromRot.Clear();
            _moveFromOpacity.Clear();
            for (var i = _moveStart; i <= _moveEnd; i += _moveStep)
            {
                if (i < 0 || i >= _preview.TileCount)
                {
                    _moveFromExtra.Add(Vector3.zero);
                    _moveFromScale.Add(1f);
                    _moveFromScaleY.Add(1f);
                    _moveFromRot.Add(0f);
                    _moveFromOpacity.Add(1f);
                    continue;
                }
                _moveFromExtra.Add(_preview.TileExtraAt(i));
                _moveFromScale.Add(_preview.TileScaleAt(i));
                _moveFromScaleY.Add(_preview.TileScaleYAt(i));
                _moveFromRot.Add(_preview.TileRotationAt(i));
                _moveFromOpacity.Add(_preview.TileOpacityAt(i));
            }
        }

        /// <summary>原生 MoveTrack：start..end 范围按 gapLength 步进，增量插值 positionOffset/rotationOffset/scale/opacity。</summary>
        private void ApplyNativeMoveTrack(PonderLogicCommand c, float t, float e)
        {
            var hasOffset = UsesOfficial(c, "positionOffset");
            var hasRot = UsesOfficial(c, "rotationOffset");
            var hasScale = UsesOfficial(c, "scale");
            var hasOpacity = UsesOfficial(c, "opacity");
            var offset = Offset(c);
            var rot = _official != null ? _official.Get("rotationOffset", 0f) : PropFloat(c, "rotationOffset", 0f);
            var scaleV2 = _official != null ? _official.GetVector2("scale", Vector2.one * 100f) / 100f : Vector2.one * (PropFloat(c, "scale", 100f) / 100f);
            var opacity = _official != null ? _official.Get("opacity", 100f) / 100f : PropFloat(c, "opacity", 100f) / 100f;
            var idx = 0;
            for (var i = _moveStart; i <= _moveEnd; i += _moveStep)
            {
                var fromExtra = idx < _moveFromExtra.Count ? _moveFromExtra[idx] : Vector3.zero;
                var fromScale = idx < _moveFromScale.Count ? _moveFromScale[idx] : 1f;
                var fromScaleY = idx < _moveFromScaleY.Count ? _moveFromScaleY[idx] : 1f;
                var fromRot = idx < _moveFromRot.Count ? _moveFromRot[idx] : 0f;
                var fromOpacity = idx < _moveFromOpacity.Count ? _moveFromOpacity[idx] : 1f;
                if (i >= 0 && i < _preview.TileCount)
                {
                    if (hasOffset)
                    {
                        _preview.SetTileExtraDelta(i, i, Vector3.Lerp(fromExtra, offset, e));
                    }
                    if (hasRot)
                    {
                        _preview.SetTileRotation(i, i, Mathf.Lerp(fromRot, rot, e));
                    }
                    if (hasScale)
                    {
                        _preview.SetTileScale(i, i, new Vector2(
                            Mathf.Lerp(fromScale, scaleV2.x, e),
                            Mathf.Lerp(fromScaleY, scaleV2.y, e)));
                    }
                    if (hasOpacity)
                    {
                        _preview.SetTileOpacity(i, i, Mathf.Lerp(fromOpacity, opacity, e));
                    }
                }
                idx++;
            }
            _preview.ApplyTransforms();
        }

        private void OnCommandExit(PonderLogicCommand c)
        {
            switch (c.Name)
            {
                case "MoveTrack":
                    if (c.EventType.Length > 0)
                    {
                        ApplyNativeMoveTrack(c, 1f, 1f);
                    }
                    else
                    {
                        _preview.SetGlobalDelta(_startDelta + Offset(c));
                    }
                    break;
                case "PositionTrack":
                {
                    var range = _context.ResolveRange(c, _official);
                    ApplyPositionTrack(c, range.start, range.end, 1f);
                    break;
                }
                case "ScaleTrack":
                    _preview.SetGlobalScale(c.Scale);
                    break;
                case "MoveDecorations":
                    _preview.SetDecoTaggedDelta(Tag(c), Offset(c));
                    break;
                case "RecolorTrack":
                case "SetTrackColor":
                case "SetFloorColor":
                    if (TryColor(c, out var col))
                    {
                        ApplyTrackColor(c, col, 1f);
                    }
                    break;
                case "SetFloorStyle":
                    if (c.Style >= 0)
                    {
                        _preview.SetTileStyleRange(ResolveStart(c), ResolveEnd(c), c.Style);
                    }
                    break;
            }
            _preview.ApplyTransforms();
            _official = null;
            _positionFromExtra.Clear();
            _positionFromScale.Clear();
            _positionFromScaleY.Clear();
            _positionFromRot.Clear();
            _positionFromOpacity.Clear();
        }

        private void CapturePositionState(int start, int end)
        {
            if (_positionFromExtra.Count > 0)
            {
                return;
            }
            _positionFromExtra.Clear();
            _positionFromScale.Clear();
            _positionFromScaleY.Clear();
            _positionFromRot.Clear();
            _positionFromOpacity.Clear();
            for (var i = start; i <= end; i++)
            {
                _positionFromExtra.Add(_preview.TileExtraAt(i));
                _positionFromScale.Add(_preview.TileScaleAt(i));
                _positionFromScaleY.Add(_preview.TileScaleYAt(i));
                _positionFromRot.Add(_preview.TileRotationAt(i));
                _positionFromOpacity.Add(_preview.TileOpacityAt(i));
            }
        }

        private void ApplyPositionTrack(PonderLogicCommand c, int start, int end, float e)
        {
            var hasPosition = UsesOfficial(c, "positionOffset");
            var hasScale = UsesOfficial(c, "scale");
            var hasRotation = UsesOfficial(c, "rotation");
            var hasOpacity = UsesOfficial(c, "opacity");
            var offset = Offset(c);
            var scale = _official != null
                ? _official.GetVector2("scale", Vector2.one * 100f) / 100f
                : Vector2.one * PropFloat(c, "scale", 100f) / 100f;
            var rotation = _official != null ? _official.Get("rotation", 0f) : PropFloat(c, "rotation", 0f);
            var opacity = _official != null ? _official.Get("opacity", 100f) / 100f : PropFloat(c, "opacity", 100f) / 100f;
            for (var i = start; i <= end; i++)
            {
                var index = i - start;
                if (hasPosition)
                {
                    var from = index < _positionFromExtra.Count ? _positionFromExtra[index] : Vector3.zero;
                    _preview.SetTileExtraDelta(i, i, Vector3.Lerp(from, offset, e));
                }
                if (hasScale)
                {
                    var fromX = index < _positionFromScale.Count ? _positionFromScale[index] : 1f;
                    var fromY = index < _positionFromScaleY.Count ? _positionFromScaleY[index] : 1f;
                    _preview.SetTileScale(i, i, new Vector2(Mathf.Lerp(fromX, scale.x, e), Mathf.Lerp(fromY, scale.y, e)));
                }
                if (hasRotation)
                {
                    var from = index < _positionFromRot.Count ? _positionFromRot[index] : 0f;
                    _preview.SetTileRotation(i, i, Mathf.Lerp(from, rotation, e));
                }
                if (hasOpacity)
                {
                    var from = index < _positionFromOpacity.Count ? _positionFromOpacity[index] : 1f;
                    _preview.SetTileOpacity(i, i, Mathf.Lerp(from, opacity, e));
                }
            }
        }

        private int ResolveEnd(PonderLogicCommand c)
        {
            if (_official != null && _official.GetTile("endTile") is { } officialEnd)
            {
                return _context.ResolveTile(c, officialEnd, _moveStart);
            }
            var end = c.EndTile;
            if (c.Props != null && c.Props.TryGetValue("endTile", out var endObj))
            {
                end = ResolveTileReference(endObj, c);
            }
            else if (c.EventType.Length == 0)
            {
                end = c.EndTile;
            }
            return end >= 0 ? end : ResolveStart(c);
        }

        /// <summary>起始砖：优先原生 startTile，其次命令 Tile，缺省 0。</summary>
        private int ResolveStart(PonderLogicCommand c)
        {
            if (_official != null && _official.GetTile("startTile") is { } officialStart)
            {
                return _context.ResolveTile(c, officialStart);
            }
            if (c.Props != null)
            {
                if (c.Props.TryGetValue("startTile", out var sObj))
                {
                    return ResolveTileReference(sObj, c);
                }
                if (c.EventType.Length == 0 && c.Props.TryGetValue("tile", out var tObj))
                {
                    return ResolveTileReference(tObj, c);
                }
            }
            return c.EventType.Length > 0 && c.Floor >= 0 ? c.Floor : c.Tile;
        }

        private int ResolveTileReference(object value, PonderLogicCommand c)
        {
            if (value is List<object> list && list.Count > 0)
            {
                var offset = Convert.ToInt32(list[0]);
                var basis = c.Floor >= 0 ? c.Floor : c.Tile;
                if (list.Count > 1 && list[1] is string reference)
                {
                    if (string.Equals(reference, "Start", StringComparison.OrdinalIgnoreCase))
                    {
                        basis = 0;
                    }
                    else if (string.Equals(reference, "End", StringComparison.OrdinalIgnoreCase))
                    {
                        basis = _preview.TileCount - 1;
                    }
                }
                return basis + offset;
            }
            return Convert.ToInt32(value);
        }

        private static int PropInt(PonderLogicCommand c, string key, int fallback)
        {
            if (c.Props != null && c.Props.TryGetValue(key, out var obj))
            {
                return Convert.ToInt32(obj);
            }
            return fallback;
        }

        private bool UsesOfficial(PonderLogicCommand c, string key)
        {
            if (_official != null)
            {
                return _official.Contains(key) && !_official.IsDisabled(key);
            }
            return c.Props != null && c.Props.ContainsKey(key);
        }

        private static string PropStr(PonderLogicCommand c, string key, string fallback)
        {
            if (c.Props != null && c.Props.TryGetValue(key, out var obj) && obj is string s && s.Length > 0)
            {
                return s;
            }
            return fallback;
        }

        private static float PropFloat(PonderLogicCommand c, string key, float fallback)
        {
            if (c.Props != null && c.Props.TryGetValue(key, out var obj))
            {
                try
                {
                    if (obj is List<object> list && list.Count > 0)
                    {
                        return Convert.ToSingle(list[0]);
                    }
                    return Convert.ToSingle(obj);
                }
                catch
                {
                }
            }
            return fallback;
        }

        /// <summary>位移：原生事件取 Props["positionOffset"]（砖块单位），旧命令取 offsetX/offsetY。</summary>
        private Vector3 Offset(PonderLogicCommand c)
        {
            Vector3 offset;
            if (_official != null && UsesOfficial(c, "positionOffset"))
            {
                var value = _official.GetVector2("positionOffset", Vector2.zero);
                offset = new Vector3(value.x, value.y, 0f);
            }
            else if (c.Props != null && c.Props.TryGetValue("positionOffset", out var posObj) && posObj is List<object> list && list.Count >= 2)
            {
                offset = new Vector3(Convert.ToSingle(list[0]), Convert.ToSingle(list[1]), 0f);
            }
            else
            {
                offset = new Vector3(c.OffsetX, c.OffsetY, 0f);
            }
            return offset * _preview.TileSize;
        }

        /// <summary>装饰物 tag：原生事件取 Props["tag"]，旧命令取 Tag。</summary>
        private static string Tag(PonderLogicCommand c)
        {
            if (c.Props != null && c.Props.TryGetValue("tag", out var tagObj) && tagObj is string tagStr)
            {
                return tagStr;
            }
            return c.Tag;
        }

        private bool TryColor(PonderLogicCommand c, out Color color)
        {
            var colorStr = c.Color;
            if (_official != null && _official.Contains("trackColor") && !_official.IsDisabled("trackColor"))
            {
                color = _official.GetColor("trackColor", Color.white);
                return true;
            }
            if (c.Props != null && c.Props.TryGetValue("trackColor", out var trackObj) && trackObj is string trackStr && trackStr.Length > 0)
            {
                colorStr = trackStr;
            }
            if (!string.IsNullOrEmpty(colorStr) && ColorUtility.TryParseHtmlString(colorStr, out color))
            {
                return true;
            }
            color = Color.white;
            return false;
        }

        /// <summary>缓动：兼容官方 DOTween 缓动名与简写。</summary>
        public static float Ease(float t, string ease)
        {
            return ease switch
            {
                "easeIn" or "InQuad" => t * t,
                "easeOut" or "OutQuad" => 1f - (1f - t) * (1f - t),
                "easeInOut" or "InOutQuad" => t < 0.5f ? 2f * t * t : 1f - Mathf.Pow(-2f * t + 2f, 2f) / 2f,
                "InCubic" => t * t * t,
                "OutCubic" => 1f - Mathf.Pow(1f - t, 3f),
                "InOutCubic" => t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) / 2f,
                "InQuart" => t * t * t * t,
                "OutQuart" => 1f - Mathf.Pow(1f - t, 4f),
                "InOutQuart" => t < 0.5f ? 8f * t * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 4f) / 2f,
                "InQuint" => t * t * t * t * t,
                "OutQuint" => 1f - Mathf.Pow(1f - t, 5f),
                "InOutQuint" => t < 0.5f ? 16f * t * t * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 5f) / 2f,
                "InSine" => 1f - Mathf.Cos(t * Mathf.PI * 0.5f),
                "OutSine" => Mathf.Sin(t * Mathf.PI * 0.5f),
                "InOutSine" => -(Mathf.Cos(Mathf.PI * t) - 1f) / 2f,
                "InExpo" => t <= 0f ? 0f : Mathf.Pow(2f, 10f * t - 10f),
                "OutExpo" => t >= 1f ? 1f : 1f - Mathf.Pow(2f, -10f * t),
                "InOutExpo" => t < 0.5f
                    ? (t <= 0f ? 0f : Mathf.Pow(2f, 20f * t - 10f) * 0.5f)
                    : (t >= 1f ? 1f : (2f - Mathf.Pow(2f, -20f * t + 10f)) * 0.5f),
                "InCirc" => 1f - Mathf.Sqrt(1f - t * t),
                "OutCirc" => Mathf.Sqrt(1f - Mathf.Pow(t - 1f, 2f)),
                "InOutCirc" => t < 0.5f
                    ? (1f - Mathf.Sqrt(1f - Mathf.Pow(2f * t, 2f))) * 0.5f
                    : (Mathf.Sqrt(1f - Mathf.Pow(-2f * t + 2f, 2f)) + 1f) * 0.5f,
                "InBack" =>
                    t * t * (2.70158f * t - 1.70158f),
                "OutBack" =>
                    1f + (t - 1f) * (t - 1f) * (2.70158f * (t - 1f) + 1.70158f),
                "InOutBack" => t < 0.5f
                    ? t * t * (7.189819f * t - 2.5949095f) * 0.5f
                    : ((t -= 1f) * t * (7.189819f * t + 2.5949095f) + 2f) * 0.5f,
                "InBounce" => 1f - BounceOut(1f - t),
                "OutBounce" => BounceOut(t),
                "InOutBounce" => t < 0.5f
                    ? (1f - BounceOut(1f - 2f * t)) * 0.5f
                    : (1f + BounceOut(2f * t - 1f)) * 0.5f,
                _ => t
            };
        }

        private static float BounceOut(float t)
        {
            const float n1 = 7.5625f;
            const float d1 = 2.75f;
            if (t < 1f / d1)
            {
                return n1 * t * t;
            }
            if (t < 2f / d1)
            {
                return n1 * (t -= 1.5f / d1) * t + 0.75f;
            }
            if (t < 2.5f / d1)
            {
                return n1 * (t -= 2.25f / d1) * t + 0.9375f;
            }
            return n1 * (t -= 2.625f / d1) * t + 0.984375f;
        }
    }
}
