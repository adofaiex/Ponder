using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using GDMiniJSON;
using UnityEngine;

namespace Ponder
{
    /// <summary>两层本地化：Mod 层词表（UI 文案 + ADOFAI 概念词）+ 场景层内联多语言对象。</summary>
    public static class PonderLang
    {
        private static readonly Dictionary<string, string> dict = new Dictionary<string, string>();
        private static string loadedCode = "";
        private static readonly object sync = new object();

        /// <summary>当前语言代码（en_us / zh_cn …），来源 = RDString.language。</summary>
        public static string CurrentCode
        {
            get
            {
                switch (RDString.language)
                {
                    case SystemLanguage.ChineseSimplified:
                    case SystemLanguage.Chinese:
                        return "zh_cn";
                    case SystemLanguage.ChineseTraditional:
                        return "zh_tw";
                    case SystemLanguage.Japanese:
                        return "ja_jp";
                    case SystemLanguage.Korean:
                        return "ko_kr";
                    default:
                        return "en_us";
                }
            }
        }

        private static void EnsureLoaded()
        {
            lock (sync)
            {
                if (loadedCode == CurrentCode && dict.Count > 0)
                {
                    return;
                }
                dict.Clear();
                LoadFile("en_us");
                var code = CurrentCode;
                if (code != "en_us")
                {
                    LoadFile(code);
                }
                loadedCode = code;
            }
        }

        private static void LoadFile(string code)
        {
            var text = ResourceLoader.LoadTextFile(Path.Combine("Ponder", "lang", code + ".json"));
            if (string.IsNullOrEmpty(text))
            {
                return;
            }
            try
            {
                if (Json.Deserialize(text) is Dictionary<string, object> root)
                {
                    Flatten(root, "", dict);
                }
            }
            catch (Exception ex)
            {
                Main.Handler?.Error($"PonderLang: failed to parse lang/{code}.json\n{ex}");
            }
        }

        private static void Flatten(Dictionary<string, object> node, string prefix, Dictionary<string, string> outDict)
        {
            foreach (var kv in node)
            {
                var key = prefix.Length == 0 ? kv.Key : prefix + "." + kv.Key;
                if (kv.Value is Dictionary<string, object> nested)
                {
                    Flatten(nested, key, outDict);
                }
                else if (kv.Value is string s)
                {
                    outDict[key] = s;
                }
            }
        }

        /// <summary>Mod 层 UI 文案查询。</summary>
        public static string Get(string key, string fallback = "")
        {
            EnsureLoaded();
            return dict.TryGetValue(key, out var v) && v.Length > 0 ? v : fallback;
        }

        /// <summary>场景层文本解析：字符串 → 原样；内联对象 → 当前语言 → en_us → 任意值 → fallback。</summary>
        public static string ResolveText(object value, string fallback = "")
        {
            if (value is string s)
            {
                return s;
            }
            if (value is Dictionary<string, object> obj)
            {
                EnsureLoaded();
                if (obj.TryGetValue(CurrentCode, out var v) && v is string vs && vs.Length > 0)
                {
                    return vs;
                }
                if (obj.TryGetValue("en_us", out var ev) && ev is string es && es.Length > 0)
                {
                    return es;
                }
                foreach (var kv in obj)
                {
                    if (kv.Value is string any && any.Length > 0)
                    {
                        return any;
                    }
                }
            }
            return fallback;
        }

        /// <summary>用 Mod 词表替换文案中的 {概念} 占位符，缺词保留原文。</summary>
        public static string Substitute(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }
            EnsureLoaded();
            return PlaceholderRegex.Replace(text, m =>
            {
                var key = "term." + m.Groups[1].Value;
                return dict.TryGetValue(key, out var v) && v.Length > 0 ? v : m.Value;
            });
        }

        /// <summary>解析 + 替换一步到位。</summary>
        public static string Text(object value, string fallback = "")
        {
            return Substitute(ResolveText(value, fallback));
        }

        private static readonly Regex PlaceholderRegex = new Regex(@"\{([A-Za-z0-9_]+)\}", RegexOptions.Compiled);
    }
}
