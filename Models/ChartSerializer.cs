using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MajSimai;

namespace MajdataEdit_Neo.Models;

internal static class ChartSerializer
{
    public static Majson ConvertToMajson(SimaiFile simaiFile, int difficulty)
    {
        if (simaiFile == null)
        {
            throw new ArgumentException("SimaiFile is null");
        }

        if (simaiFile.Charts == null || simaiFile.Charts.Length <= difficulty || simaiFile.Charts[difficulty] == null)
        {
            throw new ArgumentException($"Invalid difficulty {difficulty} or charts array");
        }

        // Get the offset - use the UI value from SimaiFile.Offset
        // The UI is the authoritative source for offset values
        var offset = simaiFile.Offset;

        var majson = new Majson
        {
            title = simaiFile.Title ?? "Unknown Title",
            artist = simaiFile.Artist ?? "Unknown Artist",
            designer = simaiFile.Charts[difficulty].Designer ?? "",
            difficulty = GetDifficultyText(difficulty),
            diffNum = difficulty,
            level = simaiFile.Charts[difficulty].Level ?? "1",
            first = offset,
            timingList = new List<SimaiTimingPoint>()
        };

        // Use the old SimaiProcess-style parsing to match MajdataEdit-master behavior
        PopulateTimingListFromRawText(majson, simaiFile.RawCharts[difficulty], offset);

        return majson;
    }

    private static void PopulateTimingListFromRawText(Majson majson, string rawChart, float offset)
    {
        // Use the old SimaiProcess.Serialize-like logic to create timing points with raw note content
        // Bake the offset into note times (like the old MajdataEdit-master does)
        var timingPoints = OldStyleSerialize(rawChart, offset);

        // Populate noteList for each timing point using the old getNotes() style parsing
        foreach (var timingPoint in timingPoints)
        {
            timingPoint.noteList = GetNotesFromContent(timingPoint.notesContent, timingPoint.currentBpm, timingPoint.time);
            majson.timingList.Add(timingPoint);
        }
    }

    private static List<SimaiTimingPoint> OldStyleSerialize(string text, float offset)
    {
        var _notelist = new List<SimaiTimingPoint>();
        try
        {
            float bpm = 0;
            var curHSpeed = 1f;
            double time = offset; //in seconds
            var beats = 4;
            var haveNote = false;
            var noteTemp = "";
            int Ycount = 0, Xcount = 0;

            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] == '|' && i + 1 < text.Length && text[i + 1] == '|')
                {
                    // Skip comments
                    Xcount++;
                    while (i < text.Length && text[i] != '\n')
                    {
                        i++;
                        Xcount++;
                    }
                    Ycount++;
                    Xcount = 0;
                    continue;
                }

                if (text[i] == '\n')
                {
                    Ycount++;
                    Xcount = 0;
                }
                else
                {
                    Xcount++;
                }

                if (text[i] == '(')
                {
                    haveNote = false;
                    noteTemp = "";
                    var bpm_s = "";
                    i++;
                    Xcount++;
                    while (text[i] != ')')
                    {
                        bpm_s += text[i];
                        i++;
                        Xcount++;
                    }
                    bpm = float.Parse(bpm_s);
                    continue;
                }

                if (text[i] == '{')
                {
                    haveNote = false;
                    noteTemp = "";
                    var beats_s = "";
                    i++;
                    Xcount++;
                    while (text[i] != '}')
                    {
                        beats_s += text[i];
                        i++;
                        Xcount++;
                    }
                    beats = int.Parse(beats_s);
                    continue;
                }

                if (text[i] == 'H')
                {
                    haveNote = false;
                    noteTemp = "";
                    var hs_s = "";
                    if (text[i + 1] == 'S' && text[i + 2] == '*')
                    {
                        i += 3;
                        Xcount += 3;
                    }
                    while (text[i] != '>')
                    {
                        hs_s += text[i];
                        i++;
                        Xcount++;
                    }
                    curHSpeed = float.Parse(hs_s);
                    continue;
                }

                if (IsNote(text[i])) haveNote = true;
                if (haveNote && text[i] != ',') noteTemp += text[i];
                if (text[i] == ',')
                {
                    if (haveNote)
                    {
                        if (noteTemp.Contains('`'))
                        {
                            var fakeEachList = noteTemp.Split('`');
                            var fakeTime = time;
                            var timeInterval = 1.875 / bpm;
                            foreach (var fakeEachGroup in fakeEachList)
                            {
                                _notelist.Add(new SimaiTimingPoint(fakeTime, Xcount, Ycount, fakeEachGroup, bpm, curHSpeed));
                                fakeTime += timeInterval;
                            }
                        }
                        else
                        {
                            _notelist.Add(new SimaiTimingPoint(time, Xcount, Ycount, noteTemp, bpm, curHSpeed));
                        }
                        noteTemp = "";
                    }
                    time += 1d / (bpm / 60d) * 4d / beats;
                    haveNote = false;
                }
            }
            return _notelist;
        }
        catch (Exception)
        {
            return new List<SimaiTimingPoint>();
        }
    }

    private static List<SimaiNote> GetNotesFromContent(string notesContent, float currentBpm, double timing)
    {
        var simaiNotes = new List<SimaiNote>();
        if (string.IsNullOrEmpty(notesContent)) return simaiNotes;

        try
        {
            var dummy = 0;
            if (notesContent.Length == 2 && int.TryParse(notesContent, out dummy)) // 连写数字
            {
                simaiNotes.Add(GetSingleNote(notesContent[0].ToString(), currentBpm, timing));
                simaiNotes.Add(GetSingleNote(notesContent[1].ToString(), currentBpm, timing));
                return simaiNotes;
            }

            if (notesContent.Contains('/'))
            {
                var notes = notesContent.Split('/');
                foreach (var note in notes)
                    if (note.Contains('*'))
                        simaiNotes.AddRange(GetSameHeadSlideNotes(note, currentBpm, timing));
                    else
                        simaiNotes.Add(GetSingleNote(note, currentBpm, timing));
                return simaiNotes;
            }

            if (notesContent.Contains('*'))
            {
                simaiNotes.AddRange(GetSameHeadSlideNotes(notesContent, currentBpm, timing));
                return simaiNotes;
            }

            simaiNotes.Add(GetSingleNote(notesContent, currentBpm, timing));
        }
        catch
        {
            // If parsing fails, add a default note
            simaiNotes.Add(new SimaiNote { noteContent = notesContent });
        }
        return simaiNotes;
    }

    private static List<string> ParseNotesContent(string notesContent, float currentBpm, double timing)
    {
        // Parse the notesContent to get individual note content strings
        var result = new List<string>();
        if (string.IsNullOrEmpty(notesContent)) return result;

        try
        {
            var dummy = 0;
            if (notesContent.Length == 2 && int.TryParse(notesContent, out dummy))
            {
                result.Add(notesContent[0].ToString());
                result.Add(notesContent[1].ToString());
                return result;
            }

            if (notesContent.Contains('/'))
            {
                var notes = notesContent.Split('/');
                foreach (var note in notes)
                {
                    if (note.Contains('*'))
                        result.AddRange(GetSameHeadSlideNotes(note));
                    else
                        result.Add(note);
                }
                return result;
            }

            if (notesContent.Contains('*'))
            {
                result.AddRange(GetSameHeadSlideNotes(notesContent));
                return result;
            }

            result.Add(notesContent);
        }
        catch
        {
            // If parsing fails, just add the original content
            result.Add(notesContent);
        }
        return result;
    }

    private static SimaiNote GetSingleNote(string noteText, float currentBpm, double timing)
    {
        var simaiNote = new SimaiNote();

        if (IsTouchNote(noteText))
        {
            simaiNote.touchArea = noteText[0];
            if (simaiNote.touchArea != 'C') simaiNote.startPosition = int.Parse(noteText[1].ToString());
            else simaiNote.startPosition = 8;
            simaiNote.noteType = MajsonNoteType.Touch;
        }
        else
        {
            simaiNote.startPosition = int.Parse(noteText[0].ToString());
            simaiNote.noteType = MajsonNoteType.Tap; // Default, will be overridden below if needed
        }

        if (noteText.Contains('f')) simaiNote.isHanabi = true;

        // Hold
        if (noteText.Contains('h'))
        {
            if (IsTouchNote(noteText))
            {
                simaiNote.noteType = MajsonNoteType.TouchHold;
                simaiNote.holdTime = GetTimeFromBeats(noteText, currentBpm);
            }
            else
            {
                simaiNote.noteType = MajsonNoteType.Hold;
                if (noteText.Last() == 'h')
                    simaiNote.holdTime = 0;
                else
                    simaiNote.holdTime = GetTimeFromBeats(noteText, currentBpm);
            }
        }

        // Slide
        if (IsSlideNote(noteText))
        {
            simaiNote.noteType = MajsonNoteType.Slide;
            simaiNote.slideTime = GetTimeFromBeats(noteText, currentBpm);
            var timeStarWait = GetStarWaitTime(noteText, currentBpm);
            simaiNote.slideStartTime = timing + timeStarWait;
            if (noteText.Contains('!') || noteText.Contains('?'))
            {
                simaiNote.isSlideNoHead = true;
                noteText = noteText.Replace("!", "").Replace("?", "");
            }
        }

        // Break
        if (noteText.Contains('b'))
        {
            if (simaiNote.noteType == MajsonNoteType.Slide)
            {
                simaiNote.isSlideBreak = true;
            }
            else
            {
                simaiNote.isBreak = true;
            }
            noteText = noteText.Replace("b", "");
        }

        // EX
        if (noteText.Contains('x'))
        {
            simaiNote.isEx = true;
            noteText = noteText.Replace("x", "");
        }

        // Force star
        if (noteText.Contains('$'))
        {
            simaiNote.isForceStar = true;
            if (noteText.Count(o => o == '$') == 2)
                simaiNote.isFakeRotate = true;
            noteText = noteText.Replace("$", "");
        }

        simaiNote.noteContent = noteText;
        return simaiNote;
    }

    private static List<SimaiNote> GetSameHeadSlideNotes(string content, float currentBpm, double timing)
    {
        var simaiNotes = new List<SimaiNote>();
        var noteContents = content.Split('*');
        var note1 = GetSingleNote(noteContents[0], currentBpm, timing);
        simaiNotes.Add(note1);
        var newNoteContent = noteContents.ToList();
        newNoteContent.RemoveAt(0);
        // Remove first note
        foreach (var item in newNoteContent)
        {
            var note2text = note1.startPosition + item;
            var note2 = GetSingleNote(note2text, currentBpm, timing);
            note2.isSlideNoHead = true;
            simaiNotes.Add(note2);
        }

        return simaiNotes;
    }

    private static List<string> GetSameHeadSlideNotes(string content)
    {
        var result = new List<string>();
        var noteContents = content.Split('*');
        var note1 = noteContents[0];
        result.Add(note1);
        var newNoteContent = noteContents.ToList();
        newNoteContent.RemoveAt(0);
        foreach (var item in newNoteContent)
        {
            result.Add(note1[0] + item);
        }
        return result;
    }

    private static bool IsSlideNote(string noteText)
    {
        var SlideMarks = "-^v<>Vpqszw";
        foreach (var mark in SlideMarks)
            if (noteText.Contains(mark))
                return true;
        return false;
    }

    private static bool IsTouchNote(string noteText)
    {
        var TouchMarks = "ABCDE";
        foreach (var mark in TouchMarks)
            if (noteText.StartsWith(mark.ToString()))
                return true;
        return false;
    }

    private static double GetTimeFromBeats(string noteText, float currentBpm)
    {
        if (noteText.Count(c => c == '[') > 1)
        {
            // Combined slide with multiple durations
            double wholeTime = 0;

            var partStartIndex = 0;
            while (noteText.IndexOf('[', partStartIndex) >= 0)
            {
                var startIndex = noteText.IndexOf('[', partStartIndex);
                var overIndex = noteText.IndexOf(']', partStartIndex);
                partStartIndex = overIndex + 1;
                var innerString = noteText.Substring(startIndex + 1, overIndex - startIndex - 1);
                var timeOneBeat = 1d / (currentBpm / 60d);
                if (innerString.Count(o => o == '#') == 1)
                {
                    var times = innerString.Split('#');
                    if (times[1].Contains(':'))
                    {
                        innerString = times[1];
                        timeOneBeat = 1d / (double.Parse(times[0]) / 60d);
                    }
                    else
                    {
                        wholeTime += double.Parse(times[1]);
                        continue;
                    }
                }

                if (innerString.Count(o => o == '#') == 2)
                {
                    var times = innerString.Split('#');
                    wholeTime += double.Parse(times[2]);
                    continue;
                }

                var numbers = innerString.Split(':');
                var divide = int.Parse(numbers[0]);
                var count = int.Parse(numbers[1]);

                wholeTime += timeOneBeat * 4d / divide * count;
            }

            return wholeTime;
        }

        {
            var startIndex = noteText.IndexOf('[');
            var overIndex = noteText.IndexOf(']');
            var innerString = noteText.Substring(startIndex + 1, overIndex - startIndex - 1);
            var timeOneBeat = 1d / (currentBpm / 60d);
            if (innerString.Count(o => o == '#') == 1)
            {
                var times = innerString.Split('#');
                if (times[1].Contains(':'))
                {
                    innerString = times[1];
                    timeOneBeat = 1d / (double.Parse(times[0]) / 60d);
                }
                else
                {
                    return double.Parse(times[1]);
                }
            }

            if (innerString.Count(o => o == '#') == 2)
            {
                var times = innerString.Split('#');
                return double.Parse(times[2]);
            }

            var numbers = innerString.Split(':');
            var divide = int.Parse(numbers[0]);
            var count = int.Parse(numbers[1]);

            return timeOneBeat * 4d / divide * count;
        }
    }

    private static double GetStarWaitTime(string noteText, float currentBpm)
    {
        var startIndex = noteText.IndexOf('[');
        var overIndex = noteText.IndexOf(']');
        var innerString = noteText.Substring(startIndex + 1, overIndex - startIndex - 1);
        double bpm = currentBpm;
        if (innerString.Count(o => o == '#') == 1)
        {
            var times = innerString.Split('#');
            bpm = double.Parse(times[0]);
        }

        if (innerString.Count(o => o == '#') == 2)
        {
            var times = innerString.Split('#');
            return double.Parse(times[0]);
        }

        return 1d / (bpm / 60d);
    }

    private static bool IsNote(char noteText)
    {
        var SlideMarks = "1234567890ABCDE";
        foreach (var mark in SlideMarks)
            if (noteText == mark)
                return true;
        return false;
    }

    private static MajsonNoteType ConvertNoteType(SimaiNoteType simaiType)
    {
        return simaiType switch
        {
            SimaiNoteType.Tap => MajsonNoteType.Tap,
            SimaiNoteType.Slide => MajsonNoteType.Slide,
            SimaiNoteType.Hold => MajsonNoteType.Hold,
            SimaiNoteType.Touch => MajsonNoteType.Touch,
            SimaiNoteType.TouchHold => MajsonNoteType.TouchHold,
            _ => MajsonNoteType.Tap
        };
    }


    private static string GetDifficultyText(int difficulty)
    {
        return difficulty switch
        {
            0 => "EZ",
            1 => "HD",
            2 => "IN",
            3 => "AT",
            4 => "MASTER",
            _ => "EZ"
        };
    }

    public static string SerializeToJson(Majson majson)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            IncludeFields = true  // Required to serialize fields instead of properties
        };
        var json = JsonSerializer.Serialize(majson, options);
        return json;
    }

    public static void SaveMajdataJson(Majson majson, string directoryPath)
    {
        var jsonPath = System.IO.Path.Combine(directoryPath, "majdata.json");
        var json = SerializeToJson(majson);
        System.IO.File.WriteAllText(jsonPath, json);
    }
}