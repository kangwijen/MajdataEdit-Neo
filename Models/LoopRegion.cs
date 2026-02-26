using System;

namespace MajdataEdit_Neo.Models;

/// <summary>
/// Represents a loop region with start and end times.
/// </summary>
public record LoopRegion
{
    /// <summary>
    /// The start time of the loop region in seconds.
    /// </summary>
    public double StartTime { get; init; }

    /// <summary>
    /// The end time of the loop region in seconds.
    /// </summary>
    public double EndTime { get; init; }

    /// <summary>
    /// Gets whether this loop region is valid (end time is after start time).
    /// </summary>
    public bool IsValid => EndTime > StartTime;

    /// <summary>
    /// Gets the duration of the loop region in seconds.
    /// </summary>
    public double Duration => EndTime - StartTime;

    /// <summary>
    /// Creates a new LoopRegion with the specified times.
    /// Times are automatically ordered (start will always be the earlier time).
    /// </summary>
    public LoopRegion(double startTime, double endTime)
    {
        StartTime = Math.Min(startTime, endTime);
        EndTime = Math.Max(startTime, endTime);
    }
}
