#if UNITY_ANDROID
using System.IO;
using System.Xml;
using UnityEditor.Android;
using UnityEngine;

/// <summary>
/// Adds &lt;uses-permission android:name="android.permission.VIBRATE" /&gt; to the
/// generated Android manifest at build time.
///
/// Unity does not add this permission on its own, and without it the Vibrator
/// service silently does nothing on a real device � the game would look fine in
/// the Editor and have no vibration in the store build.
///
/// This runs on the GENERATED gradle project, so the project's own settings and
/// any existing manifest stay untouched: nothing to maintain, nothing to merge.
/// </summary>
public class RBAndroidVibratePermission : IPostGenerateGradleAndroidProject
{
    private const string Permission = "android.permission.VIBRATE";

    public int callbackOrder => 1;

    public void OnPostGenerateGradleAndroidProject(string path)
    {
        string manifestPath = Path.Combine(path, "src", "main", "AndroidManifest.xml");
        if (!File.Exists(manifestPath))
        {
            Debug.LogWarning($"[RBAndroidVibratePermission] Manifest not found at {manifestPath}. VIBRATE permission NOT added.");
            return;
        }

        var doc = new XmlDocument();
        doc.Load(manifestPath);

        XmlElement manifest = doc.DocumentElement;
        if (manifest == null) return;

        const string androidNs = "http://schemas.android.com/apk/res/android";

        // Already declared (by Unity, a package, or a previous build)? Leave it be.
        foreach (XmlNode node in manifest.SelectNodes("uses-permission"))
        {
            var element = node as XmlElement;
            if (element != null && element.GetAttribute("name", androidNs) == Permission) return;
        }

        XmlElement permission = doc.CreateElement("uses-permission");
        permission.SetAttribute("name", androidNs, Permission);
        manifest.AppendChild(permission);

        doc.Save(manifestPath);
        Debug.Log("[RBAndroidVibratePermission] Added android.permission.VIBRATE to the build manifest.");
    }
}
#endif
