# Ponder 自由编辑 (万物皆可 Ponder) 指南

让 Ponder 沙盒里能"任意修改"轨道：要么在 `edits.json` 里**声明式**地写修改，
要么在沙盒内**直接拖动**砖块/装饰物（GUI 拖动为未来工作，目前仅 JSON 声明式生效）。

## 1) JSON edits 声明式

在场景文件夹里放 `edits.json`（整场生效），或在 `chapter_N` 子文件夹里放
`edits.json`（该章节及之后章节生效）。每条 edit 一项：

```json
[
  {
    "kind": "TileExtra",
    "target": { "tile": 1 },
    "value": [1.5, 0.5],
    "chapter": 1
  },
  {
    "kind": "TileScale",
    "target": { "tile": 1 },
    "value": [0.5, 0.5],
    "chapter": 2
  },
  {
    "kind": "TileRot",
    "target": { "tile": 1 },
    "value": [45]
  },
  {
    "kind": "TileColor",
    "target": { "tile": 1 },
    "color": "#FF8800"
  },
  {
    "kind": "DecoExtra",
    "target": { "decoTag": "pin" },
    "value": [0, 1]
  }
]
```

- `kind`：`TileExtra` / `TileScale` / `TileRot` / `TileOpacity` / `TileColor` /
  `DecoExtra` / `DecoScale` / `DecoRot` / `DecoDepth`
- `target`：PonderSelector，支持 `tile` (1-based) / `decoTag` / `name` / `nameContains` /
  `tag` / `component` / `layer` / `event` / `setting` / `image` / `floor`
- `value`：[x, y]，用于平移 / 缩放 / 旋转 / 透明度
- `color`：字符串 `"#RRGGBB"` 或 `[r, g, b, a]`
- `chapter`：>= 0 时表示仅从该章节开始生效；-1（默认）= 全程生效

章节切换时回放：进 chapter N 会 replay 所有 `Chapter <= N` 的 edits（其他 edits 隐藏）。
Ponder 关闭时回滚到 base 状态。

## 2) Note 完全自由定位

`PonderNote` 现在支持两种定位模式（自动二选一）：

- 模式 1 (砖块相对)：填 `textX` / `textY` / `targetX` / `targetY` / `tile`，
  文字锚在砖块基准 + 偏移，砖块移动时线跟着走。
- 模式 2 (世界自由)：填 `worldPos: [x, y]`，文字锚在 Ponder 沙盒世界坐标固定位置，
  不随砖块移动；可选 `worldTarget: [x, y]` 自定义线终点；
  `pivot: [0, 0]..[1, 1]` 决定 RectTransform pivot（默认 [0.5, 0.5] = 中心）。

```json
{
  "notes": [
    { "text": "自由摆放在屏幕角落", "worldPos": [-8, 4], "pivot": [1, 0.5] },
    { "text": "自定义线终点", "worldPos": [0, 5], "worldTarget": [3, 0] }
  ]
}
```

## 3) 沙盒内 GUI 拖动

pending。当前实现只支持 JSON 声明式；沙盒内拖 tile/deco 待后续工作。
