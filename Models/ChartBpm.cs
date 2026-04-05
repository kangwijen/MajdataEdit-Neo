using System.Linq;

namespace MajdataEdit_Neo.Models;

/// <summary>Resolves the active BPM at a chart time from <see cref="Majson.timingList"/>.</summary>
internal static class ChartBpm
{
    public static float GetBpmAtChartTime(Majson? maj, double chartTime)
    {
        if (maj?.timingList == null || maj.timingList.Count == 0)
            return 0f;

        var ordered = maj.timingList.OrderBy(t => t.time).ToList();
        var bpm = ordered[0].currentBpm;
        foreach (var p in ordered)
        {
            if (p.time <= chartTime + 1e-7)
                bpm = p.currentBpm;
            else
                break;
        }

        return bpm;
    }
}
