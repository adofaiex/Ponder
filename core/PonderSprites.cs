using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Sprites;

namespace Ponder
{
    public static class PonderSprites
    {
        private static Sprite? _rounded;
        private static readonly Dictionary<string, Sprite> _builtin = new Dictionary<string, Sprite>();

        public static Sprite Rounded
        {
            get
            {
                if (_rounded == null)
                {
                    _rounded = CreateRounded();
                }
                return _rounded;
            }
        }

        /// <summary>
        /// 取一张内置的 sprite（程序生成，不依赖磁盘文件）。
        /// key 例：<c>arrow</c> / <c>dot</c> / <c>ring</c>。
        /// </summary>
        public static Sprite? Builtin(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return null;
            }
            if (_builtin.TryGetValue(key, out var cached))
            {
                return cached;
            }
            Sprite? s = key switch
            {
                "arrow" => CreateArrow(),
                "dot" => CreateDot(),
                "ring" => CreateRing(),
                _ => null
            };
            if (s != null)
            {
                _builtin[key] = s;
            }
            return s;
        }

        private static Sprite CreateArrow()
        {
            const int w = 96, h = 96;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp
            };
            // 纯白三角形（顶点朝右），任何 color 都能染色
            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    var u = (x - w * 0.5f) / (w * 0.5f);   // -1..1
                    var v = (y - h * 0.5f) / (h * 0.5f);   // -1..1
                    // 三角：x 在 [0, 1-u*0.6] 区间内，|v| < (1-x)
                    var inside = u >= 0f && u <= 1f && Mathf.Abs(v) < (1f - u) * 0.95f;
                    // 圆头：x < 0，根号距离 < 0.55
                    var d = u < 0f ? Mathf.Sqrt(u * u + v * v) : 999f;
                    var head = d < 0.55f;
                    var a = (inside || head) ? 1f : 0f;
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
        }

        private static Sprite CreateDot()
        {
            const int w = 64, h = 64;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var half = w * 0.5f;
            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    var dx = x + 0.5f - half;
                    var dy = y + 0.5f - half;
                    var d = Mathf.Sqrt(dx * dx + dy * dy);
                    var a = Mathf.Clamp01(half - d);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
        }

        private static Sprite CreateRing()
        {
            const int w = 64, h = 64;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var half = w * 0.5f;
            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    var dx = x + 0.5f - half;
                    var dy = y + 0.5f - half;
                    var d = Mathf.Sqrt(dx * dx + dy * dy);
                    var a = Mathf.Clamp01(2f - Mathf.Abs(d - half * 0.7f));
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
        }

        private static Sprite CreateRounded()
        {
            const int size = 64;
            const float radius = 18f;  // 圆角更明显，按钮小时仍能看出圆角
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp
            };
            var half = size * 0.5f;
            var box = half - radius - 1.5f;
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var dx = Mathf.Abs(x + 0.5f - half) - box;
                    var dy = Mathf.Abs(y + 0.5f - half) - box;
                    var ax = Mathf.Max(dx, 0f);
                    var ay = Mathf.Max(dy, 0f);
                    var dist = Mathf.Sqrt(ax * ax + ay * ay) + Mathf.Min(Mathf.Max(dx, dy), 0f) - radius;
                    var alpha = Mathf.Clamp01(0.5f - dist);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }
            tex.Apply();
            // border 必须 >= radius，否则 sliced 时四个角被裁切，圆角变直角。
            var border = new Vector4(radius + 4f, radius + 4f, radius + 4f, radius + 4f);
            return Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
        }
    }
}
