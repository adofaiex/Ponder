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
                if (value is long l)
                {
                    return (int)l;
                }
                if (value is double d)
                {
                    return (int)d;
                }
                if (value is string s && int.TryParse(s, out var parsed))
                {
                    return parsed;
                }
            }
            return fallback;
        }

        public static float GetFloat(Dictionary<string, object>? dict, string key, float fallback = 0f)
        {
            if (dict != null && dict.TryGetValue(key, out var value))
            {
                if (value is double d)
                {
                    return (float)d;
                }
                if (value is long l)
                {
                    return (float)l;
                }
                if (value is string s && float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                {
                    return parsed;
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
                    binding.Selector.Type = PonderJson.GetString(selectorDict, "type", "event");
                    binding.Selector.Event = PonderJson.GetString(selectorDict, "event");
                    binding.Selector.Setting = PonderJson.GetString(selectorDict, "setting");
                    binding.Selector.Tag = PonderJson.GetString(selectorDict, "tag");
                    binding.Selector.Image = PonderJson.GetString(selectorDict, "image");
                    binding.Selector.Floor = PonderJson.GetBool(selectorDict, "floor", false);
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
                    Title = PonderJson.GetString(raw, "title"),
                    Description = PonderJson.GetString(raw, "description"),
                    Zoom = PonderJson.GetFloat(raw, "zoom", 4f),
                    CenterX = PonderJson.GetFloat(raw, "centerX"),
                    CenterY = PonderJson.GetFloat(raw, "centerY")
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
                                Angle = PonderJson.GetFloat(t, "angle", 180f),
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
                    def.Target.Type = PonderJson.GetString(target, "type", "event");
                    def.Target.Event = PonderJson.GetString(target, "event");
                    def.Target.Setting = PonderJson.GetString(target, "setting");
                    def.Target.Tag = PonderJson.GetString(target, "tag");
                    def.Target.Image = PonderJson.GetString(target, "image");
                    def.Target.Floor = PonderJson.GetBool(target, "floor", false);
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
                                Text = PonderJson.GetString(c, "text"),
                                Logic = ParseLogic(PonderJson.GetList(c.GetValueOrDefault("logic")))
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
                var cmd = new PonderLogicCommand
                {
                    Cmd = PonderJson.GetString(d, "cmd"),
                    Delay = PonderJson.GetFloat(d, "delay"),
                    Duration = PonderJson.GetFloat(d, "duration", 1f),
                    Ease = PonderJson.GetString(d, "ease", "linear"),
                    OffsetX = PonderJson.GetFloat(d, "offsetX"),
                    OffsetY = PonderJson.GetFloat(d, "offsetY"),
                    Tile = PonderJson.GetInt(d, "tile"),
                    EndTile = PonderJson.GetInt(d, "endTile", -1),
                    Scale = PonderJson.GetFloat(d, "scale", 1f),
                    Color = PonderJson.GetString(d, "color"),
                    Style = PonderJson.GetInt(d, "style"),
                    Tag = PonderJson.GetString(d, "tag"),
                    Image = PonderJson.GetString(d, "image"),
                    Rotation = PonderJson.GetFloat(d, "rotation"),
                    Loop = PonderJson.GetBool(d, "loop", false)
                };
                if (!string.IsNullOrEmpty(cmd.Cmd))
                {
                    result.Add(cmd);
                }
            }
            return result;
        }

        /// <summary>根据当前悬停选择器，返回命中的绑定场景。</summary>
        public static List<PonderSceneDef> FindScenes(PonderSelector hover)
        {
            var result = new List<PonderSceneDef>();
            if (hover == null || string.IsNullOrEmpty(hover.Type))
            {
                return result;
            }

            List<PonderBinding> bindings;
            List<PonderSceneDef> scenes;
            lock (_bindings)
            {
                bindings = new List<PonderBinding>(_bindings);
            }
            lock (_scenes)
            {
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

        public static bool Matches(PonderSelector target, PonderSelector hover)
        {
            if (target.Type != hover.Type)
            {
                return false;
            }
            switch (target.Type)
            {
                case "event":
                    return !string.IsNullOrEmpty(target.Event) && target.Event == hover.Event;
                case "setting":
                    return !string.IsNullOrEmpty(target.Setting) && target.Setting == hover.Setting;
                case "decoration":
                    if (!string.IsNullOrEmpty(target.Tag) && target.Tag == hover.Tag)
                    {
                        return true;
                    }
                    return !string.IsNullOrEmpty(target.Image) && target.Image == hover.Image;
                case "floor":
                    return target.Floor && hover.Floor;
                default:
                    return false;
            }
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
