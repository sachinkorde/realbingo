using UnityEngine;

[DefaultExecutionOrder(-1000)]
[RequireComponent(typeof(RectTransform))]
public class SafeAreaFitter : MonoBehaviour
{
    [System.Flags]
    public enum Edges { Top = 1, Bottom = 2, Left = 4, Right = 8, All = 15 }

    [Header("Apply padding for these edges")]
    public Edges edges = Edges.All;

    [Header("Simulate in Editor (optional)")]
    public bool simulateInEditor = false;
    public Rect simulatedSafeArea = new Rect(0, 60, 1080, 2040 - 120); // x,y,width,height

    RectTransform _rt;
    Rect _lastApplied;
    Vector2 _lastRes;
    ScreenOrientation _lastOri;

    void Awake() => _rt = GetComponent<RectTransform>();
    void OnEnable() { Apply(); }

    void Update()
    {
        if ((Vector2)Screen.safeArea.size != _lastRes || Screen.orientation != _lastOri)
            Apply();
    }

    void Apply()
    {
        var sa = GetSafeArea();
        if (sa == _lastApplied) return;

        _lastApplied = sa;
        _lastRes = sa.size;
        _lastOri = Screen.orientation;

        var full = new Rect(0, 0, Screen.width, Screen.height);

        // Convert to normalized anchors
        Vector2 anchorMin = full.position;
        Vector2 anchorMax = full.position + full.size;
        Vector2 saMin = sa.position;
        Vector2 saMax = sa.position + sa.size;

        anchorMin.x = Mathf.InverseLerp(0, full.width, saMin.x);
        anchorMin.y = Mathf.InverseLerp(0, full.height, saMin.y);
        anchorMax.x = Mathf.InverseLerp(0, full.width, saMax.x);
        anchorMax.y = Mathf.InverseLerp(0, full.height, saMax.y);

        // Optional: ignore edges you don’t want to pad
        if (!edges.HasFlag(Edges.Left)) anchorMin.x = 0f;
        if (!edges.HasFlag(Edges.Bottom)) anchorMin.y = 0f;
        if (!edges.HasFlag(Edges.Right)) anchorMax.x = 1f;
        if (!edges.HasFlag(Edges.Top)) anchorMax.y = 1f;

        _rt.anchorMin = anchorMin;
        _rt.anchorMax = anchorMax;
        _rt.offsetMin = Vector2.zero;
        _rt.offsetMax = Vector2.zero;
    }

    Rect GetSafeArea()
    {
#if UNITY_EDITOR
        if (simulateInEditor) return simulatedSafeArea;
#endif
        return Screen.safeArea;
    }
}
