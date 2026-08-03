using System;
using System.Collections.Generic;
using ADOFAI;
using UnityEngine;

namespace Ponder
{
    /// <summary>
    /// 官方 LevelEvent 的主线程适配层。只复用 ADOFAI 的属性解码、默认值和 disabled 语义；
    /// 不直接启动 ffx* 组件，因为那些组件绑定真实关卡和全局 conductor。
    /// </summary>
    public sealed class PonderOfficialEvent
    {
        private readonly LevelEvent _event;

        private PonderOfficialEvent(LevelEvent @event)
        {
            _event = @event;
        }

        public static PonderOfficialEvent? From(PonderLogicCommand command)
        {
            if (command.RawEvent == null || command.EventType.Length == 0)
            {
                return null;
            }
            try
            {
                return new PonderOfficialEvent(new LevelEvent(command.RawEvent));
            }
            catch (Exception ex)
            {
                Main.Handler?.Warning($"Ponder: official event decode failed ({command.EventType}): {ex.Message}");
                return null;
            }
        }

        public bool Contains(string key) => _event.ContainsKey(key);

        public bool IsDisabled(string key)
        {
            return _event.disabled != null && _event.disabled.TryGetValue(key, out var disabled) && disabled;
        }

        public T Get<T>(string key, T fallback = default!)
        {
            return _event.Get(key, fallback);
        }

        public Vector2 GetVector2(string key, Vector2 fallback)
        {
            return _event.TryGet<Vector2>(key, out var value) ? value : fallback;
        }

        public Tuple<int, TileRelativeTo>? GetTile(string key)
        {
            return _event.TryGet<Tuple<int, TileRelativeTo>>(key, out var value) ? value : null;
        }

        public Color GetColor(string key, Color fallback)
        {
            try
            {
                return _event.ContainsKey(key) ? _event.GetColor(key) : fallback;
            }
            catch
            {
                return fallback;
            }
        }
    }
}
