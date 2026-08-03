using UnityEngine;
using UnityEngine.UI;

namespace Ponder
{
    /// <summary>
    /// UGUI 程序化画线的箭头（仿 ADOFAI UILineRenderer：Graphic + OnPopulateMesh + VertexHelper），
    /// 不再依赖 arrow.png 图片文件。默认指向 +X，旋转/缩放通过 RectTransform 控制。
    /// </summary>
    public sealed class PonderArrowGraphic : Graphic
    {
        public float headLength = 0.34f;   // 箭头头部占整段长度的比例
        public float headHalf = 0.42f;     // 头部半高占矩形高度比例
        public float shaftThickness = 0.10f; // 箭杆厚度占矩形高度比例

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            float w = rectTransform.rect.width;
            float h = rectTransform.rect.height;
            if (w <= 0.001f || h <= 0.001f)
            {
                return;
            }

            var halfW = w * 0.5f;
            var halfH = h * 0.5f;
            var headLen = w * Mathf.Clamp01(headLength);
            var headHalfY = halfH * Mathf.Clamp01(headHalf);
            var shaftHalfY = halfH * Mathf.Clamp01(shaftThickness);
            var shaftEndX = halfW - headLen;

            // 箭杆四边形
            vh.AddVert(new UIVertex
            {
                position = new Vector3(-halfW, -shaftHalfY),
                color = color,
                uv0 = Vector2.zero
            });
            vh.AddVert(new UIVertex
            {
                position = new Vector3(shaftEndX, -shaftHalfY),
                color = color,
                uv0 = Vector2.zero
            });
            vh.AddVert(new UIVertex
            {
                position = new Vector3(shaftEndX, shaftHalfY),
                color = color,
                uv0 = Vector2.zero
            });
            vh.AddVert(new UIVertex
            {
                position = new Vector3(-halfW, shaftHalfY),
                color = color,
                uv0 = Vector2.zero
            });

            // 箭头三角
            vh.AddVert(new UIVertex
            {
                position = new Vector3(halfW, 0f),
                color = color,
                uv0 = Vector2.zero
            });
            vh.AddVert(new UIVertex
            {
                position = new Vector3(shaftEndX, -headHalfY),
                color = color,
                uv0 = Vector2.zero
            });
            vh.AddVert(new UIVertex
            {
                position = new Vector3(shaftEndX, headHalfY),
                color = color,
                uv0 = Vector2.zero
            });

            // 箭杆 2 个三角
            vh.AddTriangle(0, 1, 2);
            vh.AddTriangle(0, 2, 3);
            // 箭头 1 个三角（逆时针绕向摄像机）
            vh.AddTriangle(4, 6, 5);
        }
    }
}
