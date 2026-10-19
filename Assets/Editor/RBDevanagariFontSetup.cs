using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

/// <summary>
/// Menu:  Real Bingo ▸ Localization ▸ Set Up Devanagari Font
///
/// Builds a TMP font asset from Assets/fonts/NotoSansDevanagari/NotoSansDevanagari.ttf
/// and registers it in TMP Settings' fallback list, so every TMP_Text in the
/// project can draw Hindi and Marathi without any per-label wiring.
///
/// Created DYNAMIC on purpose. A static atlas would have to pre-bake the whole
/// Devanagari block plus every conjunct the fonts form, which is thousands of
/// glyphs and a large texture shipped whether or not a player ever switches
/// language. A dynamic asset rasterizes each glyph the first time it is used
/// and costs almost nothing in the build.
///
/// ── READ THIS: the font is necessary but NOT sufficient ──────────────────────
/// TextMeshPro reads exactly four OpenType features — liga, kern, mark, mkmk.
/// Devanagari needs rphf, blwf, half, vatu, cjct, pres, psts and haln, plus
/// syllable segmentation and pre-base matra reordering, which are algorithmic
/// rather than feature-driven. TMP implements none of that.
///
/// With this font installed:
///   • glyphs appear instead of □□□□ boxes                         (fixed)
///   • above/below matras ा ी ु ू े ो ं sit correctly, via mark/mkmk (fixed)
///   • the pre-base matra ि renders AFTER its consonant instead of
///     before it — "कि" comes out looking like "कि" reversed        (BROKEN)
///   • conjuncts क्ष त्र ज्ञ show as two letters with a visible
///     halant instead of one ligature                               (wrong, but readable)
///   • reph (र्) does not lift to a superscript hook                (wrong, but readable)
///
/// Measured against the current sheet: 67 of 166 rows contain a pre-base matra
/// and 95 contain a halant, so roughly 20% of strings render correctly as-is.
/// Run "Report Devanagari Shaping Risk" below for the live count.
///
/// Fixing it properly needs a real shaper. See the note at the end of this file.
/// </summary>
public static class RBDevanagariFontSetup
{
    private const string TtfPath = "Assets/fonts/NotoSansDevanagari/NotoSansDevanagari.ttf";
    private const string AssetPath = "Assets/fonts/NotoSansDevanagari/NotoSansDevanagari SDF.asset";
    private const string CsvPath = "Assets/Resources/Localization/strings.csv";

    [MenuItem("Real Bingo/Localization/Set Up Devanagari Font")]
    public static void SetUpFromMenu() => SetUp(true);

    /// <summary>
    /// showDialog is false when the one-click setup calls this, so the player
    /// does not have to dismiss two dialogs in a row for one action.
    /// </summary>
    public static void SetUp(bool showDialog = true)
    {
        Font ttf = AssetDatabase.LoadAssetAtPath<Font>(TtfPath);
        if (ttf == null)
        {
            Debug.LogError($"[Localization] could not find {TtfPath}.");
            if (showDialog)
                EditorUtility.DisplayDialog("Font missing",
                    $"Could not find {TtfPath}.\n\nThe .ttf should already be in the project — " +
                    "if it isn't, re-add Noto Sans Devanagari there and run this again.", "OK");
            return;
        }

        TMP_FontAsset fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetPath);

        if (fontAsset == null)
        {
            // 90pt sampling, 9px padding, 1024² atlas: the usual balance between
            // crisp SDF edges and a texture that stays reasonable on a phone.
            fontAsset = TMP_FontAsset.CreateFontAsset(
                ttf,
                samplingPointSize: 90,
                atlasPadding: 9,
                renderMode: GlyphRenderMode.SDFAA,
                atlasWidth: 1024,
                atlasHeight: 1024,
                atlasPopulationMode: AtlasPopulationMode.Dynamic,
                enableMultiAtlasSupport: true);

            if (fontAsset == null)
            {
                Debug.LogError("[Localization] TMP could not build a font asset from the ttf.");
                return;
            }

            fontAsset.name = Path.GetFileNameWithoutExtension(AssetPath);

            AssetDatabase.CreateAsset(fontAsset, AssetPath);

            // The atlas texture and material are sub-assets; without this they
            // are lost on reload and the asset comes back pink.
            if (fontAsset.atlasTextures != null)
            {
                foreach (Texture2D tex in fontAsset.atlasTextures)
                    if (tex != null && !AssetDatabase.IsSubAsset(tex))
                        AssetDatabase.AddObjectToAsset(tex, fontAsset);
            }
            if (fontAsset.material != null && !AssetDatabase.IsSubAsset(fontAsset.material))
                AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);

            AssetDatabase.SaveAssets();
            Debug.Log($"[Localization] created {AssetPath} (dynamic atlas).");
        }
        else
        {
            Debug.Log($"[Localization] reusing the existing {AssetPath}.");
        }

        AddToGlobalFallback(fontAsset);

        if (!showDialog) return;

        EditorUtility.DisplayDialog("Devanagari font installed",
            "The font asset is built and added to TMP Settings' fallback list.\n\n" +
            "Hindi and Marathi will now DRAW instead of showing boxes — but TextMeshPro " +
            "does not shape Devanagari, so pre-base matras (ि) land on the wrong side and " +
            "conjuncts show a visible halant.\n\n" +
            "Run 'Report Devanagari Shaping Risk' to see exactly which strings are affected.",
            "OK");
    }

    private static void AddToGlobalFallback(TMP_FontAsset fontAsset)
    {
        TMP_Settings settings = Resources.Load<TMP_Settings>("TMP Settings");
        if (settings == null)
        {
            Debug.LogWarning("[Localization] no TMP Settings asset found — add the font to " +
                             "Project Settings ▸ TextMesh Pro ▸ Fallback Font Assets by hand.");
            return;
        }

        var list = TMP_Settings.fallbackFontAssets ?? new System.Collections.Generic.List<TMP_FontAsset>();

        if (list.Contains(fontAsset))
        {
            Debug.Log("[Localization] font is already in the global fallback list.");
            return;
        }

        list.Add(fontAsset);
        TMP_Settings.fallbackFontAssets = list;

        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();

        Debug.Log("[Localization] added to TMP Settings ▸ Fallback Font Assets. " +
                  "Every TMP_Text in the project can now resolve Devanagari glyphs.");
    }

    /// <summary>
    /// Counts the rows in the sheet that TMP cannot render correctly, so the
    /// decision about a real shaper is made against this project's own text
    /// rather than a general warning.
    /// </summary>
    [MenuItem("Real Bingo/Localization/Report Devanagari Shaping Risk")]
    public static void ReportRisk()
    {
        if (!File.Exists(CsvPath)) { Debug.LogError($"[Localization] no sheet at {CsvPath}"); return; }

        string[] lines = File.ReadAllLines(CsvPath, System.Text.Encoding.UTF8)
                             .Where(l => !string.IsNullOrWhiteSpace(l) &&
                                         !l.StartsWith("#") &&
                                         !l.StartsWith("key,"))
                             .ToArray();

        const char PreBaseMatra = 'ि';   // ि — must move BEFORE its consonant
        const char Halant = '्';         // ्  — conjunct former
        const string Reph = "र्";   // र् — superscript hook

        int preBase = lines.Count(l => l.IndexOf(PreBaseMatra) >= 0);
        int halant = lines.Count(l => l.IndexOf(Halant) >= 0);
        int reph = lines.Count(l => l.Contains(Reph));
        int clean = lines.Count(l => l.IndexOf(PreBaseMatra) < 0 && l.IndexOf(Halant) < 0);

        string report =
            $"Sheet rows:                {lines.Length}\n" +
            $"  pre-base matra (ि):      {preBase}   → renders on the WRONG SIDE\n" +
            $"  halant / conjunct (्):   {halant}   → visible halant instead of a ligature\n" +
            $"  reph (र्):                {reph}   → hook does not lift\n" +
            $"  render correctly as-is:  {clean}\n\n" +
            "TMP supports only liga/kern/mark/mkmk. Devanagari needs rphf, blwf, half,\n" +
            "vatu, cjct, pres, psts, haln plus matra reordering — none of which TMP does.";

        Debug.Log("[Localization] Devanagari shaping risk:\n" + report);
        EditorUtility.DisplayDialog("Devanagari shaping risk", report, "OK");
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// GETTING DEVANAGARI TO RENDER *CORRECTLY*
//
// The font fixes the boxes. It does not fix shaping. Realistic options:
//
//  1. A HarfBuzz-backed text plugin for TMP (Asset Store). Smallest change to
//     this project: the UI stays uGUI/TMP and strings stay logically ordered,
//     which keeps the CSV readable and editable by a translator.
//
//  2. com.unity.ugui 3.0.0-exp.x, where TMP gains the Advanced Text Generator
//     (Unity already ships the ICU data — Editor/Data/Resources/icudt73l.dat —
//     but only UI Toolkit uses it today). It is marked EXPERIMENTAL; putting it
//     under a live real-money build is not a trade worth making yet.
//
//  3. Pre-shape the sheet: store Devanagari in VISUAL order (write "िक" so TMP's
//     naive left-to-right output looks like "कि"). This fixes matras and nothing
//     else, and it makes the CSV unreadable to the translator maintaining it.
//     A last resort, not a plan.
//
//  4. Ship English first and hold Hindi/Marathi until one of the above lands.
//     Everything else built for localization — the sheet, the picker, the
//     binders, L.T() — is already done and stays valid.
// ─────────────────────────────────────────────────────────────────────────────
