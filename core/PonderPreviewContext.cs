using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ponder
{
    /// <summary>隔离官方事件数据与 Ponder 预览状态的边界。</summary>
    public sealed class PonderPreviewContext
    {
        private readonly PonderPreview _preview;

        public PonderPreviewContext(PonderPreview preview)
        {
            _preview = preview ?? throw new ArgumentNullException(nameof(preview));
        }

        public PonderPreview Preview => _preview;
        public int TileCount => _preview.TileCount;
        public float TileSize => _preview.TileSize;

        public Vector3 TileOffset(Vector2 offset)
        {
            return new Vector3(offset.x, offset.y, 0f) * TileSize;
        }

        public PonderOfficialEvent? Decode(PonderLogicCommand command)
        {
            return PonderOfficialEvent.From(command);
        }

        public int ResolveTile(PonderLogicCommand command, object? value, int fallback = 0)
        {
            if (value is Tuple<int, TileRelativeTo> official)
            {
                var basis = command.Floor >= 0 ? command.Floor : fallback;
                if (official.Item2 == TileRelativeTo.Start)
                {
                    basis = 0;
                }
                else if (official.Item2 == TileRelativeTo.End)
                {
                    basis = TileCount - 1;
                }
                return ClampTile(basis + official.Item1);
            }

            if (value is List<object> list && list.Count > 0)
            {
                var basis = command.Floor >= 0 ? command.Floor : fallback;
                if (list.Count > 1 && list[1] is string relative)
                {
                    if (string.Equals(relative, "Start", StringComparison.OrdinalIgnoreCase)) basis = 0;
                    if (string.Equals(relative, "End", StringComparison.OrdinalIgnoreCase)) basis = TileCount - 1;
                }
                return ClampTile(basis + Convert.ToInt32(list[0]));
            }

            try
            {
                return ClampTile(Convert.ToInt32(value ?? fallback));
            }
            catch
            {
                return ClampTile(fallback);
            }
        }

        public (int start, int end) ResolveRange(PonderLogicCommand command, PonderOfficialEvent? official = null)
        {
            var startValue = official?.GetTile("startTile") ?? command.Props?.GetValueOrDefault("startTile");
            var endValue = official?.GetTile("endTile") ?? command.Props?.GetValueOrDefault("endTile");
            var start = ResolveTile(command, startValue, command.Floor >= 0 ? command.Floor : 0);
            var end = ResolveTile(command, endValue, start);
            return start <= end ? (start, end) : (end, start);
        }

        private int ClampTile(int tile)
        {
            return Mathf.Clamp(tile, 0, Mathf.Max(0, TileCount - 1));
        }
    }
}
