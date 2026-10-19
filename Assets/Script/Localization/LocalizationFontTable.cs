using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace RealBingo.Localization
{
    /// <summary>
    /// Optional per-language font overrides.
    ///
    /// READ THIS BEFORE FILLING IT IN — you probably do not need it.
    ///
    /// Hindi and Marathi are written in Devanagari, and none of this project's
    /// fonts (Gotham, Lilita One, Times, digital-7) contain a single Devanagari
    /// glyph. Without a font that does, every Hindi/Marathi string renders as a
    /// row of □□□□ no matter how correct the CSV is.
    ///
    /// THE RIGHT FIX IS GLOBAL, NOT PER LABEL:
    ///   1. Import a Devanagari font (Noto Sans Devanagari is the usual choice —
    ///      SIL Open Font License, free to ship in a commercial app).
    ///   2. Window ▸ TextMeshPro ▸ Font Asset Creator — generate an SDF asset.
    ///      Character Set = Unicode Range (Hex), range 0900-097F,20-7E
    ///      (Devanagari block plus ASCII). Dynamic atlas is fine too.
    ///   3. Edit ▸ Project Settings ▸ TextMesh Pro ▸ Settings ▸
    ///      Fallback Font Assets — add it to the list.
    ///
    /// That one step fixes EVERY label in the project at once, including text
    /// written at runtime by code, which no per-label override can reach. TMP
    /// pulls Latin glyphs from the original font and Devanagari from the
    /// fallback automatically.
    ///
    /// This asset exists for the other case: deliberately giving a language a
    /// different typeface for design reasons. Leave it unassigned otherwise.
    ///
    /// Create via: Assets ▸ Create ▸ Real Bingo ▸ Localization Font Table
    /// </summary>
    [CreateAssetMenu(fileName = "LocalizationFontTable",
                     menuName = "Real Bingo/Localization Font Table")]
    public class LocalizationFontTable : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            [Tooltip("Language code exactly as it appears in the CSV header: en, hi, mr…")]
            public string languageCode;
            public TMP_FontAsset font;
        }

        [SerializeField] private List<Entry> fonts = new List<Entry>();

        /// <summary>The override for a language, or null when it has none.</summary>
        public TMP_FontAsset FontFor(string languageCode)
        {
            if (string.IsNullOrEmpty(languageCode)) return null;

            foreach (Entry e in fonts)
                if (e != null && e.languageCode == languageCode) return e.font;

            return null;
        }
    }
}
