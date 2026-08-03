using UnityEngine;
using UnityEngine.UI;

namespace Ponder
{
    /// <summary>
    /// 指点元素的线：从起点（连文字那端）画到终点（指向目标）的一条线段。
    /// 在父 RectTransform 局部坐标中绘制，父节点应居中放在 start/end 的包围盒上。
    /// 不画箭头三角和圆点——指向靠"线本身就指向目标"传达，多余的装饰会盖住砖块细节。
    /// </summary>
    public sealed class PonderNoteGraphic : Graphic
    {
        public Vector2 start;
        public Vector2 end;
        public float thickness = 0.06f;

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

            // 箭杆四边形：从 start 到 end 的等宽线段
            var v0 = start + perp * half;
            var v1 = end + perp * half;
            var v2 = end - perp * half;
            var v3 = start - perp * half;
            AddVert(vh, v0);
            AddVert(vh, v1);
            AddVert(vh, v2);
            AddVert(vh, v3);
            vh.AddTriangle(0, 1, 2);
            vh.AddTriangle(0, 2, 3);
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
    }
}
