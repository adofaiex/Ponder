using UnityEngine;
using UnityEngine.Sprites;

namespace Ponder
{
    public static class PonderSprites
    {
        private static Sprite? _rounded;

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

        private static Sprite CreateRounded()
        {
            const int size = 64;
            const float radius = 14f;
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
            var border = new Vector4(radius + 4f, radius + 4f, radius + 4f, radius + 4f);
            return Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
        }
    }
}
