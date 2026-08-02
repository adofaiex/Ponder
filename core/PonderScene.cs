using System.Collections.Generic;

namespace Ponder
{
    /// <summary>触发选择器：说明要悬停/绑定的 GameObject。</summary>
    public sealed class PonderSelector
    {
        public string Type = "";    // event | setting | decoration | floor
        public string Event = "";   // 事件名，如 MoveTrack
        public string Setting = ""; // 设置类事件名，如 TrackSettings
        public string Tag = "";     // 装饰物 tag
        public string Image = "";   // 装饰物图片
        public bool Floor;

        public string Describe()
        {
            return Type switch
            {
                "event" => $"event:{Event}",
                "setting" => $"setting:{Setting}",
                "decoration" => $"decoration:{Tag}/{Image}",
                "floor" => "floor",
                _ => Type
            };
        }
    }

    /// <summary>绑定：哪个场景 + 哪个选择器。</summary>
    public sealed class PonderBinding
    {
        public string SceneId = "";
        public PonderSelector Selector = new PonderSelector();
    }

    /// <summary>逻辑命令：操作砖块/装饰物，效果类似官方 MoveTrack/PositionTrack。</summary>
    public sealed class PonderLogicCommand
    {
        public string Cmd = "";     // MoveTrack|PositionTrack|ScaleTrack|AddDecoration|MoveDecorations|SetTrackColor|SetFloorColor|SetFloorStyle|Wait
        public float Delay;         // 启动前等待（秒）
        public float Duration = 1f; // 持续时间（秒）
        public string Ease = "linear"; // linear|easeIn|easeOut|easeInOut

        public float OffsetX;       // 平移量（砖块单位）
        public float OffsetY;
        public int Tile;            // 起始砖
        public int EndTile = -1;    // 结束砖（-1=单块）
        public float Scale = 1f;    // 缩放倍率
        public string Color = "";   // 十六进制颜色
        public int Style;           // 轨道样式
        public string Tag = "";     // 装饰物 tag
        public string Image = "";   // 装饰物图片
        public float Rotation;      // 装饰物旋转
        public bool Loop;           // 播完自动重播
    }

    public sealed class PonderChapter
    {
        public int Floor;
        public string Text;
        public List<PonderLogicCommand> Logic = new List<PonderLogicCommand>();
    }

    public sealed class PonderSceneTile
    {
        public float Angle = 180f;      // 度，ado 角度
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
}
