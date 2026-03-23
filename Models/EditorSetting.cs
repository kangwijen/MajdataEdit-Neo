using Newtonsoft.Json;

namespace MajdataEdit_Neo.Models;

// Global (not per-maidata) editor configuration.
internal class EditorSetting
{
    public bool AutoCheckUpdate = true;
    public float backgroundCover = 0.6f;
    public int ChartRefreshDelay = 1000;
    public EditorComboIndicator comboStatusType = EditorComboIndicator.None;
    public EditorPlayMethod editorPlayMethod = EditorPlayMethod.DJAuto;

    // Playback speed adjustment keys (not wired in Neo yet, but persisted for compatibility).
    public string DecreasePlaybackSpeedKey = "Ctrl+o";
    public string IncreasePlaybackSpeedKey = "Ctrl+p";

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

    public string Mirror180Key = "Ctrl+l";
    public string Mirror45Key = "Ctrl+OemSemicolon";
    public string MirrorCcw45Key = "Ctrl+OemQuotes";
    public string MirrorLeftRightKey = "Ctrl+j";
    public string MirrorUpDownKey = "Ctrl+k";

    public string PlayPauseKey = "Ctrl+Shift+c";
    public string PlayStopKey = "Ctrl+Shift+x";
    public string SaveKey = "Ctrl+s";
    public string SendViewerKey = "Ctrl+Shift+z";

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

