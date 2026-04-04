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
    /// <param name="fumenText">Raw chart text for <c>&lt;n/d&gt;</c> time signature markers.</param>
    void SetChart(SimaiChart? chart, string? fumenText = null);

    /// <summary>
    /// Checks whether a loop should be triggered at the current time.
    /// </summary>
    /// <param name="currentTime">The current playback time in seconds.</param>
    /// <returns>True if a loop should be triggered; false otherwise.</returns>
    bool ShouldLoop(double currentTime);

    /// <summary>
    /// True when display time crosses the loop end: previous < end > current (avoids re-firing every frame past the end).
    /// </summary>
    bool ShouldLoopWithOffset(double previousDisplayTime, double currentDisplayTime, double offset);

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
    /// This is BPM-dependent - one full beat for snapping to beat lines.
    /// </summary>
    private const double BEAT_SNAP_THRESHOLD_BEATS = 1.0;

    private LoopRegion? _currentRegion;
    private bool _isEnabled;
    private SimaiChart? _chart;
    private string? _fumenText;

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
    /// <param name="fumenText">Raw chart text for <c>&lt;n/d&gt;</c> time signature markers.</param>
    public void SetChart(SimaiChart? chart, string? fumenText = null)
    {
        _chart = chart;
        _fumenText = fumenText;
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

        if (!ValidateRegion(startTime, endTime, chart))
            return false;

        var snappedStart = SnapToBeat(startTime, chart);
        var snappedEnd = SnapToBeat(endTime, chart);

        // Re-validate after snapping (times might have crossed)
        if (!ValidateRegion(snappedStart, snappedEnd, chart))
            return false;

        _currentRegion = new LoopRegion(snappedStart, snappedEnd);
        RegionChanged?.Invoke(this, _currentRegion);
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
    /// Fires once per crossing of the loop end (not while time stays past the end).
    /// </summary>
    public bool ShouldLoopWithOffset(double previousDisplayTime, double currentDisplayTime, double offset)
    {
        if (!IsEnabled)
            return false;

        if (_currentRegion is null)
            return false;

        var displayEndTime = _currentRegion.EndTime + offset;

        return previousDisplayTime < displayEndTime && currentDisplayTime >= displayEndTime;
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
            return nearestBeat;

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
    /// Generates all beat times for a chart, matching the waveform guide (see <see cref="TimeSignatureHelper"/>).
    /// </summary>
    private System.Collections.Generic.List<double> GenerateBeatTimes(SimaiChart chart)
    {
        var lastTimingTime = chart.CommaTimings[^1].Timing;
        var sectionEnd = lastTimingTime + 300.0;
        return TimeSignatureHelper.GenerateAllBeatsChartTime(chart, _fumenText, sectionEnd);
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
