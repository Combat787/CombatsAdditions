using System.Collections.Generic;
using BepInEx;
using CombatsAdditions;
using UnityEngine;

namespace Loader98
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin {
        private void Awake() {
            this.Info.Metadata.GUID;
        }

    }
}
public class LoadingScreen : MonoBehaviour
{
    public class Progress
    {
        public string Name;
        public int Current;
        public int Total = 1;
        public bool Finished;
        public bool HasError;
    }

    private readonly Dictionary<string, Progress> _pluginProgresses =
        new Dictionary<string, Progress>();

    public static LoadingScreen Instance { get; private set; }

    private GUIStyle _headerStyle;
    private GUIStyle _bodyStyle;

    private const float Margin = 50f;
    private const float RowHeight = 30f;
    private const float Gap = 5f;

    private const int BarChars = 32;

    public static void Create()
    {
        if (Instance != null) return;

        var go = new GameObject("BlueprinterLoadingScreen");
        Instance = go.AddComponent<LoadingScreen>();
        DontDestroyOnLoad(go);
    }

    public static void DestroyInstance()
    {
        if (Instance == null) return;

        Destroy(Instance.gameObject);
        Instance = null;
    }

    public void SetProgress(string progressName, int current, int total, string status)
    {
        if (string.IsNullOrEmpty(progressName))
            progressName = "Bundle";

        if (!_pluginProgresses.TryGetValue(progressName, out var bp))
            _pluginProgresses[progressName] = bp = new Progress { Name = progressName };

        bp.Total = Mathf.Max(1, total);
        bp.Current = Mathf.Clamp(current, 0, bp.Total);

        if (status == "fail")
            bp.HasError = true;
        else if (status == "done")
            bp.Finished = true;
    }

    private void EnsureStyles()
    {
        if (_headerStyle != null) return;

        _headerStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.UpperLeft,
            fontSize = 30,
            fontStyle = FontStyle.Bold,
        };
        _headerStyle.normal.textColor = Color.green;

        _bodyStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleLeft,
            fontSize = 20,
            fontStyle = FontStyle.Normal,
            wordWrap = false,
            clipping = TextClipping.Clip
        };
        _bodyStyle.normal.textColor = Color.green;
    }

    private static string BuildBar(float pct)
    {
        int filled = Mathf.Clamp(Mathf.RoundToInt(pct * BarChars), 0, BarChars);
        return new string('▆', filled).PadRight(BarChars, '▁');
    }

    private static string BuildStatus(Progress bp)
    {
        if (bp.Finished)
            return bp.HasError
                ? "WARNING (check logs)"
                : "DONE";

        return bp.HasError
            ? $"WARNING {bp.Current}/{bp.Total}"
            : $"{bp.Current}/{bp.Total}";
    }

    private void OnGUI()
    {
        if (_pluginProgresses.Count == 0) return;

        EnsureStyles();

        // background
        GUI.color = new Color(0f, 0.03f, 0f, 0.9f);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);

        float x = Margin;
        float y = Margin;
        float totalWidth = Screen.width - Margin * 2f;

        // measure widest name
        float nameWidth = 0f;
        foreach (var bp in _pluginProgresses.Values)
        {
            var size = _bodyStyle.CalcSize(new GUIContent(bp.Name));
            if (size.x > nameWidth) nameWidth = size.x;
        }

        nameWidth = Mathf.Min(nameWidth + 16f, totalWidth * 0.5f);

        // compute bar width based on character width and BarChars
        float charWidth = _bodyStyle.CalcSize(new GUIContent("▆")).x;
        float barWidth = charWidth * (BarChars + 2);
        float statusWidth = Mathf.Max(0f, totalWidth - nameWidth - barWidth - 2f * Gap);


        foreach ()
        // header
        GUI.color = _headerStyle.normal.textColor;
        GUI.Label(new Rect(x, y, totalWidth, 40),
            $"{MyPluginInfo.PLUGIN_NAME}  {MyPluginInfo.PLUGIN_VERSION}", _headerStyle);
        y += 60f;

        var normalColor = _bodyStyle.normal.textColor;
        var warningColor = new Color(1f, 0.5f, 0f, 1f);

        // rows
        foreach (var bp in _pluginProgresses.Values)
        {
            // name
            GUI.color = normalColor;
            var nameRect = new Rect(x, y, nameWidth, RowHeight);
            GUI.Label(nameRect, bp.Name, _bodyStyle);

            // bar + status
            var color = bp.HasError ? warningColor : normalColor;
            GUI.color = color;

            var barRect = new Rect(x + nameWidth + Gap, y, barWidth, RowHeight);
            GUI.Label(barRect, $"[{BuildBar((float)bp.Current / bp.Total)}]", _bodyStyle);

            if (statusWidth > 0f)
            {
                var statusRect = new Rect(barRect.xMax + Gap, y, statusWidth, RowHeight);
                GUI.Label(statusRect, BuildStatus(bp), _bodyStyle);
            }

            y += RowHeight;
        }
    }
}