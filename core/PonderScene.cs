using System.Collections.Generic;
using UnityEngine;

namespace Ponder
{
    /// <summary>
    /// 通用 GameObject 身份选择器：声明要匹配对象身上的哪些身份属性（任意组合，全部命中即匹配）。
    /// 身份属性：名称、tag、组件类型、层级、事件/设置类型、装饰物 tag/图片、是否是砖块。
    /// </summary>
    public sealed class PonderSelector
    {
        public string Name = "";        // GameObject 名称（精确）
        public string NameContains = ""; // GameObject 名称包含
        public string Tag = "";         // GameObject tag 或装饰物逻辑 tag
        public string Component = "";   // 组件类型名，如 scrFloor / scrDecoration
        public string Layer = "";       // 层级名，如 Floor / Foreground
        public string Event = "";       // 事件类型，如 MoveTrack
        public string Setting = "";     // 设置类事件类型
        public string Image = "";       // 装饰物图片
        public bool Floor;              // 是否砖块
        public int Tile;                // 砖块序号 (1-based)；0=不按序号匹配
        public string DecoTag = "";     // 装饰物 tag (Ponder 沙盒内 Decos 的 tag)

        /// <summary>是否声明了任何匹配条件（空选择器永不匹配）。</summary>
        public bool HasCriteria =>
            !string.IsNullOrEmpty(Name) ||
            !string.IsNullOrEmpty(NameContains) ||
            !string.IsNullOrEmpty(Tag) ||
            !string.IsNullOrEmpty(Component) ||
            !string.IsNullOrEmpty(Layer) ||
            !string.IsNullOrEmpty(Event) ||
            !string.IsNullOrEmpty(Setting) ||
            !string.IsNullOrEmpty(Image) ||
            !string.IsNullOrEmpty(DecoTag) ||
            Floor ||
            Tile != 0;

        /// <summary>身份完全相等（用于悬停变化检测，比较全部身份字段）。</summary>
        public bool IdentityEquals(PonderSelector other)
        {
            if (other == null)
            {
                return false;
            }
            return Name == other.Name &&
                NameContains == other.NameContains &&
                Tag == other.Tag &&
                Component == other.Component &&
                Layer == other.Layer &&
                Event == other.Event &&
                Setting == other.Setting &&
                Image == other.Image &&
                DecoTag == other.DecoTag &&
                Floor == other.Floor &&
                Tile == other.Tile;
        }

        public string Describe()
        {
            var parts = new List<string>();
            if (Name.Length > 0) parts.Add($"name:{Name}");
            if (NameContains.Length > 0) parts.Add($"name~:{NameContains}");
            if (Tag.Length > 0) parts.Add($"tag:{Tag}");
            if (Component.Length > 0) parts.Add($"component:{Component}");
            if (Layer.Length > 0) parts.Add($"layer:{Layer}");
            if (Event.Length > 0) parts.Add($"event:{Event}");
            if (Setting.Length > 0) parts.Add($"setting:{Setting}");
            if (Image.Length > 0) parts.Add($"image:{Image}");
            if (DecoTag.Length > 0) parts.Add($"decoTag:{DecoTag}");
            if (Floor) parts.Add("floor");
            if (Tile != 0) parts.Add($"tile:{Tile}");
            return parts.Count > 0 ? string.Join(" ", parts) : "(any)";
        }
    }

    /// <summary>绑定：哪个场景 + 哪个选择器。</summary>
    public sealed class PonderBinding
    {
        public string SceneId = "";
        public PonderSelector Selector = new PonderSelector();
    }

    /// <summary>
    /// 逻辑命令。支持两种写法：
    /// 1. 原生 ADOFAI 事件：{ floor, eventType, ...extraProps } —— EventType/Floor/Props 填充；
    /// 2. 沙盒命令：{ cmd, ... } —— Cmd 填充（AddTiles/Wait/...），用于增删轨道等结构性操作。
    /// </summary>
    public sealed class PonderLogicCommand
    {
        public string EventType = "";              // 原生事件类型名（如 MoveTrack / PositionTrack / RecolorTrack）
        public int Floor = -1;                     // 原生事件所在砖块
        public Dictionary<string, object>? Props;  // 原生 extraProps（positionOffset / tag / trackColor ...）
        public Dictionary<string, object>? RawEvent; // 原生事件完整 JSON，延迟到主线程交给官方 LevelEvent 解析

        public string Cmd = "";                    // 沙盒命令：AddTiles | Wait | SetFloorStyle | ScaleTrack ...
        public List<PonderSceneTile>? Tiles;       // AddTiles 用的新砖块

        public float Delay;         // 启动前等待（秒）
        public float Duration = 1f; // 持续时间（秒）
        public string Ease = "linear"; // linear|easeIn|easeOut|easeInOut|官方 ease 名

        public float OffsetX;       // 兼容旧命令：平移量（砖块单位）
        public float OffsetY;
        public int Tile;            // 兼容旧命令：起始砖
        public bool HasTile;        // 是否显式写了 tile/startTile（AddTiles 用它定位插入起点，缺省=最后一块）
        public int EndTile = -1;    // 结束砖（-1=单块）
        public float Scale = 1f;    // 缩放倍率
        public string Color = "";   // 十六进制颜色
        public int Style;           // 轨道样式
        public string Tag = "";     // 装饰物 tag
        public string Image = "";   // 装饰物图片
        public float Rotation;      // 装饰物旋转
        public bool Loop;           // 播完自动重播

        /// <summary>命令名（原生事件名或沙盒命令名），用于调度。</summary>
        public string Name => EventType.Length > 0 ? EventType : Cmd;
    }

    public sealed class PonderChapter
    {
        public int Floor;
        public string Text;
        public List<PonderLogicCommand> Logic = new List<PonderLogicCommand>();
        public List<PonderNote> Notes = new List<PonderNote>();
    }

    /// <summary>
    /// 指点元素：一条线 + 一段文字。线的一端（起点）连文字，另一端（终点/箭头）指向目标。
    /// 两种定位模式：
    ///   1) 砖块相对：所有坐标是相对锚定砖块 Tile 的偏移（砖块单位），砖块移动时线/文字跟着走。
    ///   2) 世界自由：WorldPos (x≠-1) 直接给出文字的世界坐标 + Pivot (RectTransform 0..1 pivot)，
    ///      WorldTarget (x≠-1000) 给出线终点的世界坐标；线和文字固定在 Ponder 场景内，不随砖块移动。
    /// 模式 1 是缺省（兼容旧 JSON）；模式 2 通过 worldPos/worldTarget 启用，文字可以摆在屏幕任意位置。
    /// </summary>
    public sealed class PonderNote
    {
        public string Text = "";         // 线头文字
        public int Tile;                 // 锚定砖块 (模式 1)
        public float TargetX;            // 线终点（箭头）相对锚点偏移 X
        public float TargetY;            // 线终点（箭头）相对锚点偏移 Y
        public float TextX;              // 文字位置偏移 X
        public float TextY;              // 文字位置偏移 Y
        public float LineStartX = float.NaN;  // 线起点（连文字那端），缺省 = 文字位置
        public float LineStartY = float.NaN;
        public float LineEndX = float.NaN;    // 线终点（箭头那端），缺省 = 目标位置
        public float LineEndY = float.NaN;
        public string Color = "#FFFFFF";
        public bool ShowText = true;     // 是否显示线头文字

        // 模式 2：世界自由定位。WorldPos.x < 0 视为未启用，强制走模式 1。
        public Vector2 WorldPos = new Vector2(-1f, -1f);
        public Vector2 WorldTarget = new Vector2(-1000f, -1000f);
        // RectTransform pivot (0..1, 0.5=center)：决定 worldPos 对应文字框的哪个角。
        public Vector2 Pivot = new Vector2(0.5f, 0.5f);
    }

    public sealed class PonderSceneTile
    {
        public float Angle = 0f;        // 方向数据（angleData，度）：tile 的朝向，非相对转角
        public int Style;
        public bool Midspin;
    }

    public sealed class PonderSceneDeco
    {
        public string Image;
        public int Tile;
        public float OffsetX;
        public float OffsetY;
        public float Scale = 1f;
        public float Rotation;
        public int Depth;
        public string Tag;
    }

    public sealed class PonderSceneDef
    {
        public string Id;
        public string Folder;           // 场景文件夹绝对路径
        public string Title;
        public string Description;
        public float Zoom = 4f;
        public float CenterX;
        public float CenterY;
        public float SpawnStagger = 0.15f;  // 开场砖块逐块弹出的间隔（秒），0=全部立刻出现
        public List<PonderSceneTile> Tiles = new List<PonderSceneTile>();
        public List<PonderSceneDeco> Decos = new List<PonderSceneDeco>();
        public List<PonderLogicCommand> Logic = new List<PonderLogicCommand>();   // 开场播放
        public List<PonderChapter> Chapters = new List<PonderChapter>();
        public PonderSelector Target = new PonderSelector();                     // 场景自带触发目标

        public PonderChapter ChapterAt(int index)
        {
            if (Chapters == null || Chapters.Count == 0)
            {
                return null;
            }
            if (index < 0)
            {
                index = 0;
            }
            if (index >= Chapters.Count)
            {
                index = Chapters.Count - 1;
            }
            return Chapters[index];
        }
    }

    /// <summary>
    /// 一次 Ponder 沙盒编辑（MC Ponder 风格"万物皆可 Ponder"）。
    /// 设计师既可以写在 JSON 里做声明式编辑，也可以由用户在 Ponder 沙盒内拖动时自动记录。
    /// 进入 Ponder 时先 Snapshot，章节推进 / 关闭时按 record 顺序 apply。
    /// </summary>
    public sealed class PonderEdit
    {
        public string Kind = "";          // TileExtra | TileScale | TileRot | TileOpacity | TileColor |
                                          // DecoExtra | DecoScale | DecoRot | DecoDepth
        public PonderSelector Target = new PonderSelector();
        public Vector2 Value;             // 平移 / 缩放 / 旋转
        public Color ColorValue;          // 颜色 (Kind=TileColor 时用)
        public int IntValue;              // 深度等整数 (Kind=DecoDepth 时用)
        public int Chapter = -1;          // 哪个章节产生此编辑（-1=全程 / 开场）
        public float At = -1f;            // 章节内时间 (秒)，-1=立刻 apply
    }
}
