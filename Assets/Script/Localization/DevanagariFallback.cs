using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace RealBingo.Localization
{
    /// <summary>
    /// Makes Devanagari drawable AT RUNTIME, with no editor step at all.
    ///
    /// WHY IT IS DONE THIS WAY
    /// The first two attempts at this were editor-time: generate a TMP font
    /// asset, then add it to TMP Settings' fallback list. Both failed in
    /// practice — an editor script only runs when the editor decides to run it
    /// (not while in Play mode, not before a compile finishes), and when it
    /// silently did not run, every Hindi and Marathi string rendered as □□□□
    /// with nothing to say why.
    ///
    /// Building the fallback at runtime removes that whole class of problem:
    ///   • it runs in the editor AND in a device build, from the same code
    ///   • it cannot be skipped, forgotten, or undone by someone clearing the
    ///     fallback list in Project Settings
    ///   • there is no generated .asset to keep in source control or rebuild
    ///
    /// The font is created with a DYNAMIC atlas, so glyphs are rasterized the
    /// first time they are actually used. Nothing is baked, and a player who
    /// never leaves English pays only the one-off cost of loading the .ttf.
    ///
    /// COST: ~0.6 MB for the .ttf in the build, and a few milliseconds the
    /// first time a Devanagari character appears on screen.
    ///
    /// This does NOT fix shaping. TextMeshPro reads only liga/kern/mark/mkmk,
    /// so the pre-base matra ि still lands on the wrong side and conjuncts
    /// still show a visible halant. It fixes the boxes, which is the difference
    /// between "unreadable" and "readable but imperfect".
    /// </summary>
    public static class DevanagariFallback
    {
        /// <summary>Resources path (no extension) of the .ttf.</summary>
        private const string FontResourcePath = "Localization/Fonts/NotoSansDevanagariShaped";

        private static bool done;
        private static TMP_FontAsset created;

        /// <summary>
        /// Registers the Devanagari font as a global TMP fallback. Safe to call
        /// repeatedly and from anywhere — the work happens once per run.
        /// </summary>
        public static void Ensure()
        {
            if (done) return;
            done = true;

            // Someone may already have added a Devanagari-capable font in the
            // editor. Re-adding our own would be harmless but wasteful, and it
            // would quietly override their choice of typeface.
            if (FallbackAlreadyCoversDevanagari())
            {
                Debug.Log("[Localization] a TMP fallback already covers Devanagari — leaving it alone.");
                return;
            }

            Font font = Resources.Load<Font>(FontResourcePath);
            if (font == null)
            {
                Debug.LogError($"[Localization] no font at Resources/{FontResourcePath}. " +
                               "Hindi and Marathi will show as boxes.");
                return;
            }

            created = TMP_FontAsset.CreateFontAsset(
                font,
                samplingPointSize: 90,
                atlasPadding: 9,
                renderMode: UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA,
                atlasWidth: 1024,
                atlasHeight: 1024,
                atlasPopulationMode: AtlasPopulationMode.Dynamic,
                enableMultiAtlasSupport: true);

            if (created == null)
            {
                Debug.LogError("[Localization] TMP could not build a font asset from the Devanagari .ttf. " +
                               "Hindi and Marathi will show as boxes.");
                return;
            }

            created.name = "NotoSansDevanagari (runtime)";

            // Surviving a scene load matters: TMP_Settings is an asset, but the
            // font asset we just made is a plain object and would otherwise be
            // collected, leaving the fallback list pointing at nothing.
            Object.DontDestroyOnLoad(created);

            List<TMP_FontAsset> list = TMP_Settings.fallbackFontAssets;
            if (list == null)
            {
                list = new List<TMP_FontAsset>();
                TMP_Settings.fallbackFontAssets = list;
            }

            list.Add(created);

            Debug.Log("[Localization] Devanagari fallback font registered at runtime — " +
                      "every TMP_Text in the project can now draw Hindi and Marathi.");

            RefreshEveryLabel();
        }

        /// <summary>
        /// True when some font already in the fallback chain has a Devanagari
        /// glyph. Tested with DEVANAGARI LETTER KA (U+0915), which every usable
        /// Devanagari font has and no Latin font does.
        /// </summary>
        private static bool FallbackAlreadyCoversDevanagari()
        {
            List<TMP_FontAsset> list = TMP_Settings.fallbackFontAssets;
            if (list == null) return false;

            foreach (TMP_FontAsset f in list)
            {
                if (f == null) continue;
                if (f.HasCharacter('क')) return true;
            }

            return false;
        }

        /// <summary>
        /// Labels that were built BEFORE the fallback existed are already
        /// showing boxes, and TMP will not re-check a character it has already
        /// failed to find. Forcing a rebuild turns those boxes into glyphs
        /// without waiting for the next language change.
        /// </summary>
        private static void RefreshEveryLabel()
        {
            TMP_Text[] all = Object.FindObjectsByType<TMP_Text>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            foreach (TMP_Text t in all)
            {
                if (t == null) continue;
                t.ForceMeshUpdate(ignoreActiveState: true);
            }
        }
    }
}
