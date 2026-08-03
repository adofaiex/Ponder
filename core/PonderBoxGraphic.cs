using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Ponder
{
    /// <summary>
    /// 指点文字的半透明底框：程序化绘制的圆角矩形（方 + 圆角，绝不拉伸成椭圆）。
    /// 在父 RectTransform 局部坐标中绘制，父节点居中放在文字锚点上，sizeDelta 控制尺寸。
    /// </summary>
    public sealed class PonderBoxGraphic : Graphic
    {
        public float cornerRadius = 0.1f;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var rect = rectTransform.rect;
            var w = rect.width;
            var h = rect.height;
            if (w <= 0.0001f || h <= 0.0001f)
            {
                return;
            }

            var halfW = w * 0.5f;
            var halfH = h * 0.5f;
            var r = Mathf.Clamp(cornerRadius, 0f, Mathf.Min(halfW, halfH));

            const int seg = 8;
            // 四个角弧的圆心与起始角度（度），顺时针生成轮廓
            var corners = new[]
            {
                new Vector2(halfW - r, -halfH + r), // 右下
                new Vector2(halfW - r, halfH - r),  // 右上
                new Vector2(-halfW + r, halfH - r), // 左上
                new Vector2(-halfW + r, -halfH + r) // 左下
            };
            var startAngles = new[] { -90f, 0f, 90f, 180f };

            var pts = new List<Vector2>();
            for (var c = 0; c < 4; c++)
            {
                for (var i = 0; i <= seg; i++)
                {
                    var ang = (startAngles[c] + 90f * i / seg) * Mathf.Deg2Rad;
                    pts.Add(corners[c] + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r);
                }
            }

            var baseIdx = vh.currentVertCount;
            vh.AddVert(new UIVertex { position = Vector2.zero, color = color, uv0 = Vector2.zero });
            for (var i = 0; i < pts.Count; i++)
            {
                vh.AddVert(new UIVertex { position = pts[i], color = color, uv0 = Vector2.zero });
            }
            for (var i = 0; i < pts.Count; i++)
            {
                vh.AddTriangle(baseIdx, baseIdx + 1 + i, baseIdx + 1 + (i + 1) % pts.Count);
            }
        }
    }
}
