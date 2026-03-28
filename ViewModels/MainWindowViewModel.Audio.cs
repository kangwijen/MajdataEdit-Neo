using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using MajdataEdit_Neo.Models;
using MajdataEdit_Neo.Views;

namespace MajdataEdit_Neo.ViewModels;

public partial class MainWindowViewModel
{
    /// <summary>While true, Sound Settings sliders may fire spurious writes during attach; do not persist or touch the audio engine.</summary>
    private bool _isSoundDialogAttaching;

    /// <summary>Single representation of the nine persisted wave volumes (EditorSetting.json + bound properties).</summary>
    private readonly record struct AudioLevelsSnapshot(
        float Bgm,
        float Answer,
        float Judge,
        float Break,
        float BreakSlide,
        float Slide,
        float Ex,
        float Touch,
        float Hanabi)
    {
        public static AudioLevelsSnapshot From(EditorSetting s) => new(
            s.Default_BGM_Level,
            s.Default_Answer_Level,
            s.Default_Judge_Level,
            s.Default_Break_Level,
            s.Default_Break_Slide_Level,
            s.Default_Slide_Level,
            s.Default_Ex_Level,
            s.Default_Touch_Level,
            s.Default_Hanabi_Level);

        public void CopyTo(EditorSetting s)
        {
            s.Default_BGM_Level = Bgm;
            s.Default_Answer_Level = Answer;
            s.Default_Judge_Level = Judge;
            s.Default_Break_Level = Break;
            s.Default_Break_Slide_Level = BreakSlide;
            s.Default_Slide_Level = Slide;
            s.Default_Ex_Level = Ex;
            s.Default_Touch_Level = Touch;
            s.Default_Hanabi_Level = Hanabi;
        }

        public void CopyToViewModel(MainWindowViewModel vm)
        {
            vm.BgmLevel = Bgm;
            vm.AnswerLevel = Answer;
            vm.JudgeLevel = Judge;
            vm.BreakLevel = Break;
            vm.BreakSlideLevel = BreakSlide;
            vm.SlideLevel = Slide;
            vm.ExLevel = Ex;
            vm.TouchLevel = Touch;
            vm.HanabiLevel = Hanabi;
        }
    }

    /// <summary>Copies global VM levels (EditorSetting.json) to the audio engine.</summary>
    void ApplyViewModelAudioLevelsToAudioEngine()
    {
        if (_audioManager is null) return;
        _audioManager.BgmLevel = BgmLevel;
        _audioManager.AnswerLevel = AnswerLevel;
        _audioManager.JudgeLevel = JudgeLevel;
        _audioManager.BreakLevel = BreakLevel;
        _audioManager.BreakSlideLevel = BreakSlideLevel;
        _audioManager.SlideLevel = SlideLevel;
        _audioManager.ExLevel = ExLevel;
        _audioManager.TouchLevel = TouchLevel;
        _audioManager.HanabiLevel = HanabiLevel;
        _audioManager.UpdateAllVolumes();
    }

    void OnAudioLevelChanged(float value, Action<float> setEditorDefault, Action<AudioManager, float> applyEngine)
    {
        if (!_isLoadingEditorSetting && !_isSoundDialogAttaching)
        {
            setEditorDefault(value);
            SaveEditorSetting();
        }

        if (_isLoadingEditorSetting)
            return;

        if (_isSoundDialogAttaching)
            return;

        if (_audioManager != null)
        {
            applyEngine(_audioManager, value);
            _audioManager.UpdateAllVolumes();
        }
    }

    partial void OnBgmLevelChanged(float value) =>
        OnAudioLevelChanged(value, v => _editorSetting.Default_BGM_Level = v, (am, v) => am.BgmLevel = v);

    partial void OnAnswerLevelChanged(float value) =>
        OnAudioLevelChanged(value, v => _editorSetting.Default_Answer_Level = v, (am, v) => am.AnswerLevel = v);

    partial void OnJudgeLevelChanged(float value) =>
        OnAudioLevelChanged(value, v => _editorSetting.Default_Judge_Level = v, (am, v) => am.JudgeLevel = v);

    partial void OnBreakLevelChanged(float value) =>
        OnAudioLevelChanged(value, v => _editorSetting.Default_Break_Level = v, (am, v) => am.BreakLevel = v);

    partial void OnBreakSlideLevelChanged(float value) =>
        OnAudioLevelChanged(value, v => _editorSetting.Default_Break_Slide_Level = v, (am, v) => am.BreakSlideLevel = v);

    partial void OnSlideLevelChanged(float value) =>
        OnAudioLevelChanged(value, v => _editorSetting.Default_Slide_Level = v, (am, v) => am.SlideLevel = v);

    partial void OnExLevelChanged(float value) =>
        OnAudioLevelChanged(value, v => _editorSetting.Default_Ex_Level = v, (am, v) => am.ExLevel = v);

    partial void OnTouchLevelChanged(float value) =>
        OnAudioLevelChanged(value, v => _editorSetting.Default_Touch_Level = v, (am, v) => am.TouchLevel = v);

    partial void OnHanabiLevelChanged(float value) =>
        OnAudioLevelChanged(value, v => _editorSetting.Default_Hanabi_Level = v, (am, v) => am.HanabiLevel = v);

    public async void OpenSoundSettingWindow()
    {
        var mainWindow = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        if (mainWindow?.MainWindow is null) return;

        var snapshot = AudioLevelsSnapshot.From(_editorSetting);

        ApplyViewModelAudioLevelsToAudioEngine();

        _isSoundDialogAttaching = true;
        var window = new SoundSettingWindow();
        window.DataContext = this;
        window.Opened += (_, _) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                _isLoadingEditorSetting = true;
                try
                {
                    snapshot.CopyTo(_editorSetting);
                    snapshot.CopyToViewModel(this);
                }
                finally
                {
                    _isLoadingEditorSetting = false;
                }

                _isSoundDialogAttaching = false;
                ApplyViewModelAudioLevelsToAudioEngine();
            }, DispatcherPriority.Loaded);
        };

        try
        {
            await window.ShowDialog(mainWindow.MainWindow);
        }
        finally
        {
            _isSoundDialogAttaching = false;
        }
    }
}
