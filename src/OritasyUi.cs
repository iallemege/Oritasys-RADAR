using System;
using System.IO;
using BepInEx;
using UnityEngine;

namespace RDA
{
    /// <summary>
    /// HUD monospace font preference + soft-load OritasyFonts TTF for CJK branding footer.
    /// </summary>
    internal static class OritasyUi
    {
        internal const string Branding = "Oritasy™";

        private static bool _tried;
        private static Font? _hudFont;
        private static Font? _brandFont;

        internal static Font? Font
        {
            get
            {
                EnsureLoaded();
                return _hudFont ?? _brandFont;
            }
        }

        internal static Font? HudFont
        {
            get
            {
                EnsureLoaded();
                return _hudFont;
            }
        }

        internal static Font? BrandFont
        {
            get
            {
                EnsureLoaded();
                return _brandFont ?? _hudFont;
            }
        }

        internal static void EnsureLoaded()
        {
            if (_tried)
            {
                return;
            }

            _tried = true;
            try
            {
                _hudFont = TryOsMono(14) ?? TryOsMono(13);
                if (_hudFont != null)
                {
                    Log.Info("OritasyUi HUD font: " + _hudFont.name);
                }
                else
                {
                    Log.Debug("OritasyUi: no OS monospace HUD font found; IMGUI default will be used.");
                }

                _brandFont = TryOritasyTtf();
                if (_brandFont != null)
                {
                    Log.Info("OritasyUi branding font loaded (CJK footer)");
                }
            }
            catch (Exception ex)
            {
                Log.Debug("OritasyUi font soft-fail: " + ex.Message);
                _hudFont = null;
                _brandFont = null;
            }
        }

        internal static void ApplyToStyles()
        {
            EnsureLoaded();
            ScopeDraw.ApplyFonts(_hudFont, _brandFont);
        }

        private static Font? TryOsMono(int size)
        {
            string[] families =
            {
                "Consolas",
                "Cascadia Mono",
                "CascadiaMono",
                "Lucida Console",
                "Courier New",
                "Courier",
                "DejaVu Sans Mono",
                "Liberation Mono",
                "Noto Sans Mono"
            };

            try
            {
                Font? f = Font.CreateDynamicFontFromOSFont(families, size);
                return f;
            }
            catch (Exception ex)
            {
                Log.Debug("OS mono font fail: " + ex.Message);
                return null;
            }
        }

        private static Font? TryOritasyTtf()
        {
            try
            {
                string plugins = Paths.PluginPath;
                if (string.IsNullOrEmpty(plugins))
                {
                    plugins = Path.Combine(Paths.BepInExRootPath, "plugins");
                }

                string fontDir = Path.Combine(plugins, "OritasyFonts");
                if (!Directory.Exists(fontDir))
                {
                    Log.Debug("OritasyFonts folder missing at " + fontDir);
                    return null;
                }

                string? preferred = null;
                string? fallback = null;
                foreach (string file in Directory.GetFiles(fontDir, "*.ttf"))
                {
                    string name = Path.GetFileName(file);
                    if (string.Equals(name, "NotoSansSC-VF.ttf", StringComparison.OrdinalIgnoreCase))
                    {
                        preferred = file;
                        break;
                    }

                    fallback ??= file;
                }

                string? path = preferred ?? fallback;
                if (path == null)
                {
                    return null;
                }

                string stem = Path.GetFileNameWithoutExtension(path);
                string[] candidates =
                {
                    stem,
                    stem.Replace('-', ' '),
                    "Noto Sans SC",
                    "NotoSansSC",
                    "Noto Sans SC VF",
                    path,
                    fontDir
                };

                return Font.CreateDynamicFontFromOSFont(candidates, 12);
            }
            catch (Exception ex)
            {
                Log.Debug("Oritasy TTF soft-fail: " + ex.Message);
                return null;
            }
        }
    }
}
