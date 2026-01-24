using System.Collections.Generic;

namespace MajdataEdit_Neo.Models;

internal class Majson
{
    public string artist = "default";
    public string designer = "default";
    public string difficulty = "EZ";
    public int diffNum = 0;
    public string level = "1";
    public List<SimaiTimingPoint> timingList = new();
    public string title = "default";
    public float first = 0; // Add offset field
}

internal class SimaiTimingPoint
{
    public float currentBpm;
    public bool havePlayed;
    public float HSpeed = 1.0f;
    public List<SimaiNote> noteList = new();
    public string notesContent = "";     // Raw simai text content (e.g., "C", "6<3[4:1]")
    public int rawTextPositionX;
    public int rawTextPositionY;
    public double time;

    public SimaiTimingPoint(double _time, int textposX = 0, int textposY = 0, string _content = "", float bpm = 0f,
        float _hspeed = 1f)
    {
        time = _time;
        rawTextPositionX = textposX;
        rawTextPositionY = textposY;
        notesContent = _content.Replace("\n", "").Replace(" ", "");
        currentBpm = bpm;
        HSpeed = _hspeed;
    }
}

internal enum MajsonNoteType
{
    Tap,
    Slide,
    Hold,
    Touch,
    TouchHold
}

internal class SimaiNote
{
    public double holdTime = 0d;
    public bool isBreak = false;
    public bool isEx = false;
    public bool isFakeRotate = false;
    public bool isForceStar = false;
    public bool isHanabi = false;
    public bool isSlideBreak = false;
    public bool isSlideNoHead = false;
    public string noteContent = "";
    public MajsonNoteType noteType;
    public double slideStartTime = 0d;
    public double slideTime = 0d;
    public int startPosition = 1; //键位（1-8）
    public char touchArea = ' ';
}

internal class EditRequestjson
{
    public float audioSpeed;
    public float backgroundCover;
    public EditorComboIndicator comboStatusType;
    public EditorPlayMethod editorPlayMethod;
    public EditorControlMethod control;
    public string? jsonPath;
    public float noteSpeed;
    public long startAt;
    public float startTime;
    public float touchSpeed;
    public bool smoothSlideAnime;
}

public enum EditorPlayMethod
{
    Classic, DJAuto, Random, Disabled
}

public enum EditorComboIndicator
{
    None,

    // List of viable indicators that won't be a static content.
    // ScoreBorder, AchievementMaxDown, ScoreDownDeluxe are static.
    Combo,
    ScoreClassic,
    AchievementClassic,
    AchievementDownClassic,
    AchievementDeluxe = 11,
    AchievementDownDeluxe,
    ScoreDeluxe,

    // Please prefix custom indicator with C
    CScoreDedeluxe = 101,
    CScoreDownDedeluxe,
    MAX
}

internal enum EditorControlMethod
{
    Start,
    Stop,
    OpStart,
    Pause,
    Continue,
    Record
}

//this setting is per maidata
internal class MajSetting
{
    public float Answer_Level = 0.7f;

    public float BGM_Level = 0.7f;
    public float Break_Level = 0.7f;
    public float Break_Slide_Level = 0.7f;
    public float Ex_Level = 0.7f;
    public float Hanabi_Level = 0.7f;
    public float Judge_Level = 0.7f;
    public int lastEditDiff;
    public double lastEditTime;
    public float Slide_Level = 0.7f;
    public float Touch_Level = 0.7f;
}