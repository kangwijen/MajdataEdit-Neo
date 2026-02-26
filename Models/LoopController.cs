using System;
using System.Linq;
using MajSimai;

namespace MajdataEdit_Neo.Models;

/// <summary>
/// Interface for the loop controller - enables testability and dependency injection.
/// </summary>
public interface ILoopController
{
    /// <summary>
    /// Gets or sets whether looping is enabled.
    /// </summary>
    bool IsEnabled { get; set; }

    /// <summary>
    /// Gets the current loop region, or null if no loop is set.
    /// </summary>
    LoopRegion? CurrentRegion { get; }

    /// <summary>
    /// Event fired when the loop region changes.
    /// </summary>
    event EventHandler<LoopRegion?>? RegionChanged;

    /// <summary>
    /// Event fired when the enabled state changes.
    /// </summary>
    event EventHandler<bool>? EnabledChanged;

    /// <summary>
    /// Event fired when a loop is triggered (playback reached loop end).
    /// </summary>
    event EventHandler? LoopTriggered;

    /// <summary>
    /// Attempts to set a loop region with the specified times.
    /// </summary>
    /// <param name="startTime">The start time in seconds.</param>
    /// <param name="endTime">The end time in seconds.</param>
    /// <param name="chart">The current chart for beat snapping.</param>
    /// <returns>True if the region was set successfully; false if validation failed.</returns>
    bool TrySetRegion(double startTime, double endTime, SimaiChart? chart);

    /// <summary>
    /// Clears the current loop region.
    /// </summary>
    void ClearRegion();

    /// <summary>
    /// Sets the current chart for beat snapping operations.
    /// </summary>
    /// <param name="chart">The chart to use for timing information.</param>
    void SetChart(SimaiChart? chart);

    /// <summary>
    /// Checks whether a loop should be triggered at the current time.
    /// </summary>
    /// <param name="currentTime">The current playback time in seconds.</param>
    /// <returns>True if a loop should be triggered; false otherwise.</returns>
    bool ShouldLoop(double currentTime);

    /// <summary>
    /// Checks whether a loop should be triggered at the current time with offset adjustment.
    /// </summary>
    /// <param name="currentTime">The current display playback time in seconds (with offset).</param>
    /// <param name="offset">The audio offset in seconds.</param>
    /// <returns>True if a loop should be triggered; false otherwise.</returns>
    bool ShouldLoopWithOffset(double currentTime, double offset);

    /// <summary>
    /// Snaps a time value to the nearest beat.
    /// </summary>
    /// <param name="time">The time to snap in seconds.</param>
    /// <param name="chart">The chart containing beat timing information.</param>
    /// <returns>The snapped time value.</returns>
    double SnapToBeat(double time, SimaiChart? chart);

    /// <summary>
    /// Validates whether a loop region meets minimum duration requirements.
    /// </summary>
    /// <param name="startTime">The start time in seconds.</param>
    /// <param name="endTime">The end time in seconds.</param>
    /// <param name="chart">The chart for BPM information.</param>
    /// <returns>True if the region is valid; false otherwise.</returns>
    bool ValidateRegion(double startTime, double endTime, SimaiChart? chart);
}

/// <summary>
/// Core loop logic controller. Handles loop state, validation, beat snapping, and loop triggers.
/// </summary>
public class LoopController : ILoopController
{
    /// <summary>
    /// Minimum loop duration in beats.
    /// </summary>
    private const double MIN_LOOP_DURATION_BEATS = 1.0;

    /// <summary>
    /// Maximum distance (in beats) for a time to be considered "close enough" to snap to a beat.
    /// This is BPM-dependent - quarter of a beat for more precise snapping.
    /// </summary>
    private const double BEAT_SNAP_THRESHOLD_BEATS = 0.25;

    private LoopRegion? _currentRegion;
    private bool _isEnabled;
    private SimaiChart? _chart;

    /// <summary>
    /// Gets or sets whether looping is enabled.
    /// </summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled != value)
            {
                _isEnabled = value;
                // Clear region when disabling loop
                if (!value && _currentRegion is not null)
                {
                    _currentRegion = null;
                    RegionChanged?.Invoke(this, null);
                }
                EnabledChanged?.Invoke(this, value);
            }
        }
    }

    /// <summary>
    /// Gets the current loop region, or null if no loop is set.
    /// </summary>
    public LoopRegion? CurrentRegion => _currentRegion;

    /// <summary>
    /// Event fired when the loop region changes.
    /// </summary>
    public event EventHandler<LoopRegion?>? RegionChanged;

    /// <summary>
    /// Event fired when the enabled state changes.
    /// </summary>
    public event EventHandler<bool>? EnabledChanged;

    /// <summary>
    /// Event fired when a loop is triggered (playback reached loop end).
    /// </summary>
    public event EventHandler? LoopTriggered;

    /// <summary>
    /// Sets the current chart for beat snapping operations.
    /// </summary>
    /// <param name="chart">The chart to use for timing information.</param>
    public void SetChart(SimaiChart? chart)
    {
        _chart = chart;
        if (_currentRegion is not null && chart is null)
        {
            ClearRegion();
        }
    }

    /// <summary>
    /// Attempts to set a loop region with the specified times.
    /// </summary>
    public bool TrySetRegion(double startTime, double endTime, SimaiChart? chart)
    {
        _chart = chart;

Console.WriteLine($"TrySetRegion: startTime={startTime:F3}, endTime={endTime:F3}");

        if (!ValidateRegion(startTime, endTime, chart))
            return false;

        var snappedStart = SnapToBeat(startTime, chart);
        var snappedEnd = SnapToBeat(endTime, chart);

Console.WriteLine($"After snapping: snappedStart={snappedStart:F3}, snappedEnd={snappedEnd:F3}");

        // Re-validate after snapping (times might have crossed)
        if (!ValidateRegion(snappedStart, snappedEnd, chart))
            return false;

        _currentRegion = new LoopRegion(snappedStart, snappedEnd);
        RegionChanged?.Invoke(this, _currentRegion);

Console.WriteLine($"Loop region set: Start={_currentRegion.StartTime:F3}, End={_currentRegion.EndTime:F3}");
        return true;
    }

    /// <summary>
    /// Clears the current loop region.
    /// </summary>
    public void ClearRegion()
    {
        if (_currentRegion is not null)
        {
            _currentRegion = null;
            RegionChanged?.Invoke(this, null);
        }
    }

    /// <summary>
    /// Checks whether a loop should be triggered at the current time.
    /// </summary>
    public bool ShouldLoop(double currentTime)
    {
        if (!IsEnabled || _currentRegion is null)
            return false;

        if (currentTime >= _currentRegion.EndTime)
        {
            LoopTriggered?.Invoke(this, EventArgs.Empty);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Checks whether a loop should be triggered at the current display time with offset adjustment.
    /// </summary>
    public bool ShouldLoopWithOffset(double currentTime, double offset)
    {
        if (!IsEnabled)
            return false;

        if (_currentRegion is null)
        {
            Console.WriteLine("[LOOP] ShouldLoopWithOffset: No region set!");
            return false;
        }

        // Convert chart time to display time for comparison
        var displayEndTime = _currentRegion.EndTime + offset;
        var displayStartTime = _currentRegion.StartTime + offset;

        // Debug output
        Console.WriteLine($"[LOOP] Check: time={currentTime:F3}, start={displayStartTime:F3}, end={displayEndTime:F3}, offset={offset:F3}, trigger={currentTime >= displayEndTime}");

        if (currentTime >= displayEndTime)
        {
            Console.WriteLine($"[LOOP] *** TRIGGERED! *** Jumping from {currentTime:F3} to {displayStartTime:F3}");
            LoopTriggered?.Invoke(this, EventArgs.Empty);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Snaps a time value to the nearest beat.
    /// </summary>
    public double SnapToBeat(double time, SimaiChart? chart)
    {
        if (chart is null || chart.CommaTimings is null || chart.CommaTimings.Length == 0)
            return time;

        // Generate beat lines similar to how the visualizer does it
        var beatTimes = GenerateBeatTimes(chart);

        if (beatTimes.Count == 0)
            return time;

        // Find the nearest beat
        var nearestBeat = beatTimes
            .OrderBy(t => Math.Abs(t - time))
            .FirstOrDefault();

        // Get BPM at the specific time for threshold calculation
        float bpmAtTime = GetBpmAtTime(time, chart);

        // Calculate BPM-dependent snap threshold (quarter beat duration at current BPM)
        double snapThreshold = BEAT_SNAP_THRESHOLD_BEATS * (60.0 / bpmAtTime);

        // Only snap if within threshold
        if (Math.Abs(nearestBeat - time) < snapThreshold)
        {
    Console.WriteLine($"Snap: {time:F3} -> {nearestBeat:F3} (distance: {Math.Abs(nearestBeat - time):F3}, threshold: {snapThreshold:F3})");
            return nearestBeat;
        }

Console.WriteLine($"No snap: {time:F3} (nearest beat: {nearestBeat:F3}, distance: {Math.Abs(nearestBeat - time):F3}, threshold: {snapThreshold:F3})");
        return time;
    }

    /// <summary>
    /// Gets the BPM at a specific time by finding the last BPM change before or at that time.
    /// </summary>
    private float GetBpmAtTime(double time, SimaiChart chart)
    {
        float currentBpm = chart.CommaTimings.FirstOrDefault()?.Bpm ?? 120.0f;

        foreach (var timing in chart.CommaTimings)
        {
            if (timing.Bpm != currentBpm)
            {
                if (timing.Timing <= time)
                {
                    currentBpm = timing.Bpm;
                }
                else
                {
                    break; // BPM changes are sorted by time
                }
            }
        }

        return currentBpm;
    }

    /// <summary>
    /// Generates all beat times for a chart, matching the visualizer's beat line positions.
    /// This matches the beat generation in SimaiVisualizerControl.cs lines 272-335.
    /// </summary>
    private System.Collections.Generic.List<double> GenerateBeatTimes(SimaiChart chart)
    {
        var beatTimes = new System.Collections.Generic.List<double>();
        var lastBpm = -1f;
        var bpmChangeTimes = new System.Collections.Generic.List<double>();
        var bpmChangeValues = new System.Collections.Generic.List<float>();

        // Collect BPM change points (same as visualizer line 282)
        foreach (var timing in chart.CommaTimings)
        {
            if (timing.Bpm != lastBpm)
            {
                bpmChangeTimes.Add(timing.Timing);
                bpmChangeValues.Add(timing.Bpm);
                lastBpm = timing.Bpm;
            }
        }

        const int signature = 4; // Time signature
        var currentBeat = 1;
        double time;

        // Visualizer starts from first BPM change time (line 289)
        time = bpmChangeTimes.FirstOrDefault();

        // Generate beat times for each BPM section (matches visualizer lines 298-321)
        for (var i = 1; i < bpmChangeTimes.Count; i++)
        {
            double timePerBeat = 60.0 / bpmChangeValues[i - 1];

            while (time < bpmChangeTimes[i] - 0.05)
            {
                beatTimes.Add(time);
                currentBeat++;
                if (currentBeat > signature) currentBeat = 1;
                time += timePerBeat;
            }

            time = bpmChangeTimes[i];
            currentBeat = 1;
        }

        return beatTimes;
    }

    /// <summary>
    /// Validates whether a loop region meets minimum duration requirements.
    /// </summary>
    public bool ValidateRegion(double startTime, double endTime, SimaiChart? chart)
    {
        if (startTime >= endTime)
            return false;

        var duration = endTime - startTime;

        // Get minimum duration from BPM (1 beat at current BPM)
        double minDuration = MIN_LOOP_DURATION_BEATS * (60.0 / 120.0); // Default to 120 BPM
        if (chart?.CommaTimings != null && chart.CommaTimings.Length > 0)
        {
            var bpm = chart.CommaTimings.FirstOrDefault()?.Bpm ?? 120.0;
            minDuration = MIN_LOOP_DURATION_BEATS * (60.0 / bpm);
        }

        return duration >= minDuration;
    }
}
