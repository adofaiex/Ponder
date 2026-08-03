using UnityEngine;
using UnityEngine.UI;

namespace Ponder
{
    /// <summary>
    /// 指点元素的线：从起点（连文字那端）画到终点（指向目标），终点带箭头 + 可选圆点。
    /// 在父 RectTransform 局部坐标中绘制，父节点应居中放在 start/end 的包围盒上。
    /// </summary>
    public sealed class PonderNoteGraphic : Graphic
    {
        public Vector2 start;
        public Vector2 end;
        public float thickness = 0.06f;
        public float headLength = 0.18f;
        public float headHalf = 0.16f;
        public bool drawCircle = true;
        public float circleRadius = 0.13f;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var dir = end - start;
            var len = dir.magnitude;
            if (len < 0.0001f)
            {
                return;
            }
            var norm = dir / len;
            var perp = new Vector2(-norm.y, norm.x);
            var half = thickness * 0.5f;

            var headLen = Mathf.Min(headLength, len * 0.6f);
            var shaftEnd = end - norm * headLen;

            // 箭杆四边形
            var v0 = start + perp * half;
            var v1 = shaftEnd + perp * half;
            var v2 = shaftEnd - perp * half;
            var v3 = start - perp * half;
            AddVert(vh, v0);
            AddVert(vh, v1);
            AddVert(vh, v2);
            AddVert(vh, v3);
            vh.AddTriangle(0, 1, 2);
            vh.AddTriangle(0, 2, 3);

            // 箭头三角（终点方向）
            var baseA = shaftEnd + perp * headHalf;
            var baseB = shaftEnd - perp * headHalf;
            AddVert(vh, end);
            AddVert(vh, baseA);
            AddVert(vh, baseB);
            vh.AddTriangle(4, 6, 5);

            if (drawCircle)
            {
                AddCircle(vh, end, circleRadius, 20);
            }
        }

        private void AddVert(VertexHelper vh, Vector2 pos)
        {
            vh.AddVert(new UIVertex
            {
                position = pos,
                color = color,
                uv0 = Vector2.zero
            });
        }

        private void AddCircle(VertexHelper vh, Vector2 center, float radius, int segments)
        {
            var baseIndex = vh.currentVertCount;
            AddVert(vh, center);
            for (var i = 0; i < segments; i++)
            {
                var a = Mathf.PI * 2f * i / segments;
                var p = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
                AddVert(vh, p);
            }
            for (var i = 0; i < segments; i++)
            {
                var next = baseIndex + 1 + (i + 1) % segments;
                vh.AddTriangle(baseIndex, baseIndex + 1 + i, next);
            }
        }
    }
}
