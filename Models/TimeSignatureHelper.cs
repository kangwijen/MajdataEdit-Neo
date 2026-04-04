using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using MajSimai;

namespace MajdataEdit_Neo.Models;

/// <summary>
/// Parses angle-bracket time signatures (e.g. 3/4) from chart text and builds beat grids for the waveform guide lines.
/// A marker applies from the chart time of the target measure line: the next line that starts with <c>{</c> after a standalone marker,
/// or the same line when <c>{n}</c> and the marker share one line. Timings are resolved from comma positions inside that line (with a Y-coordinate fallback).
/// </summary>
public static class TimeSignatureHelper
{
    private static readonly Regex TimeSignatureRegex = new(@"<(\d+)\s*/\s*(\d+)>", RegexOptions.Compiled);

    private readonly struct SignatureMarker
    {
        public double Time { get; init; }
        public int Numerator { get; init; }
        public int Denominator { get; init; }
        /// <summary>Character index of the opening <c>&lt;</c> in fumen text; used to order markers that share the same time.</summary>
        public int MatchIndex { get; init; }
    }

    /// <summary>
    /// Quarter-note duration at <paramref name="bpm"/>, scaled so denominator 4 = quarter, 8 = eighth, etc.
    /// </summary>
    public static double BeatDurationSeconds(float bpm, int denominator)
    {
        if (bpm <= 0 || denominator <= 0)
            return 0;
        var quarter = 60.0 / bpm;
        return quarter * (4.0 / denominator);
    }

    /// <summary>
    /// Fills strong (downbeat) and weak beat times in chart seconds (no audio offset).
    /// When <paramref name="fumenText"/> is null or has no markers, uses 4/4.
    /// </summary>
    public static void GenerateStrongWeakBeatsChartTime(
        SimaiChart chart,
        string? fumenText,
        double sectionEndTime,
        List<double> strongBeats,
        List<double> weakBeats)
    {
        strongBeats.Clear();
        weakBeats.Clear();
        if (chart.CommaTimings is null || chart.CommaTimings.Length == 0)
            return;

        var sigMarkers = BuildSignatureMarkers(chart, fumenText);

        var lastBpm = -1f;
        var bpmChangeTimes = new List<double>();
        var bpmChangeValues = new List<float>();
        foreach (var timing in chart.CommaTimings)
        {
            if (timing.Bpm != lastBpm)
            {
                bpmChangeTimes.Add(timing.Timing);
                bpmChangeValues.Add(timing.Bpm);
                lastBpm = timing.Bpm;
            }
        }

        if (bpmChangeTimes.Count == 0)
            return;

        var end = Math.Max(sectionEndTime, bpmChangeTimes[^1]);
        bpmChangeTimes.Add(end);

        for (var i = 1; i < bpmChangeTimes.Count; i++)
        {
            var segStart = bpmChangeTimes[i - 1];
            var segEnd = bpmChangeTimes[i];
            var bpm = bpmChangeValues[i - 1];

            var subStarts = new List<double> { segStart };
            foreach (var m in sigMarkers)
            {
                if (m.Time > segStart + 1e-9 && m.Time < segEnd - 1e-9)
                    subStarts.Add(m.Time);
            }

            subStarts.Add(segEnd);
            subStarts.Sort();

            for (var k = 0; k < subStarts.Count - 1; k++)
            {
                var blockStart = subStarts[k];
                var blockEnd = subStarts[k + 1];
                var sig = GetSignatureAt(sigMarkers, blockStart);
                var beatDur = BeatDurationSeconds(bpm, sig.Den);
                var beatsPerMeasure = sig.Num;

                if (beatDur <= 0 || beatsPerMeasure <= 0)
                    continue;

                var time = blockStart;
                var currentBeat = 1;
                while (time < blockEnd - 0.05)
                {
                    if (currentBeat > beatsPerMeasure)
                        currentBeat = 1;

                    if (currentBeat == 1)
                        strongBeats.Add(time);
                    else
                        weakBeats.Add(time);

                    currentBeat++;
                    time += beatDur;
                }
            }
        }
    }

    /// <summary>
    /// All beat times (strong and weak) in chart seconds, sorted, for snap-to-grid.
    /// </summary>
    public static List<double> GenerateAllBeatsChartTime(SimaiChart chart, string? fumenText, double sectionEndTime)
    {
        var strong = new List<double>();
        var weak = new List<double>();
        GenerateStrongWeakBeatsChartTime(chart, fumenText, sectionEndTime, strong, weak);
        var all = new List<double>(strong.Count + weak.Count);
        all.AddRange(strong);
        all.AddRange(weak);
        all.Sort();
        return all;
    }

    private static (int Num, int Den) GetSignatureAt(IReadOnlyList<SignatureMarker> markers, double time)
    {
        var sig = (Num: 4, Den: 4);
        foreach (var m in markers)
        {
            if (m.Time <= time + 1e-9)
                sig = (m.Numerator, m.Denominator);
        }

        return sig;
    }

    /// <summary>
    /// Active time signature at chart time in seconds (no audio offset). For display/waveform time use <c>chartTime = displayTime - offset</c>.
    /// </summary>
    public static (int Numerator, int Denominator) GetSignatureAtChartTime(SimaiChart? chart, string? fumenText, double chartTimeSeconds)
    {
        if (chart?.CommaTimings is null || chart.CommaTimings.Length == 0)
            return (4, 4);
        var t = chartTimeSeconds < 0 ? 0 : chartTimeSeconds;
        var markers = BuildSignatureMarkers(chart, fumenText);
        var sig = GetSignatureAt(markers, t);
        return (sig.Num, sig.Den);
    }

    /// <summary>
    /// Each parsed <c>&lt;n/d&gt;</c> change and its chart time, ordered by time then source position.
    /// </summary>
    public static List<(double ChartTime, int Numerator, int Denominator)> GetSignatureChangePoints(SimaiChart? chart, string? fumenText)
    {
        var list = new List<(double, int, int)>();
        if (chart?.CommaTimings is null || chart.CommaTimings.Length == 0 || string.IsNullOrEmpty(fumenText))
            return list;
        foreach (var m in BuildSignatureMarkers(chart, fumenText))
            list.Add((m.Time, m.Numerator, m.Denominator));
        return list;
    }

    private static List<SignatureMarker> BuildSignatureMarkers(SimaiChart chart, string? fumenText)
    {
        var list = new List<SignatureMarker>();
        if (string.IsNullOrEmpty(fumenText))
            return list;

        foreach (Match match in TimeSignatureRegex.Matches(fumenText))
        {
            if (!int.TryParse(match.Groups[1].Value, out var num) ||
                !int.TryParse(match.Groups[2].Value, out var den))
                continue;
            if (num is <= 0 or > 128 || den is <= 0 or > 128)
                continue;

            if (!TryResolveMarkerChartTime(chart, fumenText, match.Index, match.Length, out var t0))
                continue;

            list.Add(new SignatureMarker
            {
                Time = t0,
                Numerator = num,
                Denominator = den,
                MatchIndex = match.Index
            });
        }

        list.Sort((a, b) =>
        {
            var c = a.Time.CompareTo(b.Time);
            return c != 0 ? c : a.MatchIndex.CompareTo(b.MatchIndex);
        });
        return list;
    }

    /// <summary>
    /// Chart time when the signature takes effect: min comma timing on the measure line
    /// (next line starting with <c>{</c> after a standalone marker, or the line that contains both <c>{</c> and the marker).
    /// </summary>
    private static bool TryResolveMarkerChartTime(SimaiChart chart, string fumenText, int matchIndex, int matchLength, out double chartTime)
    {
        chartTime = 0;
        var afterToken = matchIndex + matchLength;
        var lines = GetLineRanges(fumenText);
        if (lines.Count == 0)
            return false;

        var markerLine = LineIndexForOffset(lines, matchIndex);
        var (mls, mle) = lines[markerLine];

        var prefixLen = Math.Max(0, matchIndex - mls);
        var prefix = fumenText.AsSpan(mls, prefixLen);
        if (prefix.IndexOf('{') >= 0)
        {
            if (TryMinTimingOnLine(chart, mls, mle, out chartTime))
                return true;
        }
        else if (afterToken < mle)
        {
            var after = fumenText.AsSpan(afterToken, mle - afterToken).TrimStart();
            if (after.Length > 0 && after[0] == '{')
            {
                if (TryMinTimingOnLine(chart, mls, mle, out chartTime))
                    return true;
            }
        }

        for (var li = markerLine + 1; li < lines.Count; li++)
        {
            var (ls, le) = lines[li];
            if (le <= ls)
                continue;
            var trimmed = fumenText.AsSpan(ls, le - ls).TrimStart();
            if (trimmed.Length == 0 || trimmed[0] != '{')
                continue;

            if (TryMinTimingOnLine(chart, ls, le, out chartTime))
                return true;

            var yHits = chart.CommaTimings.Where(t => (int)t.RawTextPositionY == li).ToList();
            if (yHits.Count > 0)
            {
                chartTime = yHits.Min(t => t.Timing);
                return true;
            }
        }

        var first = chart.CommaTimings
            .Where(t => t.RawTextPosition >= afterToken)
            .OrderBy(t => t.RawTextPosition)
            .FirstOrDefault();
        if (first is null)
            return false;
        chartTime = first.Timing;
        return true;
    }

    private static bool TryMinTimingOnLine(SimaiChart chart, int lineStart, int lineEndExclusive, out double chartTime)
    {
        chartTime = 0;
        var hits = chart.CommaTimings
            .Where(t => t.RawTextPosition >= lineStart && t.RawTextPosition < lineEndExclusive)
            .ToList();
        if (hits.Count == 0)
            return false;
        chartTime = hits.Min(t => t.Timing);
        return true;
    }

    private static List<(int Start, int End)> GetLineRanges(string text)
    {
        var r = new List<(int Start, int End)>();
        var n = text.Length;
        var i = 0;
        // Must use i < n, not i <= n: at i == n there is nothing left to scan; i <= n repeats (n,n) forever and OOMs.
        while (i < n)
        {
            var lineStart = i;
            while (i < n && text[i] != '\r' && text[i] != '\n')
                i++;
            r.Add((lineStart, i));
            if (i < n && text[i] == '\r')
                i++;
            if (i < n && text[i] == '\n')
                i++;
        }
        return r;
    }

    private static int LineIndexForOffset(IReadOnlyList<(int Start, int End)> lines, int offset)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            var (s, e) = lines[i];
            if (offset >= s && offset < e)
                return i;
        }

        for (var i = 0; i < lines.Count; i++)
        {
            var (s, e) = lines[i];
            if (offset >= s && offset <= e)
                return i;
        }

        return Math.Max(0, lines.Count - 1);
    }
}
