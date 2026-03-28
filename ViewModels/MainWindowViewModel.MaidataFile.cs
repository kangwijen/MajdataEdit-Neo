using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Input;
using MajdataEdit_Neo.Models;
using MajdataEdit_Neo.Views;
using MajSimai;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using Newtonsoft.Json;
using System.Collections.ObjectModel;

namespace MajdataEdit_Neo.ViewModels;

public partial class MainWindowViewModel
{
    private const string majSettingFilename = "majSetting.json";

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
            if (File.Exists(_maidataDir + "/maidata.txt"))
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
            _autoSaveManager.Enabled = true;
            _internalAutoSaveContentProvider.Content = await File.ReadAllTextAsync(maidataPath);
            UpdateAutoSaveContext();
            await EditorLoad();
            ReadSetting();
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

    // return: isCancel
    public async Task<bool> AskSave()
    {
        if (!IsSaved)
        {
            var mainWindow = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            var msgBox = MessageBoxManager.GetMessageBoxStandard(
                title: "Warning",
                text: "Chart not yet saved.\nSave it now?",
                @enum: ButtonEnum.YesNoCancel,
                icon: Icon.Warning);
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
        lock (_fumenContentChangedSyncLock)
        {
            IsFumenContextChanged = false;
            OriginFumen = CurrentFumen;
        }

        await _simaiParser.DeParseAsync(CurrentSimaiFile, _maidataDir + "/maidata.txt");
        SaveSetting();
    }

    private void RegenerateMajson()
    {
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

            // Do not apply majSetting.json volume fields to the audio engine: they desynced global
            // EditorSetting.json (VM) from playback until the user opened Settings → Audio.
            // Volumes always follow VM / EditorSetting; majSetting still stores them on SaveSetting for portability.
            ApplyViewModelAudioLevelsToAudioEngine();
        }
        catch (Exception)
        {
            // Silently handle settings loading errors
        }
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
}
