using System.Text.RegularExpressions;

namespace MajdataEdit_Neo.Models;

/// <summary>
/// Formats dense simai measure lines by breaking before each new <c>{...}</c> timing group.
/// </summary>
public static class SimaiTimingLineFormatter
{
    /// <summary>
    /// After each comma that is followed by optional space and a <c>{</c>, inserts a line break (LF).
    /// Commas inside note data (e.g. <c>{8}5,3,6</c>) are unchanged because the next token is not <c>{</c>.
    /// </summary>
    public static string SplitTimingsToNewLines(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;
        return Regex.Replace(text, @",\s*(?={)", ",\n");
    }
}
