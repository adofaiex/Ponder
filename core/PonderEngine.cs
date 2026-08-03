using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using GDMiniJSON;
using UnityEngine;

namespace Ponder
{
    public static class PonderJson
    {
        public static Dictionary<string, object>? GetDict(object value)
        {
            return value as Dictionary<string, object>;
        }

        public static List<object>? GetList(object value)
        {
            return value as List<object>;
        }

        public static string GetString(Dictionary<string, object>? dict, string key, string fallback = "")
        {
            if (dict != null && dict.TryGetValue(key, out var value) && value is string str)
            {
                return str;
            }
            return fallback;
        }

        public static int GetInt(Dictionary<string, object>? dict, string key, int fallback = 0)
        {
            if (dict != null && dict.TryGetValue(key, out var value))
            {
                if (value is string s)
                {
                    return int.TryParse(s, out var parsed) ? parsed : fallback;
                }
                if (value is bool)
                {
                    return fallback;
                }
                try
                {
                    return Convert.ToInt32(value);
                }
                catch
                {
                }
            }
            return fallback;
        }

        public static float GetFloat(Dictionary<string, object>? dict, string key, float fallback = 0f)
        {
            if (dict != null && dict.TryGetValue(key, out var value))
            {
                if (value is string s)
                {
                    return float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
                        ? parsed
                        : fallback;
                }
                if (value is bool)
                {
                    return fallback;
                }
                try
                {
                    return Convert.ToSingle(value);
                }
                catch
                {
                }
            }
            return fallback;
        }

        public static bool GetBool(Dictionary<string, object>? dict, string key, bool fallback = false)
        {
            if (dict != null && dict.TryGetValue(key, out var value))
            {
                if (value is bool b)
                {
                    return b;
                }
                if (value is string s && bool.TryParse(s, out var parsed))
                {
                    return parsed;
                }
            }
            return fallback;
        }
    }

    public static class PonderEngine
    {
        private static bool _scanning;
        private static bool _scanThreadDone;

        private static readonly Dictionary<string, PonderSceneDef> _scenes = new Dictionary<string, PonderSceneDef>();
        private static readonly List<PonderBinding> _bindings = new List<PonderBinding>();
        private static readonly Dictionary<string, Sprite> _imageCache = new Dictionary<string, Sprite>();

        public static bool IsScanning => _scanning;

        public static IReadOnlyDictionary<string, PonderSceneDef> Scenes => _scenes;
        public static IReadOnlyList<PonderBinding> Bindings => _bindings;

        public static void StartBackgroundScan()
        {
            if (_scanning || _scanThreadDone)
            {
                return;
            }
            _scanning = true;
            _scanThreadDone = false;
            var thread = new Thread(ScanWorker)
            {
                IsBackground = true,
                Name = "PonderScan"
            };
            thread.Start();
        }

        private static void ScanWorker()
        {
            try
            {
                var folder = ResourceLoader.ResourcesPath;
                var scenes = new Dictionary<string, PonderSceneDef>();
                var bindings = new List<PonderBinding>();
                if (Directory.Exists(folder))
                {
                    foreach (var indexFile in Directory.GetFiles(Path.Combine(folder, "Ponder"), "index.json", SearchOption.AllDirectories))
                    {
                        var def = ParseScene(indexFile);
                        if (def != null)
                        {
                            scenes[def.Id] = def;
                        }
                    }
                    var bindingFile = Path.Combine(folder, "Ponder", "bindings.json");
                    if (File.Exists(bindingFile))
                    {
                        ParseBindings(bindingFile, bindings);
                    }
                }
                lock (_scenes)
                {
                    _scenes.Clear();
                    foreach (var kv in scenes)
                    {
                        _scenes[kv.Key] = kv.Value;
                    }
                    _bindings.Clear();
                    _bindings.AddRange(bindings);
                }
            }
            catch (Exception ex)
            {
                Main.Handler?.Error($"PonderEngine scan failed\n{ex}");
            }
            finally
            {
                _scanning = false;
                _scanThreadDone = true;
            }
        }

        private static void ParseBindings(string bindingFile, List<PonderBinding> bindings)
        {
            try
            {
                var text = File.ReadAllText(bindingFile);
                var raw = Json.Deserialize(text) as Dictionary<string, object>;
                var list = raw != null ? PonderJson.GetList(raw.GetValueOrDefault("bindings")) : null;
                if (list == null)
                {
                    return;
                }
                foreach (var item in list)
                {
                    var b = PonderJson.GetDict(item);
                    if (b == null)
                    {
                        continue;
                    }
                    var selectorDict = PonderJson.GetDict(b.GetValueOrDefault("selector"));
                    if (selectorDict == null)
                    {
                        continue;
                    }
                    var binding = new PonderBinding
                    {
                        SceneId = PonderJson.GetString(b, "scene")
                    };
                    ParseSelector(selectorDict, binding.Selector);
                    if (!string.IsNullOrEmpty(binding.SceneId))
                    {
                        bindings.Add(binding);
                    }
                }
            }
            catch (Exception ex)
            {
                Main.Handler?.Error($"Ponder: failed to parse bindings\n{ex}");
            }
        }

        private static void ParseSelector(Dictionary<string, object> dict, PonderSelector selector)
        {
            selector.Name = PonderJson.GetString(dict, "name");
            selector.NameContains = PonderJson.GetString(dict, "nameContains");
            selector.Tag = PonderJson.GetString(dict, "tag");
            selector.Component = PonderJson.GetString(dict, "component");
            selector.Layer = PonderJson.GetString(dict, "layer");
            selector.Event = PonderJson.GetString(dict, "event");
            selector.Setting = PonderJson.GetString(dict, "setting");
            selector.Image = PonderJson.GetString(dict, "image");
            selector.Floor = PonderJson.GetBool(dict, "floor", false);
            if (selector.Event.Length == 0 && selector.Setting.Length == 0 && selector.Tag.Length == 0 &&
                selector.Image.Length == 0 && !selector.Floor && selector.Name.Length == 0 &&
                selector.NameContains.Length == 0 && selector.Component.Length == 0 && selector.Layer.Length == 0)
            {
                var type = PonderJson.GetString(dict, "type", "");
                switch (type)
                {
                    case "event":
                        selector.Event = PonderJson.GetString(dict, "event");
                        break;
                    case "setting":
                        selector.Setting = PonderJson.GetString(dict, "setting");
                        break;
                    case "decoration":
                        selector.Tag = PonderJson.GetString(dict, "tag");
                        selector.Image = PonderJson.GetString(dict, "image");
                        break;
                    case "floor":
                        selector.Floor = true;
                        break;
                }
            }
        }

        private static PonderSceneDef? ParseScene(string indexFile)
        {
            try
            {
                var text = File.ReadAllText(indexFile);
                var raw = Json.Deserialize(text) as Dictionary<string, object>;
                if (raw == null)
                {
                    Main.Handler?.Error($"Ponder: bad index.json {indexFile}");
                    return null;
                }
                var def = new PonderSceneDef
                {
                    Id = PonderJson.GetString(raw, "id"),
                    Folder = Path.GetDirectoryName(indexFile)!,
                    Title = PonderLang.Text(raw.GetValueOrDefault("title")),
                    Description = PonderLang.Text(raw.GetValueOrDefault("description")),
                    Zoom = PonderJson.GetFloat(raw, "zoom", 4f),
                    CenterX = PonderJson.GetFloat(raw, "centerX"),
                    CenterY = PonderJson.GetFloat(raw, "centerY"),
                    SpawnStagger = PonderJson.GetFloat(raw, "spawnStagger", 0.15f)
                };
                if (string.IsNullOrEmpty(def.Title))
                {
                    def.Title = def.Id;
                }

                if (PonderJson.GetList(raw.GetValueOrDefault("tiles")) is { } tiles)
                {
                    foreach (var item in tiles)
                    {
                        if (PonderJson.GetDict(item) is { } t)
                        {
                            def.Tiles.Add(new PonderSceneTile
                            {
                                Angle = PonderJson.GetFloat(t, "angle", 0f),
                                Style = PonderJson.GetInt(t, "style", 0),
                                Midspin = PonderJson.GetBool(t, "midspin", false)
                            });
                        }
                    }
                }

                if (PonderJson.GetList(raw.GetValueOrDefault("decos")) is { } decos)
                {
                    foreach (var item in decos)
                    {
                        if (PonderJson.GetDict(item) is { } d)
                        {
                            def.Decos.Add(new PonderSceneDeco
                            {
                                Image = PonderJson.GetString(d, "image"),
                                Tile = PonderJson.GetInt(d, "tile"),
                                OffsetX = PonderJson.GetFloat(d, "offsetX"),
                                OffsetY = PonderJson.GetFloat(d, "offsetY"),
                                Scale = PonderJson.GetFloat(d, "scale", 1f),
                                Rotation = PonderJson.GetFloat(d, "rotation"),
                                Depth = PonderJson.GetInt(d, "depth"),
                                Tag = PonderJson.GetString(d, "tag")
                            });
                        }
                    }
                }

                def.Logic = ParseLogic(PonderJson.GetList(raw.GetValueOrDefault("logic")));

                if (PonderJson.GetDict(raw.GetValueOrDefault("target")) is { } target)
                {
                    ParseSelector(target, def.Target);
                }

                if (PonderJson.GetList(raw.GetValueOrDefault("chapters")) is { } chapters)
                {
                    foreach (var item in chapters)
                    {
                        if (PonderJson.GetDict(item) is { } c)
                        {
                            def.Chapters.Add(new PonderChapter
                            {
                                Floor = PonderJson.GetInt(c, "floor"),
                                Text = PonderLang.Text(c.GetValueOrDefault("text")),
                                Logic = ParseLogic(PonderJson.GetList(c.GetValueOrDefault("logic"))),
                                Notes = ParseNotes(PonderJson.GetList(c.GetValueOrDefault("notes")))
                            });
                        }
                    }
                }
                return def;
            }
            catch (Exception ex)
            {
                Main.Handler?.Error($"Ponder: failed to parse {indexFile}\n{ex}");
                return null;
            }
        }

        private static List<PonderNote> ParseNotes(List<object>? list)
        {
            var notes = new List<PonderNote>();
            if (list == null)
            {
                return notes;
            }
            foreach (var item in list)
            {
                if (PonderJson.GetDict(item) is not { } d)
                {
                    continue;
                }
                var note = new PonderNote
                {
                    Text = PonderLang.Text(d.GetValueOrDefault("text")),
                    Tile = PonderJson.GetInt(d, "tile"),
                    TargetX = PonderJson.GetFloat(d, "targetX"),
                    TargetY = PonderJson.GetFloat(d, "targetY"),
                    TextX = PonderJson.GetFloat(d, "textX"),
                    TextY = PonderJson.GetFloat(d, "textY"),
                    Color = PonderJson.GetString(d, "color", "#FFFFFF"),
                    ShowText = PonderJson.GetBool(d, "showText", true)
                };
                var lineStart = PonderJson.GetList(d.GetValueOrDefault("lineStart"));
                if (lineStart != null && lineStart.Count >= 2)
                {
                    note.LineStartX = (float)Convert.ToDouble(lineStart[0]);
                    note.LineStartY = (float)Convert.ToDouble(lineStart[1]);
                }
                var lineEnd = PonderJson.GetList(d.GetValueOrDefault("lineEnd"));
                if (lineEnd != null && lineEnd.Count >= 2)
                {
                    note.LineEndX = (float)Convert.ToDouble(lineEnd[0]);
                    note.LineEndY = (float)Convert.ToDouble(lineEnd[1]);
                }
                notes.Add(note);
            }
            return notes;
        }

        public static List<PonderLogicCommand> ParseLogic(List<object>? list)
        {
            var result = new List<PonderLogicCommand>();
            if (list == null)
            {
                return result;
            }
            foreach (var item in list)
            {
                var d = PonderJson.GetDict(item);
                if (d == null)
                {
                    continue;
                }
                var eventType = PonderJson.GetString(d, "eventType");
                if (eventType.Length > 0)
                {
                    result.Add(ParseNativeEvent(d, eventType));
                    continue;
                }
                var cmd = ParseSandboxCommand(d);
                if (cmd != null)
                {
                    result.Add(cmd);
                }
            }
            return result;
        }

        /// <summary>原生事件：{ floor, eventType, ...extraProps }，extraProps 原样保留供运行时用官方 LevelEvent 解析。</summary>
        private static PonderLogicCommand ParseNativeEvent(Dictionary<string, object> d, string eventType)
        {
            var props = new Dictionary<string, object>();
            foreach (var kv in d)
            {
                if (kv.Key == "floor" || kv.Key == "eventType" || kv.Key == "delay" ||
                    kv.Key == "duration" || kv.Key == "ease" || kv.Key == "loop")
                {
                    continue;
                }
                props[kv.Key] = kv.Value;
            }
            var cmd = new PonderLogicCommand
            {
                EventType = eventType,
                RawEvent = new Dictionary<string, object>(d),
                Floor = PonderJson.GetInt(d, "floor", -1),
                Delay = PonderJson.GetFloat(d, "delay"),
                Duration = PonderJson.GetFloat(d, "duration", 1f),
                Ease = PonderJson.GetString(d, "ease", "linear"),
                Loop = PonderJson.GetBool(d, "loop", false),
                Props = props
            };
            if (props.TryGetValue("tiles", out var tilesObj) && PonderJson.GetList(tilesObj) is { } tileList)
            {
                cmd.Tiles = ParseTiles(tileList);
            }
            return cmd;
        }

        /// <summary>沙盒命令：{ cmd: "AddTiles"|"Wait"|"SetFloorStyle"|"ScaleTrack"|... }，兼容旧 offsetX/offsetY 写法。</summary>
        private static PonderLogicCommand? ParseSandboxCommand(Dictionary<string, object> d)
        {
            var cmdName = PonderJson.GetString(d, "cmd");
            if (cmdName.Length == 0)
            {
                return null;
            }
            var cmd = new PonderLogicCommand
            {
                Cmd = cmdName,
                Delay = PonderJson.GetFloat(d, "delay"),
                Duration = PonderJson.GetFloat(d, "duration", 1f),
                Ease = PonderJson.GetString(d, "ease", "linear"),
                OffsetX = PonderJson.GetFloat(d, "offsetX"),
                OffsetY = PonderJson.GetFloat(d, "offsetY"),
                Tile = d.ContainsKey("startTile") ? PonderJson.GetInt(d, "startTile") : PonderJson.GetInt(d, "tile"),
                HasTile = d.ContainsKey("tile") || d.ContainsKey("startTile"),
                EndTile = PonderJson.GetInt(d, "endTile", -1),
                Scale = PonderJson.GetFloat(d, "scale", 1f),
                Color = PonderJson.GetString(d, "color"),
                Style = PonderJson.GetInt(d, "style"),
                Tag = PonderJson.GetString(d, "tag"),
                Image = PonderJson.GetString(d, "image"),
                Rotation = PonderJson.GetFloat(d, "rotation"),
                Loop = PonderJson.GetBool(d, "loop", false)
            };
            if (PonderJson.GetList(d.GetValueOrDefault("tiles")) is { } tileList)
            {
                cmd.Tiles = ParseTiles(tileList);
            }
            return cmd;
        }

        private static List<PonderSceneTile> ParseTiles(List<object> tileList)
        {
            var tiles = new List<PonderSceneTile>();
            foreach (var item in tileList)
            {
                if (PonderJson.GetDict(item) is { } t)
                {
                    tiles.Add(new PonderSceneTile
                    {
                        Angle = PonderJson.GetFloat(t, "angle", 0f),
                        Style = PonderJson.GetInt(t, "style", 0),
                        Midspin = PonderJson.GetBool(t, "midspin", false)
                    });
                }
            }
            return tiles;
        }

        /// <summary>根据当前悬停选择器，返回命中的绑定场景。</summary>
        public static List<PonderSceneDef> FindScenes(PonderSelector hover)
        {
            var result = new List<PonderSceneDef>();
            if (hover == null || !hover.HasCriteria)
            {
                return result;
            }

            List<PonderBinding> bindings;
            List<PonderSceneDef> scenes;
            lock (_scenes)
            {
                bindings = new List<PonderBinding>(_bindings);
                scenes = new List<PonderSceneDef>(_scenes.Values);
            }

            var seen = new HashSet<string>();
            var scenesById = new Dictionary<string, PonderSceneDef>();
            foreach (var s in scenes)
            {
                if (s != null && s.Id != null)
                {
                    scenesById[s.Id] = s;
                }
            }

            void AddScene(PonderSceneDef s)
            {
                if (s != null && s.Target != null && Matches(s.Target, hover) && seen.Add(s.Id))
                {
                    result.Add(s);
                }
            }

            foreach (var scene in scenes)
            {
                AddScene(scene);
            }

            foreach (var binding in bindings)
            {
                var s = binding.Selector;
                if (!Matches(s, hover))
                {
                    continue;
                }
                if (scenesById.TryGetValue(binding.SceneId, out var scene) && seen.Add(scene.Id))
                {
                    result.Add(scene);
                }
            }
            return result;
        }

        /// <summary>
        /// 通用身份匹配：target 声明的每一条身份条件都必须被 hover（悬停对象的实际身份）满足。
        /// 事件/设置类型匹配时，Editor 里把事件面板当作一个"身份"对象（component:InspectorPanel + event/Setting）。
        /// </summary>
        public static bool Matches(PonderSelector target, PonderSelector hover)
        {
            if (target == null || hover == null || !target.HasCriteria)
            {
                return false;
            }
            if (target.Name.Length > 0 && !string.Equals(target.Name, hover.Name, StringComparison.Ordinal))
            {
                return false;
            }
            if (target.NameContains.Length > 0 &&
                (hover.Name == null || hover.Name.IndexOf(target.NameContains, StringComparison.OrdinalIgnoreCase) < 0))
            {
                return false;
            }
            if (target.Tag.Length > 0 && !string.Equals(target.Tag, hover.Tag, StringComparison.Ordinal))
            {
                return false;
            }
            if (target.Component.Length > 0 && !string.Equals(target.Component, hover.Component, StringComparison.Ordinal))
            {
                return false;
            }
            if (target.Layer.Length > 0 && !string.Equals(target.Layer, hover.Layer, StringComparison.Ordinal))
            {
                return false;
            }
            if (target.Event.Length > 0 && !string.Equals(target.Event, hover.Event, StringComparison.Ordinal))
            {
                return false;
            }
            if (target.Setting.Length > 0 && !string.Equals(target.Setting, hover.Setting, StringComparison.Ordinal))
            {
                return false;
            }
            if (target.Image.Length > 0 && !string.Equals(target.Image, hover.Image, StringComparison.Ordinal))
            {
                return false;
            }
            if (target.Floor && !hover.Floor)
            {
                return false;
            }
            return true;
        }

        public static Sprite? GetImage(PonderSceneDef def, string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }
            var full = Path.Combine(def.Folder, path);
            lock (_imageCache)
            {
                if (_imageCache.TryGetValue(full, out var cached))
                {
                    return cached;
                }
            }
            var tex = ResourceLoader.LoadTexture(full);
            if (tex == null)
            {
                return null;
            }
            var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            lock (_imageCache)
            {
                _imageCache[full] = sprite;
            }
            return sprite;
        }
    }
}
