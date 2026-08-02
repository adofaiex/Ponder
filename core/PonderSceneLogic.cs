using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ponder
{
    public sealed class PonderSceneLogic
    {
        private readonly PonderPreview _preview;
        private readonly PonderSceneDef _scene;
        private List<PonderLogicCommand> _commands = new List<PonderLogicCommand>();
        private int _index;
        private float _state;
        private bool _playing;
        private bool _entered;
        private bool _loop;
        private Action? _onComplete;

        private Vector3 _startDelta;
        private int _decoIndex = -1;
        private Color _fromColor;

        public bool IsPlaying => _playing;

        public PonderSceneLogic(PonderPreview preview, PonderSceneDef scene)
        {
            _preview = preview;
            _scene = scene;
        }

        public void Play(List<PonderLogicCommand> commands, bool loop = false, Action? onComplete = null)
        {
            _commands = commands ?? new List<PonderLogicCommand>();
            _index = 0;
            _state = 0f;
            _entered = false;
            _loop = loop;
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
                if (_loop)
                {
                    _preview.ResetTransforms();
                    _index = 0;
                    _state = 0f;
                    _entered = false;
                }
                else
                {
                    _playing = false;
                    var done = _onComplete;
                    _onComplete = null;
                    done?.Invoke();
                }
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
            switch (c.Cmd)
            {
                case "MoveTrack":
                    _startDelta = _preview.GlobalDelta;
                    break;
                case "ScaleTrack":
                    _startDelta = new Vector3(_preview.GlobalScale, 0f, 0f);
                    break;
                case "SetTrackColor":
                    _fromColor = Color.white;
                    break;
                case "SetFloorColor":
                    _fromColor = Color.white;
                    break;
                case "AddDecoration":
                    _decoIndex = _preview.AddDeco(_scene, c.Image, c.Tile, c.OffsetX, c.OffsetY, c.Scale, c.Rotation, c.Style, c.Tag);
                    break;
            }
        }

        private void ApplyCommand(PonderLogicCommand c, float t, float e)
        {
            switch (c.Cmd)
            {
                case "MoveTrack":
                    _preview.SetGlobalDelta(Vector3.Lerp(_startDelta, _startDelta + new Vector3(c.OffsetX, c.OffsetY, 0f), e));
                    break;
                case "PositionTrack":
                    _preview.SetTileExtraDelta(c.Tile, ResolveEnd(c), new Vector3(c.OffsetX, c.OffsetY, 0f) * e);
                    break;
                case "ScaleTrack":
                    _preview.SetGlobalScale(Mathf.Lerp(_startDelta.x, c.Scale, e));
                    break;
                case "AddDecoration":
                    if (_decoIndex >= 0)
                    {
                        _preview.SetDecoAlpha(_decoIndex, e);
                    }
                    break;
                case "MoveDecorations":
                    _preview.SetDecoTaggedDelta(c.Tag, new Vector3(c.OffsetX, c.OffsetY, 0f) * e);
                    break;
                case "SetTrackColor":
                    if (TryColor(c, out var col))
                    {
                        _preview.SetTileColorRange(0, _preview.TileCount - 1, Color.Lerp(_fromColor, col, e));
                    }
                    break;
                case "SetFloorColor":
                    if (TryColor(c, out var col2))
                    {
                        _preview.SetTileColorRange(c.Tile, ResolveEnd(c), Color.Lerp(_fromColor, col2, e));
                    }
                    break;
            }
        }

        private void OnCommandExit(PonderLogicCommand c)
        {
            switch (c.Cmd)
            {
                case "MoveTrack":
                    _preview.SetGlobalDelta(_startDelta + new Vector3(c.OffsetX, c.OffsetY, 0f));
                    break;
                case "PositionTrack":
                    _preview.SetTileExtraDelta(c.Tile, ResolveEnd(c), new Vector3(c.OffsetX, c.OffsetY, 0f));
                    break;
                case "ScaleTrack":
                    _preview.SetGlobalScale(c.Scale);
                    break;
                case "MoveDecorations":
                    _preview.SetDecoTaggedDelta(c.Tag, new Vector3(c.OffsetX, c.OffsetY, 0f));
                    break;
                case "SetTrackColor":
                case "SetFloorColor":
                    if (TryColor(c, out var col))
                    {
                        _preview.SetTileColorRange(c.Cmd == "SetTrackColor" ? 0 : c.Tile,
                            c.Cmd == "SetTrackColor" ? _preview.TileCount - 1 : ResolveEnd(c), col);
                    }
                    break;
                case "SetFloorStyle":
                    if (c.Style >= 0)
                    {
                        _preview.SetTileStyleRange(c.Tile, ResolveEnd(c), c.Style);
                    }
                    break;
            }
            _preview.ApplyTransforms();
        }

        private int ResolveEnd(PonderLogicCommand c)
        {
            return c.EndTile >= 0 ? c.EndTile : c.Tile;
        }

        private static bool TryColor(PonderLogicCommand c, out Color color)
        {
            if (!string.IsNullOrEmpty(c.Color) && ColorUtility.TryParseHtmlString(c.Color, out color))
            {
                return true;
            }
            color = Color.white;
            return false;
        }

        private static float Ease(float t, string ease)
        {
            return ease switch
            {
                "easeIn" => t * t,
                "easeOut" => 1f - (1f - t) * (1f - t),
                "easeInOut" => t < 0.5f ? 2f * t * t : 1f - Mathf.Pow(-2f * t + 2f, 2f) / 2f,
                _ => t
            };
        }
    }
}
