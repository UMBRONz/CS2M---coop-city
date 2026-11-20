using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Colossal;
using Colossal.Localization;
using Game.Modding;
using Game.SceneFlow;

namespace CS2M.Util
{
    /// <summary>
    ///     Loads lang/*.json shipped next to the mod into the game's localization, so CS2M no longer
    ///     needs the I18N Everywhere mod. Locales without their own file get English.
    /// </summary>
    internal static class LangLoader
    {
        private const string Fallback = "en-US";

        public static void Load(IMod mod)
        {
            try
            {
                string dir = null;
                if (GameManager.instance.modManager.TryGetExecutableAsset(mod, out var asset))
                {
                    dir = Path.Combine(Path.GetDirectoryName(asset.path) ?? "", "lang");
                }

                if (dir == null || !Directory.Exists(dir))
                {
                    Log.Warn($"LangLoader: no lang folder at {dir}");
                    return;
                }

                LocalizationManager lm = GameManager.instance.localizationManager;
                var loaded = new Dictionary<string, Dictionary<string, string>>();
                foreach (string file in Directory.GetFiles(dir, "*.json"))
                {
                    loaded[Path.GetFileNameWithoutExtension(file)] = FlatJson.Parse(File.ReadAllText(file, Encoding.UTF8));
                }

                loaded.TryGetValue(Fallback, out Dictionary<string, string> english);
                foreach (string locale in lm.GetSupportedLocales())
                {
                    if (loaded.TryGetValue(locale, out Dictionary<string, string> entries) || english != null)
                    {
                        lm.AddSource(locale, new Source(entries ?? english));
                    }
                }

                Log.Info($"LangLoader: loaded {loaded.Count} language files from {dir}");
            }
            catch (Exception ex)
            {
                Log.Error("LangLoader: failed to load translations", ex);
            }
        }

        private sealed class Source : IDictionarySource
        {
            private readonly Dictionary<string, string> _entries;

            public Source(Dictionary<string, string> entries)
            {
                _entries = entries;
            }

            public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors,
                Dictionary<string, int> indexCounts)
            {
                return _entries;
            }

            public void Unload()
            {
            }
        }
    }

    /// <summary>Parser for a flat JSON object of string values: { "key": "value", ... }.</summary>
    internal static class FlatJson
    {
        public static Dictionary<string, string> Parse(string json)
        {
            var result = new Dictionary<string, string>();
            int i = 0;
            Skip(json, ref i);
            Expect(json, ref i, '{');
            while (true)
            {
                Skip(json, ref i);
                if (json[i] == '}') break;
                string key = ReadString(json, ref i);
                Skip(json, ref i);
                Expect(json, ref i, ':');
                Skip(json, ref i);
                result[key] = ReadString(json, ref i);
                Skip(json, ref i);
                if (json[i] == ',')
                {
                    i++;
                    continue;
                }

                Expect(json, ref i, '}');
                break;
            }

            return result;
        }

        private static void Skip(string s, ref int i)
        {
            while (i < s.Length && (char.IsWhiteSpace(s[i]) || s[i] == '﻿')) i++;
        }

        private static void Expect(string s, ref int i, char c)
        {
            if (i >= s.Length || s[i] != c) throw new FormatException($"Expected '{c}' at {i}");
            i++;
        }

        private static string ReadString(string s, ref int i)
        {
            Expect(s, ref i, '"');
            var sb = new StringBuilder();
            while (s[i] != '"')
            {
                char c = s[i++];
                if (c != '\\')
                {
                    sb.Append(c);
                    continue;
                }

                char e = s[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u':
                        sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16));
                        i += 4;
                        break;
                    default: sb.Append(e); break; // \" \\ \/
                }
            }

            i++;
            return sb.ToString();
        }
    }
}
