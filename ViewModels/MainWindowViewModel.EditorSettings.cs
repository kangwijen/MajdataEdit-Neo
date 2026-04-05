using System;
using System.IO;
using System.Threading.Tasks;
using MajdataEdit_Neo.Models;
using Newtonsoft.Json;

namespace MajdataEdit_Neo.ViewModels;

public partial class MainWindowViewModel
{
    private const float MinEditorFontSize = 6f;
    private const float MaxEditorFontSize = 72f;

    private readonly string _editorSettingFilename = "EditorSetting.json";
    private EditorSetting _editorSetting = new();
    private bool _isLoadingEditorSetting;

    // Expose hotkey strings to XAML (HotKey property accepts these formats).
    public string DecreasePlaybackSpeedKey => _editorSetting.DecreasePlaybackSpeedKey;
    public string IncreasePlaybackSpeedKey => _editorSetting.IncreasePlaybackSpeedKey;

    public string Mirror180Key => _editorSetting.Mirror180Key;
    public string Mirror45Key => _editorSetting.Mirror45Key;
    public string MirrorCcw45Key => _editorSetting.MirrorCcw45Key;
    public string MirrorLeftRightKey => _editorSetting.MirrorLeftRightKey;
    public string MirrorUpDownKey => _editorSetting.MirrorUpDownKey;

    public string PlayPauseKey => _editorSetting.PlayPauseKey;
    public string PlayStopKey => _editorSetting.PlayStopKey;
    public string RecordModeKey => _editorSetting.RecordModeKey;
    public string SendViewerKey => _editorSetting.SendViewerKey;
    public string SaveKey => _editorSetting.SaveKey;

    private string GetEditorSettingPath()
    {
        // Match legacy Majdata behavior: relative to the app working directory.
        return Path.Combine(Environment.CurrentDirectory, _editorSettingFilename);
    }

    private void ReadEditorSetting()
    {
        _isLoadingEditorSetting = true;
        var fontSizeClampedOnLoad = false;
        try
        {
            var path = GetEditorSettingPath();
            if (!File.Exists(path))
            {
                _editorSetting = new EditorSetting();
                File.WriteAllText(path, JsonConvert.SerializeObject(_editorSetting, Formatting.Indented));
            }
            else
            {
                var json = File.ReadAllText(path);
                _editorSetting = JsonConvert.DeserializeObject<EditorSetting>(json) ?? new EditorSetting();
            }

            // Neo UI supports Off/Combo + Default/DJAuto and independent note/touch speed.
            CenterDisplayMode = _editorSetting.comboStatusType == EditorComboIndicator.Combo ? 1 : 0;
            PlayModeIndex = _editorSetting.editorPlayMethod == EditorPlayMethod.DJAuto ? 1 : 0;
            NoteSpeed = _editorSetting.playSpeed;
            TouchSpeed = _editorSetting.touchSpeed;

            var fontSize = _editorSetting.FontSize;
            if (fontSize < MinEditorFontSize || fontSize > MaxEditorFontSize)
            {
                fontSize = Math.Clamp(fontSize, MinEditorFontSize, MaxEditorFontSize);
                _editorSetting.FontSize = fontSize;
                fontSizeClampedOnLoad = true;
            }

            EditorFontSize = fontSize;

            AudioLevelsSnapshot.From(_editorSetting).CopyToViewModel(this);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to read EditorSetting.json: {ex.Message}");
        }
        finally
        {
            _isLoadingEditorSetting = false;
        }

        if (fontSizeClampedOnLoad)
            SaveEditorSetting();
    }

    private void SaveEditorSetting()
    {
        try
        {
            var path = GetEditorSettingPath();
            File.WriteAllText(path, JsonConvert.SerializeObject(_editorSetting, Formatting.Indented));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to save EditorSetting.json: {ex.Message}");
        }
    }

    private EditorComboIndicator GetCenterDisplayIndicator()
    {
        return CenterDisplayMode == 1 ? EditorComboIndicator.Combo : EditorComboIndicator.None;
    }

    private EditorPlayMethod GetSelectedPlayMethod()
    {
        return PlayModeIndex switch
        {
            0 => EditorPlayMethod.Classic,
            1 => EditorPlayMethod.DJAuto,
            _ => EditorPlayMethod.DJAuto
        };
    }

    partial void OnCenterDisplayModeChanged(int value)
    {
        if (_isLoadingEditorSetting) return;
        _editorSetting.comboStatusType = value == 1 ? EditorComboIndicator.Combo : EditorComboIndicator.None;
        SaveEditorSetting();
    }

    partial void OnPlayModeIndexChanged(int value)
    {
        if (_isLoadingEditorSetting) return;
        _editorSetting.editorPlayMethod = value == 1 ? EditorPlayMethod.DJAuto : EditorPlayMethod.Classic;
        SaveEditorSetting();
    }

    partial void OnNoteSpeedChanged(float value)
    {
        if (_isLoadingEditorSetting) return;
        _editorSetting.playSpeed = value;
        SaveEditorSetting();
    }

    partial void OnTouchSpeedChanged(float value)
    {
        if (_isLoadingEditorSetting) return;
        _editorSetting.touchSpeed = value;
        SaveEditorSetting();
    }

    partial void OnEditorFontSizeChanged(float value)
    {
        if (_isLoadingEditorSetting) return;
        var clamped = Math.Clamp(value, MinEditorFontSize, MaxEditorFontSize);
        if (Math.Abs(clamped - value) > 0.0001f)
        {
            EditorFontSize = clamped;
            return;
        }

        _editorSetting.FontSize = value;
        SaveEditorSetting();
    }

    private bool _isAdjustingPlaybackSpeed;
    private const float PlaybackSpeedStep = 0.25f;

    public async void DecreasePlaybackSpeed()
    {
        await AdjustPlaybackSpeedAsync(-1);
    }

    public async void IncreasePlaybackSpeed()
    {
        await AdjustPlaybackSpeedAsync(1);
    }

    private async Task AdjustPlaybackSpeedAsync(float direction)
    {
        if (_isAdjustingPlaybackSpeed) return;
        if (!IsLoaded) return;
        if (CurrentSimaiFile is null) return;

        _isAdjustingPlaybackSpeed = true;
        try
        {
            var target = NoteSpeed + PlaybackSpeedStep * direction;
            target = target < 1f ? 1f : target > 10f ? 10f : target;
            if (Math.Abs(target - NoteSpeed) < 0.0001f) return;

            NoteSpeed = target; // persists to EditorSetting.json via OnNoteSpeedChanged

            if (!IsPlaying) return;

            // If the user changes speed mid-play, avoid re-applying "back to start" behavior later.
            _isBackToStartOnPlayStop = false;
            IsPlayControlEnabled = false;

            await RestartPlaybackFromCurrentTimeAsync();
        }
        finally
        {
            _isAdjustingPlaybackSpeed = false;
            IsPlayControlEnabled = true;
        }
    }

    private async Task RestartPlaybackFromCurrentTimeAsync()
    {
        // Stop audio + viewer, then start playback again at the current TrackTime.
        try
        {
            IsPlaying = false;
            await Task.Delay(100); // allow the existing play loop to exit

            if (_audioManager != null)
            {
                _audioManager.StopSfxLoop();
                _audioManager.StopBgm();
            }

            if (!await CheckViewerConnection())
            {
                return;
            }

            await _viewerConnection.StopPlaybackAsync();

            playStartTime = TrackTime;

            if (CurrentSimaiFile == null) return;

            Models.Majson majson;
            try
            {
                majson = ChartSerializer.ConvertToMajson(CurrentSimaiFile, SelectedDifficulty);
                ChartSerializer.SaveMajdataJson(majson, _maidataDir);

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
                // Create a minimal majson for testing/fallback.
                majson = new Models.Majson
                {
                    title = CurrentSimaiFile.Title ?? "Test",
                    artist = CurrentSimaiFile.Artist ?? "Test",
                    timingList = new System.Collections.Generic.List<Models.SimaiTimingPoint>()
                };
                ChartSerializer.SaveMajdataJson(majson, _maidataDir);
            }

            var jsonPath = Path.Combine(_maidataDir, "majdata.json");
            IsRecordModeActive = false;
            await _viewerConnection.StartPlaybackAsync(
                jsonPath,
                DateTime.Now,
                (float)TrackTime,
                _editorSetting.playSpeed, // noteSpeed
                _editorSetting.touchSpeed, // touchSpeed
                1.0f, // audioSpeed
                _editorSetting.backgroundCover, // backgroundCover
                GetCenterDisplayIndicator(), // comboStatusType
                _editorSetting.SmoothSlideAnime, // smoothSlideAnime
                GetSelectedPlayMethod(), // editorPlayMethod
                ChartBpm.GetBpmAtChartTime(majson, TrackTime));

            OnPlayStarted();
        }
        catch
        {
            // Keep playback speed adjustment non-fatal.
        }
    }
}
