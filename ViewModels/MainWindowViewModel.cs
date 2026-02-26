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
using System.Collections.ObjectModel;
using MsBox.Avalonia;
using AvaloniaEdit;
using MsBox.Avalonia.Enums;
using MajdataEdit_Neo.Utils;
using Avalonia.Threading;
using MajdataEdit_Neo.Modules.AutoSave;
using MajdataEdit_Neo.Modules.AutoSave.Contexts;
using System.Runtime.InteropServices;
using MajdataEdit_Neo.Types;
using DiscordRPC;
using Newtonsoft.Json;

namespace MajdataEdit_Neo.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
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

    [ObservableProperty]
    private double sfxLatencyCompensation = 0.0545;

    partial void OnBgmLevelChanged(float value)
    {
        if (_audioManager != null)
        {
            _audioManager.BgmLevel = value;
            _audioManager.UpdateAllVolumes();
        }
    }

    partial void OnAnswerLevelChanged(float value)
    {
        if (_audioManager != null)
        {
            _audioManager.AnswerLevel = value;
            _audioManager.UpdateAllVolumes();
        }
    }

    partial void OnJudgeLevelChanged(float value)
    {
        if (_audioManager != null)
        {
            _audioManager.JudgeLevel = value;
            _audioManager.UpdateAllVolumes();
        }
    }

    partial void OnBreakLevelChanged(float value)
    {
        if (_audioManager != null)
        {
            _audioManager.BreakLevel = value;
            _audioManager.UpdateAllVolumes();
        }
    }

    partial void OnBreakSlideLevelChanged(float value)
    {
        if (_audioManager != null)
        {
            _audioManager.BreakSlideLevel = value;
            _audioManager.UpdateAllVolumes();
        }
    }

    partial void OnSlideLevelChanged(float value)
    {
        if (_audioManager != null)
        {
            _audioManager.SlideLevel = value;
            _audioManager.UpdateAllVolumes();
        }
    }

    partial void OnExLevelChanged(float value)
    {
        if (_audioManager != null)
        {
            _audioManager.ExLevel = value;
            _audioManager.UpdateAllVolumes();
        }
    }

    partial void OnTouchLevelChanged(float value)
    {
        if (_audioManager != null)
        {
            _audioManager.TouchLevel = value;
            _audioManager.UpdateAllVolumes();
        }
    }

    partial void OnHanabiLevelChanged(float value)
    {
        if (_audioManager != null)
        {
            _audioManager.HanabiLevel = value;
            _audioManager.UpdateAllVolumes();
        }
    }

    partial void OnSfxLatencyCompensationChanged(double value)
    {
        if (_audioManager != null)
        {
            _audioManager.SfxLatencyCompensation = value;
        }
    }

    bool _isBackToStartOnPlayStop = false;
    bool _isUpdatingAutoSaveContext = false;
    
    float _offset = 0;
    double playStartTime = 0d;
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

    TextEditor? _textEditor;

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
    private const string majSettingFilename = "majSetting.json";
    public MainWindowViewModel()
    {
        PropertyChanged += MainWindowViewModel_PropertyChanged;
        _internalLocalAutoSaveContext = new(_internalAutoSaveContentProvider);
        _internalGlobalAutoSaveContext = new InternalAutoSaveContext(_internalAutoSaveContentProvider);
        AutoSaveManager.Initialize(_internalLocalAutoSaveContext, _internalGlobalAutoSaveContext);
        _autoSaveManager = AutoSaveManager.Instance;
        _autoSaveRecoverer = _autoSaveManager.Recoverer;

        _autoSaveManager.OnAutoSaveExecuted += OnAutoSaveExecuted;
        _dcRPCClient.SetPresence(_dcRichPresence);

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
        }
        catch (Exception ex)
        {
            // Audio initialization failed, continue without audio
            Console.WriteLine($"Audio initialization failed: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _audioManager?.Dispose();
        _autoSaveManager.OnAutoSaveExecuted -= OnAutoSaveExecuted;
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
        var time = TrackTime - delta * 0.2 * TrackZoomLevel;
        if (time < 0) time = 0;
        else if (time > SongTrackInfo.Length) time = SongTrackInfo.Length;
        if(_viewerConnection.IsViewerRunning)
        {
            Stop(false);
        }
        TrackTime = time;
        if (CurrentSimaiChart is null) return new Point();
        var nearestNote = CurrentSimaiChart.CommaTimings.Where(o=> o.Timing + Offset - time < 0).MinBy(o => Math.Abs(o.Timing + Offset - time));
        if (nearestNote is null) return new Point();
        return new Point(nearestNote.RawTextPositionX, nearestNote.RawTextPositionY);
    }
    public void SetCaretTime(int rawPostion, bool setTrackTime)
    {
        if (CurrentSimaiChart is null) return;
        var timings = CurrentSimaiChart.CommaTimings.ToArray();
        var nearestNote = timings.FirstOrDefault();
        //theLine = theLine.OrderBy(o => o.RawTextPositionX).ToArray();
        if (timings.Length >= 2)
        {
            for (int i = 0; i + 1 < timings.Length; i++)
            {
                var note = timings[i];
                var nextnote = timings[i + 1];
                if(rawPostion <= note.RawTextPosition)
                {
                    nearestNote = note;
                    break;
                }
                if (note.RawTextPosition < rawPostion && rawPostion <= nextnote.RawTextPosition)
                {
                    nearestNote = nextnote;
                    break;
                }
            }
        }
        if (nearestNote is null) return;
        CaretTime = nearestNote.Timing;
        if (IsFollowCursor|| setTrackTime) {
            //By pass Ctrl+Click if it's playing
            if (IsPlaying) return;
            Stop(false);
            TrackTime = CaretTime + Offset;
        }
    }

    public async Task NewFile()
    {
        if (await AskSave()) return;
        try
        {
            var file = await FileIOManager.DoOpenFilePickerAsync(FileIOManager.FileOpenerType.Track);
            if (file is null) return;
            var maidataPath = file.TryGetLocalPath();
            if (maidataPath is null) return;
            var fileInfo = new FileInfo(maidataPath);
            if (fileInfo.Directory == null) return;
            _maidataDir = fileInfo.Directory.FullName;
            if(File.Exists( _maidataDir+"/maidata.txt"))
            {
                var mainWindow = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
                if (mainWindow?.MainWindow != null)
                {
                    await MessageBoxManager.GetMessageBoxStandard(
                    "Error", "Maidata Already Exist",
                    MsBox.Avalonia.Enums.ButtonEnum.Ok, MsBox.Avalonia.Enums.Icon.Error)
                    .ShowWindowDialogAsync(mainWindow.MainWindow);
                }
                return;
            }
            CurrentSimaiFile = SimaiFile.Empty("Set Title", "Set Artist");
            SongTrackInfo = _trackReader.ReadTrack(_maidataDir);
            IsSaved = false;
            OpenChartInfoWindow();
        }
        catch (Exception)
        {
            // Silently handle file opening errors
        }
    }
    public async Task OpenFile()
    {
        if (await AskSave()) return;
        try
        {
            var file = await FileIOManager.DoOpenFilePickerAsync(FileIOManager.FileOpenerType.Maidata);
            if (file is null) return;
            var maidataPath = file.TryGetLocalPath();
            if (maidataPath is null) return;
            CurrentSimaiFile = await _simaiParser.ParseAsync(maidataPath);
            var fileInfo = new FileInfo(maidataPath);
            if (fileInfo.Directory == null) return;
            _maidataDir = fileInfo.Directory.FullName;
            SongTrackInfo = _trackReader.ReadTrack(_maidataDir);
            //IsFumenContextChanged = false;
            _autoSaveManager.Enabled = true;
            _internalAutoSaveContentProvider.Content = await File.ReadAllTextAsync(maidataPath);
            UpdateAutoSaveContext();
            //TODO: Reset view if already loaded?
            await EditorLoad();
            ReadSetting();
            // Update loop view model with current offset after loading
            LoopViewModel.SetOffset(Offset);
        }
        catch (Exception)
        {
            // Silently handle initialization errors
        }
    }

    private async Task EditorLoad()
    {
        try
        {
            IsPlayControlEnabled = false;
            var useOgg = File.Exists(_maidataDir + "/track.ogg");
            var trackPath = _maidataDir + "/track" + (useOgg ? ".ogg" : ".mp3");

            var bgPath = _maidataDir + "/bg.jpg";
            if (!File.Exists(bgPath)) bgPath = _maidataDir + "/bg.png";
            if (!File.Exists(bgPath)) bgPath = "";

            var pvPath = _maidataDir + "/pv.mp4";
            if (!File.Exists(pvPath)) pvPath = _maidataDir + "/bg.mp4";
            if (!File.Exists(pvPath)) pvPath = "";

            if (!await CheckViewerConnection())
            {
                return;
            }
            // HTTP-based viewer doesn't need explicit loading - chart data is sent with play command
        }
        catch
        {
        }
    }

    //return: isCancel
    public async Task<bool> AskSave()
    {
        if (!IsSaved)
        {
            var mainWindow = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            var msgBox = MessageBoxManager.GetMessageBoxStandard(
                title: "Warning", 
                text : "Chart not yet saved.\nSave it now?", 
                @enum: ButtonEnum.YesNoCancel, 
                icon : Icon.Warning);
            ButtonResult result;
            if (mainWindow is null)
            {
                result = await msgBox.ShowWindowAsync();
            }
            else
            {
                result = await msgBox.ShowWindowDialogAsync(mainWindow);
            }
            
            switch (result)
            {
                case ButtonResult.Yes:
                    SaveFile();
                    return false;
                case ButtonResult.No:
                    return false;
                default:
                    return true;

            }
        }
        return false;
    }
    public async void SaveFile()
    {
        if (CurrentSimaiFile is null) 
            return;
        lock(_fumenContentChangedSyncLock)
        {
            IsFumenContextChanged = false;
            OriginFumen = CurrentFumen;
        }
        await _simaiParser.DeParseAsync(CurrentSimaiFile, _maidataDir + "/maidata.txt");
        SaveSetting();
    }

    private void RegenerateMajson()
    {
        // Only regenerate if a file is loaded and directory is set
        if (CurrentSimaiFile == null || string.IsNullOrEmpty(_maidataDir))
            return;

        try
        {
            var majson = ChartSerializer.ConvertToMajson(CurrentSimaiFile, SelectedDifficulty);
            ChartSerializer.SaveMajdataJson(majson, _maidataDir);
        }
        catch (Exception)
        {
            // Silently handle errors during regeneration
        }
    }

    private void SaveSetting()
    {
        if (string.IsNullOrEmpty(_maidataDir)) return;

        var setting = new MajSetting
        {
            lastEditDiff = SelectedDifficulty,
            lastEditTime = TrackTime,
            BGM_Level = BgmLevel,
            Answer_Level = AnswerLevel,
            Judge_Level = JudgeLevel,
            Break_Level = BreakLevel,
            Break_Slide_Level = BreakSlideLevel,
            Slide_Level = SlideLevel,
            Ex_Level = ExLevel,
            Touch_Level = TouchLevel,
            Hanabi_Level = HanabiLevel,
            SFX_Latency_Compensation = SfxLatencyCompensation
        };

        var json = JsonConvert.SerializeObject(setting, Formatting.Indented);
        File.WriteAllText(Path.Combine(_maidataDir, majSettingFilename), json);
    }

    private void ReadSetting()
    {
        var path = Path.Combine(_maidataDir, majSettingFilename);
        if (!File.Exists(path)) return;

        try
        {
            var setting = JsonConvert.DeserializeObject<MajSetting>(File.ReadAllText(path));
            if (setting == null) return;

            SelectedDifficulty = setting.lastEditDiff;
            TrackTime = setting.lastEditTime;
            BgmLevel = setting.BGM_Level;
            AnswerLevel = setting.Answer_Level;
            JudgeLevel = setting.Judge_Level;
            BreakLevel = setting.Break_Level;
            BreakSlideLevel = setting.Break_Slide_Level;
            SlideLevel = setting.Slide_Level;
            ExLevel = setting.Ex_Level;
            TouchLevel = setting.Touch_Level;
            HanabiLevel = setting.Hanabi_Level;
            SfxLatencyCompensation = setting.SFX_Latency_Compensation;

            // Save updated settings to handle any version differences
            SaveSetting();
        }
        catch (Exception)
        {
            // Silently handle settings loading errors
        }
    }

    public void OpenBpmTapWindow()
    {
        new BpmTapWindow().Show();
    }

    public async void OpenSoundSettingWindow()
    {
        var mainWindow = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        if (mainWindow?.MainWindow is null) return;

        var window = new SoundSettingWindow();
        window.DataContext = this;
        await window.ShowDialog(mainWindow.MainWindow);
    }
    public async void OpenChartInfoWindow()
    {
        if (CurrentSimaiFile is null) return;
        var mainWindow = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        if (mainWindow is null || mainWindow.MainWindow is null) return;
        var window = new ChartInfoWindow();
        window.DataContext = new ChartInfoViewModel()
        {
            Title = CurrentSimaiFile.Title,
            Artist = CurrentSimaiFile.Artist,
            SimaiCommands = new ObservableCollection<SimaiCommand>(CurrentSimaiFile.Commands),
            MaidataDir = _maidataDir
        };
        await window.ShowDialog(mainWindow.MainWindow);
        var datacontext = window.DataContext as ChartInfoViewModel;
        if (datacontext is null || CurrentSimaiFile is null) throw new Exception("Wtf");
        CurrentSimaiFile.Title = datacontext.Title ?? string.Empty;
        CurrentSimaiFile.Artist = datacontext.Artist ?? string.Empty;
        CurrentSimaiFile.Commands = datacontext.SimaiCommands?.ToArray() ?? Array.Empty<SimaiCommand>();
        await Task.Delay(100);
        OnPropertyChanged(nameof(CurrentSimaiFile));
        await EditorLoad();
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

    
    public async void PlayPause(TextEditor textEditor)
    {
        bool shouldRecoverPlayControl = true;
        try
        {
            IsPlayControlEnabled = false;
            if (!await CheckViewerConnection())
            {
                return;
            }

            // For HTTP-based communication, we don't track state like WebSocket
            // Just send the appropriate command based on current playing state
            if (IsPlaying)
            {
                await _viewerConnection.PausePlaybackAsync();
                // Pause audio instead of stopping
                if (_audioManager != null)
                {
                    _audioManager.PauseBgm();
                    _audioManager.StopSfxLoop();
                }
                IsPlaying = false;
                IsPlayControlEnabled = true;
                return;
            }

            shouldRecoverPlayControl = false;
            playStartTime = TrackTime;
            _textEditor = textEditor;

            // Convert chart to Majson format and send to viewer
            if (CurrentSimaiFile != null)
            {
                Models.Majson majson;
                try
                {
                    majson = ChartSerializer.ConvertToMajson(CurrentSimaiFile, SelectedDifficulty);
                    ChartSerializer.SaveMajdataJson(majson, _maidataDir);

                    // Load BGM and generate SFX timings
                    if (_audioManager != null)
                    {
                        var useOgg = File.Exists(Path.Combine(_maidataDir, "track.ogg"));
                        var bgmPath = Path.Combine(_maidataDir, useOgg ? "track.ogg" : "track.mp3");
                        _audioManager.LoadBgm(bgmPath);
                        _audioManager.SetSfxOffset(Offset);
                        _audioManager.GenerateSfxTimings(majson.timingList, playStartTime);
                    }
                }
                catch (Exception)
                {
                    // Create a minimal majson for testing
                    majson = new Models.Majson
                    {
                        title = CurrentSimaiFile.Title ?? "Test",
                        artist = CurrentSimaiFile.Artist ?? "Test",
                        timingList = new System.Collections.Generic.List<Models.SimaiTimingPoint>()
                    };
                    ChartSerializer.SaveMajdataJson(majson, _maidataDir);
                }

                var jsonPath = System.IO.Path.Combine(_maidataDir, "majdata.json");
                await _viewerConnection.StartPlaybackAsync(
                    jsonPath,
                    DateTime.Now,
                    (float)TrackTime,  // Playback position, not offset
                    7.5f, // playSpeed
                    7.5f, // touchSpeed
                    1.0f, // audioSpeed
                    0.6f, // backgroundCover
                    EditorComboIndicator.None, // comboStatusType
                    false, // smoothSlideAnime
                    EditorPlayMethod.DJAuto); // editorPlayMethod

                OnPlayStarted();
            }
        }
        finally
        {
            if (shouldRecoverPlayControl)
                IsPlayControlEnabled = true;
        }
    }

    public async void PlayStop(TextEditor textEditor)
    {
        bool shouldRecoverPlayControl = true;
        try
        {
            IsPlayControlEnabled = false;
            if (!await CheckViewerConnection())
            {
                TrackTime = playStartTime;
                return;
            }

            if (IsPlaying)
            {
                _isBackToStartOnPlayStop = true;
                await _viewerConnection.StopPlaybackAsync();
                OnPlayStopped();
                return;
            }

            shouldRecoverPlayControl = false;
            playStartTime = TrackTime;
            _textEditor = textEditor;

            // Convert chart to Majson format and send to viewer
            if (CurrentSimaiFile != null)
            {
                Models.Majson majson;
                try
                {
                    majson = ChartSerializer.ConvertToMajson(CurrentSimaiFile, SelectedDifficulty);
                    ChartSerializer.SaveMajdataJson(majson, _maidataDir);

                    // Load BGM and generate SFX timings
                    if (_audioManager != null)
                    {
                        var useOgg = File.Exists(Path.Combine(_maidataDir, "track.ogg"));
                        var bgmPath = Path.Combine(_maidataDir, useOgg ? "track.ogg" : "track.mp3");
                        _audioManager.LoadBgm(bgmPath);
                        _audioManager.SetSfxOffset(Offset);
                        _audioManager.GenerateSfxTimings(majson.timingList, playStartTime);
                    }
                }
                catch (Exception)
                {
                    // Create a minimal majson for testing
                    majson = new Models.Majson
                    {
                        title = CurrentSimaiFile.Title ?? "Test",
                        artist = CurrentSimaiFile.Artist ?? "Test",
                        timingList = new System.Collections.Generic.List<Models.SimaiTimingPoint>()
                    };
                    ChartSerializer.SaveMajdataJson(majson, _maidataDir);
                }

                var jsonPath = System.IO.Path.Combine(_maidataDir, "majdata.json");
                await _viewerConnection.StartPlaybackAsync(
                    jsonPath,
                    DateTime.Now,
                    (float)TrackTime,  // Playback position, not offset
                    7.5f, // playSpeed
                    7.5f, // touchSpeed
                    1.0f, // audioSpeed
                    0.6f, // backgroundCover
                    EditorComboIndicator.None, // comboStatusType
                    false, // smoothSlideAnime
                    EditorPlayMethod.DJAuto); // editorPlayMethod

                OnPlayStarted();
            }
        }
        finally
        {
            if (shouldRecoverPlayControl)
                IsPlayControlEnabled = true;
        }
    }
    
    private async void OnPlayStarted()
    {
        IsPlaying = true;
        IsPlayControlEnabled = true;

        // Update loop controller with current chart for beat snapping
        LoopViewModel.UpdateChart(CurrentSimaiChart);

        // Start audio playback
        if (_audioManager != null)
        {
            _audioManager.PlayBgm(playStartTime);
            _audioManager.StartSfxLoop();
        }

        await Task.Run(async () =>
        {
            Stopwatch watch = new Stopwatch();
            watch.Start();
            var timeA = watch.Elapsed;
            IsAnimated = false;
            while (IsPlaying && _viewerConnection.IsViewerRunning)
            {
                TrackTime = watch.ElapsedMilliseconds / 1000d + playStartTime;

                // Check loop condition
                if (_loopController.ShouldLoopWithOffset(TrackTime, Offset))
                {
                    // Get the loop start time BEFORE triggering (to avoid race condition)
                    var loopStart = (_loopController.CurrentRegion?.StartTime ?? 0) + Offset;

                    await OnLoopTriggeredAsync();

                    // Set playStartTime directly after async call completes
                    playStartTime = loopStart;
                    watch.Restart();

                    // Skip the rest of the loop iteration after triggering loop
                    var timeB_loop = watch.Elapsed;
                    var waitTime_loop = Math.Max(16 - (int)(timeB_loop - timeA).TotalMilliseconds, 0);
                    await Task.Delay(waitTime_loop);
                    continue;
                }

                // Stop playback if we've reached the end of the song
                if (SongTrackInfo != null && TrackTime >= SongTrackInfo.Length)
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        OnPlayStopped();
                    });
                    break;
                }

                if (IsFollowCursor && CurrentSimaiChart != null && _textEditor != null)
                {
                    var nearestNote = CurrentSimaiChart.CommaTimings.MinBy(o => Math.Abs(o.Timing + Offset - TrackTime));
                    if (nearestNote is null) continue;

                    var point = new Point(nearestNote.RawTextPositionX, nearestNote.RawTextPositionY);
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        SeekToDocPos(point, _textEditor!);
                    });

                }
                var timeB = watch.Elapsed;
                var waitTime = Math.Max(16 - (int)(timeB - timeA).TotalMilliseconds, 0);
                await Task.Delay(waitTime);
            }
            IsAnimated = true;
        });
    }
    public void Stop() => Stop(true);

    private async void Stop(bool isBackToStart)
    {
        try
        {
            IsPlayControlEnabled = false;

            // Signal the loop to stop FIRST
            if (IsPlaying)
            {
                IsPlaying = false;
                await Task.Delay(100); // Give the loop time to exit

                // Stop audio
                if (_audioManager != null)
                {
                    _audioManager.StopSfxLoop();
                    _audioManager.StopBgm();
                }
            }

            // Send Stop command to viewer (clears notes from viewer)
            if (!await CheckViewerConnection())
            {
                if (isBackToStart)
                    TrackTime = playStartTime;
                return;
            }

            await _viewerConnection.StopPlaybackAsync();

            if (isBackToStart)
            {
                // Returning to start - reset TrackTime
                TrackTime = playStartTime;
            }
            // else: scrubbing - TrackTime stays at new position, viewer is cleared
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error stopping playback: {ex.Message}");
        }
        finally
        {
            IsPlayControlEnabled = true;
        }

    }

    private async void OnPlayStopped()
    {
        await Task.Delay(32); // Wait the OnPlayStarted Loop to end
        IsPlaying = false;

        // Stop audio playback
        if (_audioManager != null)
        {
            _audioManager.StopSfxLoop();
            _audioManager.StopBgm();
        }
        if (_isBackToStartOnPlayStop)
            TrackTime = playStartTime;
        IsPlayControlEnabled = true;
    }

    /// <summary>
    /// Handles loop trigger - jumps playback back to loop start.
    /// </summary>
    private async Task OnLoopTriggeredAsync()
    {
        // Re-entrancy guard - prevent overlapping loop triggers
        if (_isProcessingLoop) return;
        if (_loopController.CurrentRegion is null) return;

        _isProcessingLoop = true;
        try
        {
            // Get loop start and convert to display time (add offset)
            var loopStart = _loopController.CurrentRegion.StartTime + Offset;

            // Send Stop command to viewer to clear notes
            await _viewerConnection.StopPlaybackAsync();
            await Task.Delay(32); // Brief pause for viewer to process

            // Stop audio
            if (_audioManager != null)
            {
                _audioManager.StopSfxLoop();
                _audioManager.StopBgm();
            }

            // Reset time to loop start
            playStartTime = loopStart;
            TrackTime = loopStart;

            // Regenerate chart and send Start command to viewer
            if (CurrentSimaiFile != null)
            {
                var majson = ChartSerializer.ConvertToMajson(CurrentSimaiFile, SelectedDifficulty);
                ChartSerializer.SaveMajdataJson(majson, _maidataDir);

                var jsonPath = Path.Combine(_maidataDir, "majdata.json");
                await _viewerConnection.StartPlaybackAsync(
                    jsonPath,
                    DateTime.Now,
                    (float)loopStart,
                    7.5f, // playSpeed
                    7.5f, // touchSpeed
                    1.0f, // audioSpeed
                    0.6f, // backgroundCover
                    EditorComboIndicator.None,
                    false,
                    EditorPlayMethod.DJAuto);

                // Restart audio at loop position
                if (_audioManager != null)
                {
                    _audioManager.PlayBgm(loopStart);
                    _audioManager.GenerateSfxTimings(majson.timingList, loopStart);
                    _audioManager.StartSfxLoop();
                }
            }
        }
        finally
        {
            _isProcessingLoop = false;
        }
    }

    private async void OnLoadRequired()
    {
        await EditorLoad();
    }

    async Task<bool> CheckViewerConnection()
    {
        // Check if MajdataView is running, launch if needed
        if (!_viewerConnection.IsViewerRunning)
        {
            if (!await _viewerConnection.LaunchViewerIfNeededAsync())
            {
                // Failed to launch MajdataView
                OnPropertyChanged(nameof(IsConnected));
                return false;
            }
        }

        // For HTTP connection, we don't need to explicitly connect like WebSocket
        // The connection check is implicit in each HTTP request
        OnPropertyChanged(nameof(IsConnected));
        return _viewerConnection.IsViewerRunning;
    }
    public void SeekToDocPos(Point position, TextEditor editor)
    {
        var offset = editor.Document.GetOffset((int)position.Y + 1, (int)position.X);
        editor.Select(offset, 0);
        editor.ScrollTo((int)position.Y + 1, (int)position.X);
        editor.Focus();
    }

    // Loop region tracking
    private double? _pendingLoopStart;
    private bool _loopStartSet;
    private bool _hasLoopRegion;

    /// <summary>
    /// Gets whether the loop start point has been set.
    /// </summary>
    public bool LoopStartSet => _loopStartSet;

    /// <summary>
    /// Gets whether a loop region has been set (for button styling).
    /// </summary>
    public bool HasLoopRegion => _hasLoopRegion;

    /// <summary>
    /// Sets the loop start point to the current track time.
    /// </summary>
    [RelayCommand]
    public void SetLoopStart()
    {
        _pendingLoopStart = TrackTime; // Store display time (with offset) for snapping
        _loopStartSet = true;
        OnPropertyChanged(nameof(LoopStartSet));
    }

    /// <summary>
    /// Sets the loop end point to the current track time and creates the loop region.
    /// </summary>
    [RelayCommand]
    public void SetLoopEnd()
    {
        if (_pendingLoopStart is null) return;

        var endTime = TrackTime; // Display time (with offset)
        var startTime = _pendingLoopStart.Value;

        // Convert to chart times for storage (without offset)
        var chartStartTime = startTime - Offset;
        var chartEndTime = endTime - Offset;

        // Ensure start is before end
        if (chartStartTime > chartEndTime)
        {
            (chartStartTime, chartEndTime) = (chartEndTime, chartStartTime);
        }

        // Try to set the region via LoopViewModel
        var success = LoopViewModel?.TrySetRegion(chartStartTime, chartEndTime, CurrentSimaiChart) ?? false;
        Console.WriteLine($"[LOOP] TrySetRegion: start={chartStartTime:F3}, end={chartEndTime:F3}, success={success}, Offset={Offset:F3}");
        if (success)
        {
            _hasLoopRegion = true;
            OnPropertyChanged(nameof(HasLoopRegion));
            Console.WriteLine($"[LOOP] Region set successfully! IsEnabled={_loopController.IsEnabled}");
        }
        else
        {
            Console.WriteLine($"[LOOP] Region set FAILED!");
        }

        // Clear the pending start
        _pendingLoopStart = null;
        _loopStartSet = false;
        OnPropertyChanged(nameof(LoopStartSet));
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
        if (e.PropertyName == nameof(CurrentSimaiFile))
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
