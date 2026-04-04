using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MajdataEdit_Neo.Models;
using MajSimai;

namespace MajdataEdit_Neo.ViewModels;

/// <summary>
/// ViewModel for loop-related UI state and commands.
/// Coordinates between the View (UI) and the LoopController (logic).
/// </summary>
public partial class LoopViewModel : ViewModelBase
{
    private readonly ILoopController _loopController;
    private float _offset = 0f;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRegion))]
    private LoopRegion? _currentRegion;

    [ObservableProperty]
    private bool _isEnabled;

    /// <summary>
    /// Called when IsEnabled property changes.
    /// </summary>
    partial void OnIsEnabledChanged(bool value)
    {
        // Sync to controller when UI changes the value
        if (_loopController.IsEnabled != value)
        {
            _loopController.IsEnabled = value;
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LoopError))]
    private string? _errorMessage;

    /// <summary>
    /// Gets whether a loop region is currently set.
    /// </summary>
    public bool HasRegion => CurrentRegion is not null;

    /// <summary>
    /// Gets the error message to display, or null if there's no error.
    /// </summary>
    public string? LoopError => ErrorMessage;

    /// <summary>
    /// Gets the loop start time for XAML binding (null-safe, with offset for display).
    /// </summary>
    public double? StartTime => CurrentRegion?.StartTime + _offset;

    /// <summary>
    /// Gets the loop end time for XAML binding (null-safe, with offset for display).
    /// </summary>
    public double? EndTime => CurrentRegion?.EndTime + _offset;

    /// <summary>
    /// Sets the audio offset for display time calculations.
    /// </summary>
    public void SetOffset(float offset)
    {
        if (_offset != offset)
        {
            _offset = offset;
            OnPropertyChanged(nameof(StartTime));
            OnPropertyChanged(nameof(EndTime));
        }
    }

    /// <summary>
    /// Initializes a new instance of the LoopViewModel.
    /// </summary>
    /// <param name="loopController">The loop controller to use.</param>
    public LoopViewModel(ILoopController loopController)
    {
        _loopController = loopController;
        _isEnabled = _loopController.IsEnabled;

        // Subscribe to loop controller events
        _loopController.RegionChanged += (s, r) =>
        {
            CurrentRegion = r;
            ErrorMessage = null;
        };

        _loopController.EnabledChanged += (s, e) =>
        {
            IsEnabled = e;
        };
    }

    /// <summary>
    /// Toggles the loop enabled state.
    /// </summary>
    [RelayCommand]
    private void ToggleEnabled()
    {
        _loopController.IsEnabled = !_loopController.IsEnabled;
    }

    /// <summary>
    /// Clears the current loop region.
    /// </summary>
    [RelayCommand]
    private void ClearRegion()
    {
        _loopController.ClearRegion();
        ErrorMessage = null;
    }

    /// <summary>
    /// Updates the chart for beat snapping operations.
    /// </summary>
    /// <param name="chart">The current chart.</param>
    internal void UpdateChart(SimaiChart? chart, string? fumenText = null)
    {
        _loopController.SetChart(chart, fumenText);
    }

    /// <summary>
    /// Attempts to set a loop region from the specified times.
    /// </summary>
    /// <param name="startTime">The start time in seconds.</param>
    /// <param name="endTime">The end time in seconds.</param>
    /// <param name="chart">The current chart for beat snapping.</param>
    /// <returns>True if the region was set successfully; false otherwise.</returns>
    internal bool TrySetRegion(double startTime, double endTime, SimaiChart? chart)
    {
        ErrorMessage = null;

        var success = _loopController.TrySetRegion(startTime, endTime, chart);

        if (!success)
        {
            ErrorMessage = "Loop region must be at least 1 beat long.";
        }

        return success;
    }

    /// <summary>
    /// Gets the loop controller for direct access by MainWindowViewModel.
    /// </summary>
    internal ILoopController Controller => _loopController;
}
