using Newtonsoft.Json;

namespace MajdataEdit_Neo.Models;

// Global (not per-maidata) editor configuration.
internal class EditorSetting
{
    public bool AutoCheckUpdate = true;
    public float backgroundCover = 0.6f;
    public int ChartRefreshDelay = 1000;

    /// <summary>Fallback seconds for record-mode intro delay when <c>track_start.wav</c> duration cannot be read. Null uses 3s in app.</summary>
    public float? RecordIntroDelaySeconds = null;
    public EditorComboIndicator comboStatusType = EditorComboIndicator.None;
    public EditorPlayMethod editorPlayMethod = EditorPlayMethod.DJAuto;

    // Playback speed adjustment keys (applied in MainWindow via KeyGesture.Parse).
    // Alt+ avoids common Ctrl shortcuts (Open, Print, Redo, etc.).
    public string DecreasePlaybackSpeedKey = "Alt+O";
    public string IncreasePlaybackSpeedKey = "Alt+P";

    // Per-wave default volumes (Neo still uses MajSetting + Sound Settings, but we persist for compatibility).
    public float Default_Answer_Level = 0.7f;
    public float Default_BGM_Level = 0.7f;
    public float Default_Break_Level = 0.7f;
    public float Default_Break_Slide_Level = 0.7f;
    public float Default_Ex_Level = 0.7f;
    public float Default_Hanabi_Level = 0.7f;
    public float Default_Judge_Level = 0.7f;
    public float Default_Slide_Level = 0.7f;
    public float Default_Touch_Level = 0.7f;

    public float DefaultSlideAccuracy = 0.2f;
    public float FontSize = 12;
    public string Language = "en-US";

    public string Mirror180Key = "Alt+L";
    public string Mirror45Key = "Alt+OemSemicolon";
    public string MirrorCcw45Key = "Alt+OemQuotes";
    public string MirrorLeftRightKey = "Alt+J";
    public string MirrorUpDownKey = "Alt+K";

    public string PlayPauseKey = "Alt+Shift+C";
    public string PlayStopKey = "Alt+Shift+X";
    public string RecordModeKey = "Alt+Shift+V";
    public string SaveKey = "Ctrl+s";
    public string SendViewerKey = "Alt+Shift+Z";

    public float playSpeed = 7.5f;
    public float touchSpeed = 7.5f;

    public int RenderMode = 0; // 0=hardware(default), 1=software
    public int SyntaxCheckLevel = 1; // 0=disabled, 1=warning(default), 2=enable
    public bool SmoothSlideAnime = false;

    [JsonConstructor]
    public EditorSetting()
    {
    }
}

