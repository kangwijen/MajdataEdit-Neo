using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Input;
using MajdataEdit_Neo.Views;
using MajdataEdit_Neo.Models;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MajSimai;
using CommunityToolkit.Mvvm.ComponentModel;
using AvaloniaEdit.Document;
using System.Linq;
using AvaloniaEdit;
using Avalonia.Threading;
using MajdataEdit_Neo.Modules.AutoSave;
using MajdataEdit_Neo.Modules.AutoSave.Contexts;
using System.Runtime.InteropServices;
using MajdataEdit_Neo.Types;
using DiscordRPC;

namespace MajdataEdit_Neo.ViewModels;

/// <summary>Main window VM split across <c>MainWindowViewModel.*.cs</c> partials (editor settings, audio, maidata file I/O, playback).</summary>
public partial class MainWindowViewModel : ViewModelBase
{
    // True only during programmatic caret changes performed via SeekToDocPos.
    // When this is false, manual editor caret changes while playing + FollowCursor are ignored
    // to avoid desyncing highlight from audio playback time.
    volatile bool _isProgrammaticCaretUpdate = false;

    public float Offset
    {
        get
        {
            if (CurrentSimaiFile is null) return _offset;
            _offset = CurrentSimaiFile.Offset;
            return _offset;
        }
        set
        {
            if (CurrentSimaiFile is null) return;
            CurrentSimaiFile.Offset = value;
            SetProperty(ref _offset, value);
            OnPropertyChanged(nameof(CurrentSimaiFile));
            // Update loop view model with new offset for display
            LoopViewModel.SetOffset(value);
            // Regenerate majson.json when offset changes
            RegenerateMajson();
        }
    }
    public string DisplayTime
    {
        get
        {
            var minute = (int)TrackTime / 60;
            double second = (int)(TrackTime - 60 * minute);
            return string.Format("{0}:{1:00}", minute, second);
        }
    }
    public bool IsLoaded
    {
        get
        {
            return CurrentSimaiFile is not null;
        }
    }
    public bool IsPointerPressedSimaiVisual { get; set; }
    public string WindowTitle
    {
        get
        {
            if (CurrentSimaiFile is null) return "MajdataEdit Neo";
            return "MajdataEdit Neo - " + CurrentSimaiFile.Title + (IsSaved ? "" : "*");
        }
    }
    public bool IsFumenContextChanged
    {
        get
        {
            _autoSaveManager.IsFileChanged = !IsSaved;
            return _autoSaveManager.IsFileChanged;
        }
        set
        {
            IsSaved = !value;
            _autoSaveManager.IsFileChanged = value;
        }
    }
    public string Level
    {
        get
        {
            if (CurrentSimaiFile is null || CurrentSimaiFile.Charts[SelectedDifficulty] is null) return "";
            _level[SelectedDifficulty] = CurrentSimaiFile.Charts[SelectedDifficulty].Level;
            return _level[SelectedDifficulty];
        }
        set
        {
            if (CurrentSimaiFile is null || CurrentSimaiFile.Charts[SelectedDifficulty] is null) return;
            CurrentSimaiFile.Charts[SelectedDifficulty].Level = value;
            SetProperty(ref _level[SelectedDifficulty], value);
            OnPropertyChanged(nameof(CurrentSimaiFile));
        }
    }
    public string Designer
    {
        get
        {
            if (CurrentSimaiFile is null || CurrentSimaiFile.Charts[SelectedDifficulty] is null) return "";
            var text = CurrentSimaiFile.Charts[SelectedDifficulty].Designer;
            if (text is null) return "";
            return text;
        }
        set
        {
            if (CurrentSimaiFile is null || CurrentSimaiFile.Charts[SelectedDifficulty] is null) return;
            var text = CurrentSimaiFile.Charts[SelectedDifficulty].Designer;
            if (text is null) return;
            SetProperty(ref text, value);
            CurrentSimaiFile.Charts[SelectedDifficulty].Designer = text;
            OnPropertyChanged(nameof(CurrentSimaiFile));
        }
    }
    public TextDocument FumenDocument
    {
        get
        {
            if (CurrentSimaiFile is null) return new TextDocument();
            var text = CurrentSimaiFile.RawCharts[SelectedDifficulty];
            if (text is null) return new TextDocument();
            ref var fumenContent = ref CurrentSimaiFile.RawCharts[SelectedDifficulty];
            OriginFumen = fumenContent;
            return new TextDocument(fumenContent);
        }
        //setter not working here, so using the event instead
    }
    public string CurrentFumen 
    {
        get
        {
            if (CurrentSimaiFile is null)
                return string.Empty;

            return CurrentSimaiFile.RawCharts[SelectedDifficulty];
        }
    }
    public string OriginFumen { get; set; } = string.Empty;

    public async Task SetFumenContent(string content)
    {
        if (CurrentSimaiFile is null) return;
        var text = CurrentSimaiFile.RawCharts[SelectedDifficulty];
        if (text is null) CurrentSimaiFile.RawCharts[SelectedDifficulty] = "";
        CurrentSimaiFile.RawCharts[SelectedDifficulty] = content;
        OnPropertyChanged(nameof(CurrentSimaiFile));
        try
        {
            CurrentSimaiChart = await _simaiParser.ParseChartAsync(string.Empty, string.Empty, content);
            //IsSaved = true;
        }
        catch (Exception)
        {
            // Silently handle parsing errors
        }
    }

    public bool IsConnected => _viewerConnection.IsViewerRunning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FumenDocument))]
    [NotifyPropertyChangedFor(nameof(Level))]
    [NotifyPropertyChangedFor(nameof(Designer))]
    int selectedDifficulty = 0;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FumenDocument))]
    [NotifyPropertyChangedFor(nameof(Level))]
    [NotifyPropertyChangedFor(nameof(Designer))]
    [NotifyPropertyChangedFor(nameof(Offset))]
    [NotifyPropertyChangedFor(nameof(IsLoaded))]
    SimaiFile? currentSimaiFile = null;
    [ObservableProperty]
    SimaiChart? currentSimaiChart = null;
    [ObservableProperty]
    double caretTime = 0f;
    [ObservableProperty]
    float trackZoomLevel = 4f;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    bool isSaved = true;
    [ObservableProperty]
    TrackInfo? songTrackInfo = null;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayTime))]
    double trackTime = 0f;
    [ObservableProperty]
    private bool isFollowCursor;
    [ObservableProperty]
    private bool isPlayControlEnabled = true;
    [ObservableProperty]
    private bool isAnimated = true;

    [ObservableProperty]
    private bool _isPlaying = false;

    /// <summary>True after Record Mode successfully arms the viewer until stop, song end, or starting normal play.</summary>
    [ObservableProperty]
    private bool isRecordModeActive = false;

    // Audio level properties
    [ObservableProperty]
    private float bgmLevel = 0.7f;
    [ObservableProperty]
    private float answerLevel = 0.7f;
    [ObservableProperty]
    private float judgeLevel = 0.7f;
    [ObservableProperty]
    private float breakLevel = 0.7f;
    [ObservableProperty]
    private float breakSlideLevel = 0.7f;
    [ObservableProperty]
    private float slideLevel = 0.7f;
    [ObservableProperty]
    private float exLevel = 0.7f;
    [ObservableProperty]
    private float touchLevel = 0.7f;
    [ObservableProperty]
    private float hanabiLevel = 0.7f;

    // 0 = Off, 1 = Combo
    [ObservableProperty]
    private int centerDisplayMode = 0;

    // 0 = Classic/Default, 1 = DJAuto
    [ObservableProperty]
    private int playModeIndex = 1;

    [ObservableProperty]
    private float noteSpeed = 7.5f;

    [ObservableProperty]
    private float touchSpeed = 7.5f;

    bool _isBackToStartOnPlayStop = false;
    bool _isUpdatingAutoSaveContext = false;
    
    float _offset = 0;
    DateTime _lastUpdateAutoSaveContextTime = DateTime.UnixEpoch;

    string _maidataDir = "";

    readonly string[] _level = new string[7];
    readonly Lock _syncLock = new();
    readonly DiscordRpcClient _dcRPCClient = new("1068882546932326481");
    private AudioManager? _audioManager;
    readonly RichPresence _dcRichPresence = new()
    {
        Details = "Nothing to do",
        State = "",
        Assets = new Assets
        {
            LargeImageKey = "salt",
            LargeImageText = "Majdata",
            SmallImageKey = "None"
        }
    };
    readonly Lock _fumenContentChangedSyncLock = new();

    CancellationTokenSource? _discordRpcLoopCts;
    Task? _discordRpcLoopTask;

    string GetSelectedDifficultyText()
    {
        return SelectedDifficulty switch
        {
            0 => "EASY",
            1 => "BASIC",
            2 => "ADVANCED",
            3 => "EXPERT",
            4 => "MASTER",
            5 => "Re:MASTER",
            6 => "ORIGINAL",
            _ => $"DIFF{SelectedDifficulty}"
        };
    }

    string GetSelectedChartLevelText()
    {
        if (CurrentSimaiFile is null) return string.Empty;
        var chart = CurrentSimaiFile.Charts.ElementAtOrDefault(SelectedDifficulty);
        var level = chart?.Level;
        return string.IsNullOrWhiteSpace(level) ? string.Empty : level.Trim();
    }

    void UpdateDiscordRpcEditingPresence()
    {
        const string fallback = "Nothing to do";

        if (CurrentSimaiFile is null)
        {
            _dcRichPresence.Details = fallback;
            _dcRichPresence.State = string.Empty;
            if (_dcRichPresence.Assets is not null)
                _dcRichPresence.Assets.LargeImageText = fallback;
        }
        else
        {
            var chartTitle = CurrentSimaiFile.Title;
            if (string.IsNullOrWhiteSpace(chartTitle))
                chartTitle = "Unknown";

            var difficultyText = GetSelectedDifficultyText();
            var chartLevelText = GetSelectedChartLevelText();
            if (string.IsNullOrWhiteSpace(chartLevelText))
                chartLevelText = "?";

            // Discord activity card uses `Details` (top line) and `State` (second line).
            _dcRichPresence.Details = $"Editing: {chartTitle}";
            _dcRichPresence.State = $"{difficultyText} {chartLevelText}";

            if (_dcRichPresence.Assets is not null)
                _dcRichPresence.Assets.LargeImageText = _dcRichPresence.State;
        }

        _dcRPCClient.SetPresence(_dcRichPresence);
    }

    ViewerConnection _viewerConnection = new ViewerConnection();

    // Loop system
    ILoopController _loopController = new LoopController();
    public LoopViewModel LoopViewModel { get; private set; }
    private bool _isProcessingLoop = false;  // Re-entrancy guard for loop trigger
    SimaiParser _simaiParser = new SimaiParser();
    TrackReader _trackReader = new TrackReader();
    InternalAutoSaveContext _internalLocalAutoSaveContext = new();
    InternalAutoSaveContext _internalGlobalAutoSaveContext = new();
    InternalAutoSaveContentProvider _internalAutoSaveContentProvider = new();
    AutoSaveManager _autoSaveManager;
    IAutoSaveRecoverer _autoSaveRecoverer;

    const int AUTOSAVE_CONTEXT_UPDATE_INTERVAL_MS = 5000;
    public MainWindowViewModel()
    {
        PropertyChanged += MainWindowViewModel_PropertyChanged;
        _internalLocalAutoSaveContext = new(_internalAutoSaveContentProvider);
        _internalGlobalAutoSaveContext = new InternalAutoSaveContext(_internalAutoSaveContentProvider);
        AutoSaveManager.Initialize(_internalLocalAutoSaveContext, _internalGlobalAutoSaveContext);
        _autoSaveManager = AutoSaveManager.Instance;
        _autoSaveRecoverer = _autoSaveManager.Recoverer;

        _autoSaveManager.OnAutoSaveExecuted += OnAutoSaveExecuted;

        ReadEditorSetting();

        InitializeDiscordRpc();
        UpdateDiscordRpcEditingPresence();

        // Initialize Loop system
        LoopViewModel = new LoopViewModel(_loopController);
        _loopController.LoopTriggered += async (s, e) => await OnLoopTriggeredAsync();

        // Subscribe to loop region changes to update HasLoopRegion
        LoopViewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(LoopViewModel.CurrentRegion))
            {
                _hasLoopRegion = LoopViewModel.CurrentRegion != null;
                OnPropertyChanged(nameof(HasLoopRegion));
            }
        };

        // Initialize AudioManager
        try
        {
            _audioManager = new AudioManager("SFX");
            _audioManager.LoadSfx();
            ApplyViewModelAudioLevelsToAudioEngine();
        }
        catch (Exception ex)
        {
            // Audio initialization failed, continue without audio
            Console.WriteLine($"Audio initialization failed: {ex.Message}");
        }
    }

    void InitializeDiscordRpc()
    {
        try
        {
            _dcRPCClient.Initialize();
        }
        catch (Exception ex)
        {
            // If Discord RPC IPC fails, don't crash the app.
            Console.WriteLine($"Discord RPC init failed: {ex.Message}");
            return;
        }

        _discordRpcLoopCts = new CancellationTokenSource();
        var token = _discordRpcLoopCts.Token;

        _discordRpcLoopTask = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    _dcRPCClient.Invoke();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Discord RPC invoke failed: {ex.Message}");
                }

                // Recommended: call Invoke faster than 15s due to join-request timeout.
                try
                {
                    await Task.Delay(10000, token);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }, token);
    }

    partial void OnCurrentSimaiFileChanged(SimaiFile? value)
    {
        UpdateDiscordRpcEditingPresence();
    }

    partial void OnSelectedDifficultyChanged(int value)
    {
        UpdateDiscordRpcEditingPresence();
    }

    public void Dispose()
    {
        _audioManager?.Dispose();
        _autoSaveManager.OnAutoSaveExecuted -= OnAutoSaveExecuted;
        try
        {
            _discordRpcLoopCts?.Cancel();
        }
        catch
        {
        }
        _dcRPCClient.Dispose();
    }

    public async Task<bool> ConnectToPlayerAsync()
    {
        // Launch MajdataView if it's not already running
        if (!_viewerConnection.IsViewerRunning)
        {
            await _viewerConnection.LaunchViewerIfNeededAsync();
        }

        // HTTP connection doesn't need explicit connect
        OnPropertyChanged(nameof(IsConnected));
        return true;
    }
    public void SlideZoomLevel(float delta)
    {
        var level = TrackZoomLevel + delta;
        if (level <= 0.1f) level = 0.1f;
        if (level > 10f) level = 10f;
        TrackZoomLevel = level;
    }

    /// <summary>
    /// returns raw postion in chart
    /// </summary>
    /// <param name="delta"></param>
    /// <returns></returns>
    public Point SlideTrackTime(double delta)
    {
        if (SongTrackInfo is null) return new Point();
        // Drag direction should map monotonically: positive delta increases time.
        var computedTime = TrackTime + delta * 0.2 * TrackZoomLevel;
        var time = computedTime;
        if (time < 0) time = 0;
        else if (time > SongTrackInfo.Length) time = SongTrackInfo.Length;

        if(_viewerConnection.IsViewerRunning)
        {
            Stop(false);
        }
        TrackTime = time;
        if (CurrentSimaiChart is null) return new Point();
        // Snap to the nearest note in time (both sides), avoiding a bias that makes one drag direction feel "sticky".
        var nearestNote = CurrentSimaiChart.CommaTimings
            .MinBy(o => Math.Abs(o.Timing + Offset - time));
        if (nearestNote is null) return new Point();

        return new Point(nearestNote.RawTextPositionX, nearestNote.RawTextPositionY);
    }
    public void SetCaretTime(int rawPostion, bool setTrackTime)
    {
        if (CurrentSimaiChart is null) return;

        // During playback with Follow Cursor enabled, ignore manual editor caret changes.
        // This prevents clicks from desyncing the editor highlight from the audio-driven TrackTime.
        if (IsPlaying && IsFollowCursor && !_isProgrammaticCaretUpdate) return;

        var timings = CurrentSimaiChart.CommaTimings.ToArray();

        if (timings.Length == 0) return;

        var foundByRange = false;
        var nearestNote = timings[0];

        // Try to locate the caret position within timing raw-text ranges.
        if (timings.Length >= 2)
        {
            for (int i = 0; i + 1 < timings.Length; i++)
            {
                var note = timings[i];
                var nextnote = timings[i + 1];

                if (rawPostion <= note.RawTextPosition)
                {
                    nearestNote = note;
                    foundByRange = true;
                    break;
                }

                if (note.RawTextPosition < rawPostion && rawPostion <= nextnote.RawTextPosition)
                {
                    nearestNote = nextnote;
                    foundByRange = true;
                    break;
                }
            }
        }

        // If caret raw position doesn't fall into any range, snap to the closest RawTextPosition.
        if (!foundByRange)
            nearestNote = timings.MinBy(o => Math.Abs(o.RawTextPosition - rawPostion));

        if (nearestNote is null) return;

        CaretTime = nearestNote.Timing;

        if (setTrackTime)
        {
            // By pass Ctrl+Click if it's playing
            if (IsPlaying) return;
            Stop(false);
            TrackTime = CaretTime + Offset;
        }
    }

    public void OpenBpmTapWindow()
    {
        new BpmTapWindow().Show();
    }

    public async void OpenPlayerSettingsWindow()
    {
        var mainWindow = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        if (mainWindow?.MainWindow is null) return;

        var window = new PlayerSettingsWindow();
        window.DataContext = this;
        await window.ShowDialog(mainWindow.MainWindow);
    }

    public void MirrorHorizontal(TextEditor editor)
    {
        editor.SelectedText = SimaiMirror.HandleMirror(editor.SelectedText, SimaiMirror.HandleType.LRMirror);
    }
    public void MirrorVertical(TextEditor editor)
    {
        editor.SelectedText = SimaiMirror.HandleMirror(editor.SelectedText, SimaiMirror.HandleType.UDMirror);
    }
    public void Mirror180(TextEditor editor)
    {
        editor.SelectedText = SimaiMirror.HandleMirror(editor.SelectedText, SimaiMirror.HandleType.HalfRotation);
    }
    public void Turn45(TextEditor editor)
    {
        editor.SelectedText = SimaiMirror.HandleMirror(editor.SelectedText, SimaiMirror.HandleType.Rotation45);
    }
    public void TurnNegative45(TextEditor editor)
    {
        editor.SelectedText = SimaiMirror.HandleMirror(editor.SelectedText, SimaiMirror.HandleType.CcwRotation45);
    }

    void UpdateAutoSaveContext()
    {
        _internalLocalAutoSaveContext.RawFilePath = Path.Combine(_maidataDir, "maidata.txt");
        _internalLocalAutoSaveContext.WorkingPath = Path.Combine(_maidataDir, ".autosave");
        _internalGlobalAutoSaveContext.RawFilePath = Path.Combine(_maidataDir, "maidata.txt");
    }
    private async void MainWindowViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        //Console.WriteLine(e.PropertyName);
        if (e.PropertyName == nameof(CurrentSimaiFile) || e.PropertyName == nameof(Level))
        {
            Stop(false);
            lock(_fumenContentChangedSyncLock)
            {
                if(OriginFumen == CurrentFumen)
                    IsFumenContextChanged = false;
                else
                    IsFumenContextChanged = true;
            }
            lock (_syncLock)
            {
                if ((DateTime.Now - _lastUpdateAutoSaveContextTime).TotalMilliseconds < AUTOSAVE_CONTEXT_UPDATE_INTERVAL_MS)
                    return;
                else if (_isUpdatingAutoSaveContext)
                    return;
                _isUpdatingAutoSaveContext = true;
                _lastUpdateAutoSaveContextTime = DateTime.Now;
            }

            try
            {
                if (CurrentSimaiFile is null)
                    return;
                var maidata = await SimaiParser.Shared.DeParseAsStringAsync(CurrentSimaiFile);
                _internalAutoSaveContentProvider.Content = maidata;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
            finally
            {
                _isUpdatingAutoSaveContext = false;
            }

        }
    }
    void OnAutoSaveExecuted(object? sender)
    {
        if (!_fumenContentChangedSyncLock.TryEnter())
            return;
        try
        {
            IsFumenContextChanged = false;
            OriginFumen = CurrentFumen;
        }
        finally
        {
            _fumenContentChangedSyncLock.Exit();
        }
    }
    public void AboutButtonClicked(int index)
    {
        switch (index)
        {
            case 0:
                OpenBrowser("https://discord.gg/AcWgZN7j6K");
                break;
            case 1:
                OpenBrowser("https://qm.qq.com/q/GAxbFZHP6A");
                break;
            case 2:
                OpenBrowser("https://github.com/LingFeng-bbben/MajdataEdit-Neo");
                break;
            case 3:
                OpenBrowser("https://majdata.net/");
                break;
        }
    }

    private void OpenBrowser(string url)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); // Works ok on windows
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            Process.Start("xdg-open", url);  // Works ok on linux
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            Process.Start("open", url); // Not tested
        }
    }
    class InternalAutoSaveContext : IAutoSaveContext, IAutoSaveContentProvider<string>
    {
        public string WorkingPath { get; set; } = Path.Combine(Environment.CurrentDirectory, ".autosave");
        public string RawFilePath { get; set; } = string.Empty;
        public string Content => _contentProvider?.Content ?? string.Empty;

        IAutoSaveContentProvider<string>? _contentProvider;

        public InternalAutoSaveContext(IAutoSaveContentProvider<string>? contentProvider)
        {
            _contentProvider = contentProvider;
        }
        public InternalAutoSaveContext()
        {

        }
    }
    class InternalAutoSaveContentProvider: IAutoSaveContentProvider<string>
    {
        public string Content { get; set; } = string.Empty;
    }
}
