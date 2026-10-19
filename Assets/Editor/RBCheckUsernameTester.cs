using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Window:  Real Bingo ▸ Tests ▸ Check Username API
///
/// Hits /check-username for real and shows BOTH halves of the result:
///   • what the server actually sent back (raw body + HTTP code), and
///   • what APIManager.CheckUsername would hand the UI for that body —
///     (requestOk, isAvailable, message) — plus the message the player
///     would end up seeing.
///
/// WHY THIS EXISTS
/// The "username is free" path is easy to confirm by hand; the "already
/// taken" path needs a name that genuinely exists in the database, which
/// only you can supply. Type one in here and you can see in one click
/// whether the endpoint reports it as taken and whether the client reads
/// that answer correctly.
///
/// This deliberately MIRRORS the parse in APIManager.CheckUsernameCoroutine
/// rather than calling it, so it runs in edit mode with no scene, no
/// APIManager instance and no play mode. If you change the parse there,
/// change ReadLikeApiManager below to match — the point of the tool is lost
/// if the two drift apart.
/// </summary>
public class RBCheckUsernameTester : EditorWindow
{
    private string baseUrl = "https://bingo-backend-bjqq.onrender.com/api";
    private string username = "";
    private Vector2 scroll;
    private bool running;
    private readonly List<string> log = new List<string>();

    // Same free-name / edge cases probed against the live endpoint. "Batch"
    // runs these; the single field is for the one case they cannot cover.
    private static readonly string[] EdgeCases =
    {
        "",                 // required-field rejection (422)
        "a",                // single character
        "test",             // the obvious name to collide with
        "  spaced  ",       // untrimmed
    };

    [MenuItem("Real Bingo/Tests/Check Username API")]
    private static void Open()
    {
        GetWindow<RBCheckUsernameTester>(true, "Check Username API").minSize = new Vector2(620, 420);
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Endpoint", EditorStyles.boldLabel);
        baseUrl = EditorGUILayout.TextField("Base URL", baseUrl);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Single username", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Enter a username you KNOW is already registered. That is the one path " +
            "that cannot be verified without real data: if a taken name still comes " +
            "back \"available\", the endpoint is not actually checking the table and " +
            "the client-side gate will never fire.",
            MessageType.Info);

        using (new EditorGUILayout.HorizontalScope())
        {
            username = EditorGUILayout.TextField("Username", username);
            using (new EditorGUI.DisabledScope(running || string.IsNullOrEmpty(username)))
            {
                if (GUILayout.Button("Check", GUILayout.Width(90)))
                    Run(new[] { username });
            }
        }

        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(running))
        {
            if (GUILayout.Button("Run edge-case batch (empty / 1 char / \"test\" / untrimmed)"))
                Run(EdgeCases);
        }

        if (running) EditorGUILayout.HelpBox("Running… the free host can take ~50s to wake up.", MessageType.Warning);

        EditorGUILayout.Space();
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("Results", EditorStyles.boldLabel);
            if (GUILayout.Button("Clear", GUILayout.Width(60))) log.Clear();
            if (GUILayout.Button("Copy", GUILayout.Width(60)))
                EditorGUIUtility.systemCopyBuffer = string.Join("\n", log);
        }

        scroll = EditorGUILayout.BeginScrollView(scroll);
        foreach (string line in log) EditorGUILayout.SelectableLabel(line, GUILayout.Height(16));
        EditorGUILayout.EndScrollView();
    }

    private void Run(string[] names)
    {
        running = true;
        int remaining = names.Length;

        foreach (string name in names)
        {
            string captured = name;
            Request(captured, () =>
            {
                if (--remaining <= 0) { running = false; Repaint(); }
            });
        }
    }

    private void Request(string name, Action onDone)
    {
        string url = $"{baseUrl}/check-username?username={UnityWebRequest.EscapeURL(name)}";
        UnityWebRequest request = UnityWebRequest.Get(url);
        request.timeout = 70;

        UnityWebRequestAsyncOperation op = request.SendWebRequest();
        op.completed += _ =>
        {
            string body = request.downloadHandler != null ? request.downloadHandler.text : "";
            Append(name, request.responseCode, body);
            request.Dispose();
            onDone?.Invoke();
            Repaint();
        };
    }

    private void Append(string name, long code, string body)
    {
        ReadLikeApiManager(body, out bool requestOk, out bool available, out string message);

        string shown = !requestOk
            ? "(nothing — player is not blocked, /register stays the gate)"
            : available ? "proceeds to OTP" : $"BLOCKED: \"{message}\"";

        var sb = new StringBuilder();
        sb.AppendLine($"── username: \"{name}\"   HTTP {code}");
        sb.AppendLine($"   raw      : {Trim(body, 200)}");
        sb.AppendLine($"   client   : requestOk={requestOk}  available={available}");
        sb.AppendLine($"   message  : {message}");
        sb.AppendLine($"   UI does  : {shown}");

        log.Add(sb.ToString());
        Debug.Log($"[CheckUsername test] {sb}");
    }

    /// <summary>Mirror of the decision in APIManager.CheckUsernameCoroutine.</summary>
    private static void ReadLikeApiManager(string body, out bool requestOk, out bool available, out string message)
    {
        CheckUsernameResponse resp = null;
        try { resp = JsonUtility.FromJson<CheckUsernameResponse>(body); }
        catch { /* HTML error page, empty body, WAF block — all land here */ }

        bool serverAnswered = resp != null && !string.IsNullOrEmpty(body) && body.Contains("\"success\"");

        if (!serverAnswered)
        {
            requestOk = false;
            available = false;
            message = "could not read an app response from this body";
            return;
        }

        requestOk = true;
        available = resp.success;
        message = string.IsNullOrEmpty(resp.message)
            ? (resp.success ? "Username available!" : "That username is already taken.")
            : resp.message;
    }

    private static string Trim(string s, int max)
    {
        if (string.IsNullOrEmpty(s)) return "(empty)";
        s = s.Replace("\n", " ").Replace("\r", "");
        return s.Length <= max ? s : s.Substring(0, max) + " …";
    }
}
