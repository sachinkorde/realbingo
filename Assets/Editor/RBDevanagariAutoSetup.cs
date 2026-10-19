using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds the Devanagari font asset automatically, with no menu click.
///
/// WHY THIS EXISTS
/// Hindi and Marathi were rendering as □□□□ because the TMP font asset had
/// never been generated and TMP Settings' fallback list was empty. That is a
/// one-time setup step, but it is also the single thing standing between a
/// correct CSV and text a player can actually read — so it should not depend
/// on someone remembering to run a menu item.
///
/// Runs on every domain reload, but does essentially nothing after the first:
/// it checks whether the .asset file exists and whether it is already in the
/// fallback list, and returns immediately when both are true. The real work
/// happens once.
///
/// Deliberately NOT silent — it logs what it did, because a font asset
/// appearing in the project on its own is confusing otherwise.
/// </summary>
[InitializeOnLoad]
public static class RBDevanagariAutoSetup
{
    private const string TtfPath = "Assets/fonts/NotoSansDevanagari/NotoSansDevanagari.ttf";
    private const string AssetPath = "Assets/fonts/NotoSansDevanagari/NotoSansDevanagari SDF.asset";
    private const string DonePrefKey = "RealBingo.DevanagariFontSetup.Failed";

    static RBDevanagariAutoSetup()
    {
        // delayCall: the AssetDatabase is not reliable during a static
        // constructor, and TMP Settings may not have loaded yet.
        EditorApplication.delayCall += TryEnsure;
    }

    private static void TryEnsure()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;

        // Cheap early-out for the normal case: already done.
        TMP_FontAsset fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetPath);
        if (fontAsset != null && IsInFallback(fontAsset)) return;

        // A previous attempt failed hard. Retrying on every reload would spam
        // the console forever, so back off until someone runs the menu item.
        if (fontAsset == null && SessionState.GetBool(DonePrefKey, false)) return;

        if (fontAsset == null)
        {
            Font ttf = AssetDatabase.LoadAssetAtPath<Font>(TtfPath);
            if (ttf == null) return;   // font not imported (yet) — say nothing

            fontAsset = Build(ttf);

            if (fontAsset == null)
            {
                SessionState.SetBool(DonePrefKey, true);
                Debug.LogError(
                    "[Localization] could not build a TMP font asset from " + TtfPath + ".\n" +
                    "Hindi and Marathi will keep showing as boxes until this works.\n" +
                    "Build it by hand: Window ▸ TextMeshPro ▸ Font Asset Creator, source font " +
                    "NotoSansDevanagari, Character Set = Unicode Range (Hex), range 0900-097F,20-7E, " +
                    "then add the result to Project Settings ▸ TextMesh Pro ▸ Settings ▸ Fallback Font Assets.");
                return;
            }
        }

        AddToFallback(fontAsset);
    }

    private static TMP_FontAsset Build(Font ttf)
    {
        // Dynamic atlas: glyphs are rasterized the first time they are used, so
        // the whole Devanagari block costs nothing in the build until a player
        // actually switches language.
        TMP_FontAsset asset = TMP_FontAsset.CreateFontAsset(
            ttf,
            samplingPointSize: 90,
            atlasPadding: 9,
            renderMode: UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA,
            atlasWidth: 1024,
            atlasHeight: 1024,
            atlasPopulationMode: AtlasPopulationMode.Dynamic,
            enableMultiAtlasSupport: true);

        if (asset == null) return null;

        asset.name = Path.GetFileNameWithoutExtension(AssetPath);
        AssetDatabase.CreateAsset(asset, AssetPath);

        // The atlas texture and material are sub-assets. Without this they are
        // lost on the next reload and the font comes back pink.
        if (asset.atlasTextures != null)
        {
            foreach (Texture2D tex in asset.atlasTextures)
                if (tex != null && !AssetDatabase.IsSubAsset(tex))
                    AssetDatabase.AddObjectToAsset(tex, asset);
        }

        if (asset.material != null && !AssetDatabase.IsSubAsset(asset.material))
            AssetDatabase.AddObjectToAsset(asset.material, asset);

        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(AssetPath);

        Debug.Log($"[Localization] built {AssetPath} (dynamic atlas).");
        return AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetPath);
    }

    private static bool IsInFallback(TMP_FontAsset asset)
    {
        var list = TMP_Settings.fallbackFontAssets;
        return list != null && list.Contains(asset);
    }

    private static void AddToFallback(TMP_FontAsset asset)
    {
        TMP_Settings settings = Resources.Load<TMP_Settings>("TMP Settings");
        if (settings == null)
        {
            Debug.LogWarning("[Localization] no TMP Settings asset — add the Devanagari font to " +
                             "Project Settings ▸ TextMesh Pro ▸ Fallback Font Assets by hand.");
            return;
        }

        var list = TMP_Settings.fallbackFontAssets ?? new System.Collections.Generic.List<TMP_FontAsset>();
        if (list.Contains(asset)) return;

        list.Add(asset);
        TMP_Settings.fallbackFontAssets = list;

        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();

        Debug.Log("[Localization] Devanagari font added to TMP Settings ▸ Fallback Font Assets. " +
                  "Hindi and Marathi can now be drawn by every TMP_Text in the project.");
    }

    /// <summary>
    /// Forces the whole thing to run again, for when the asset was deleted or a
    /// build failed earlier in the session.
    /// </summary>
    [MenuItem("Real Bingo/Localization/Rebuild Devanagari Font")]
    private static void Rebuild()
    {
        SessionState.SetBool(DonePrefKey, false);

        TMP_FontAsset existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetPath);
        if (existing != null) AssetDatabase.DeleteAsset(AssetPath);

        TryEnsure();

        TMP_FontAsset now = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetPath);
        EditorUtility.DisplayDialog("Devanagari font",
            now != null
                ? "Font asset built and registered in the TMP fallback list.\n\n" +
                  "Hindi and Marathi will now draw instead of showing boxes.\n\n" +
                  "Note: TextMeshPro still does not SHAPE Devanagari — the pre-base matra ि " +
                  "lands on the wrong side and conjuncts show a visible halant."
                : "Could not build the font asset. See the Console for the manual steps.",
            "OK");
    }
}
