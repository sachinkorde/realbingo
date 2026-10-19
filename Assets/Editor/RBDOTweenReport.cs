using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools &gt; Real Bingo &gt; DOTween &gt; Report Setup
///
/// Read-only. Reports the two DOTween settings that actually cost this project
/// performance and capability, and what to do about each.
/// </summary>
public static class RBDOTweenReport
{
    [MenuItem("Tools/Real Bingo/DOTween/Report Setup")]
    private static void Report()
    {
        var settings = Resources.Load("DOTweenSettings") as ScriptableObject;

        var sb = new StringBuilder();
        sb.AppendLine("[DOTween Report]");
        sb.AppendLine();

        if (settings == null)
        {
            sb.AppendLine("  DOTweenSettings asset not found in Resources. Open");
            sb.AppendLine("  Tools > Demigiant > DOTween Utility Panel and run Setup.");
            Debug.Log(sb.ToString());
            return;
        }

        var so = new SerializedObject(settings);

        bool recyclable = ReadBool(so, "defaultRecyclable");
        bool safeMode = ReadBool(so, "useSafeMode");
        bool tmpEnabled = ReadBool(so, "modules.textMeshProEnabled");
        bool uiEnabled = ReadBool(so, "modules.uiEnabled");

        sb.AppendLine($"  Recycle All Tweens : {(recyclable ? "ON" : "OFF")}");
        sb.AppendLine($"  Safe Mode          : {(safeMode ? "ON" : "OFF")}");
        sb.AppendLine($"  UI module          : {(uiEnabled ? "ON" : "OFF")}");
        sb.AppendLine($"  TextMeshPro module : {(tmpEnabled ? "ON" : "OFF")}");
        sb.AppendLine();

        if (!recyclable)
        {
            sb.AppendLine("  ► Recycling is OFF. Every tween is a fresh allocation that becomes");
            sb.AppendLine("    garbage on completion. With ~170 buttons, a per-second timer punch,");
            sb.AppendLine("    chip select/deselect and wallet counters, that is constant GC churn.");
            sb.AppendLine();
            sb.AppendLine("    DO NOT simply tick the box: 17 tween references are stored in fields");
            sb.AppendLine("    and killed by reference across ChipButton, SpinManager, TimerController,");
            sb.AppendLine("    UIManager, ButtonFeedback, FAQAccordion, UIPanelTweener and AudioManager.");
            sb.AppendLine("    Under recycling a stale reference can kill an unrelated animation.");
            sb.AppendLine("    Convert those to target-based kills first — see DOTweenConfig.cs.");
            sb.AppendLine();
        }

        if (!tmpEnabled)
        {
            sb.AppendLine("  ► TextMeshPro module is OFF, so DOTween Pro's TMP shortcuts are compiled");
            sb.AppendLine("    out and unavailable — DOCounter, DOText, DOFontSize, DOMaxVisibleCharacters,");
            sb.AppendLine("    DOPunchCharScale and the rest. This project's text is 100% TextMeshPro,");
            sb.AppendLine("    so that is the whole Pro text toolset sitting unused.");
            sb.AppendLine();
            sb.AppendLine("    Enable it: Tools > Demigiant > DOTween Utility Panel > Setup DOTween...");
            sb.AppendLine("    It detects TMP and adds the DOTWEEN_TEXTMESHPRO define itself. One click,");
            sb.AppendLine("    and it is the vendor's own tool — safer than editing defines by hand.");
            sb.AppendLine();
        }

        if (recyclable && tmpEnabled)
            sb.AppendLine("  ✔ Nothing to change.");

        Debug.Log(sb.ToString());
    }

    private static bool ReadBool(SerializedObject so, string path)
    {
        SerializedProperty p = so.FindProperty(path);
        return p != null && p.boolValue;
    }
}
