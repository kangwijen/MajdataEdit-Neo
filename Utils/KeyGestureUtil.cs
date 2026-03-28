using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Avalonia.Input;

namespace MajdataEdit_Neo.Utils;

/// <summary>Parses hotkey strings from <c>EditorSetting.json</c> (same rules as toolbar <see cref="Avalonia.Controls.Button.HotKey"/>).</summary>
public static class KeyGestureUtil
{
    public static KeyGesture? TryParse(string? gestureString)
    {
        if (string.IsNullOrWhiteSpace(gestureString)) return null;

        var trimmed = gestureString.Trim();
        foreach (var candidate in BuildGestureCandidates(trimmed))
        {
            try
            {
                return KeyGesture.Parse(candidate);
            }
            catch
            {
                // try next candidate
            }
        }

        return null;
    }

    private static IEnumerable<string> BuildGestureCandidates(string trimmed)
    {
        yield return trimmed;
        if (trimmed.Contains("OemSemicolon", StringComparison.OrdinalIgnoreCase))
            yield return Regex.Replace(trimmed, "OemSemicolon", ";", RegexOptions.IgnoreCase);
        if (trimmed.Contains("OemQuotes", StringComparison.OrdinalIgnoreCase))
            yield return Regex.Replace(trimmed, "OemQuotes", "'", RegexOptions.IgnoreCase);
    }
}
