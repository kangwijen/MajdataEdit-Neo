using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Threading;
using AvaloniaEdit;
using CommunityToolkit.Mvvm.Input;
using MajdataEdit_Neo.Models;

namespace MajdataEdit_Neo.ViewModels;

public partial class MainWindowViewModel
{
    double playStartTime = 0d;
    TextEditor? _textEditor;

    private double? _pendingLoopStart;
    private bool _loopStartSet;
    private bool _hasLoopRegion;

    /// <summary>Previous display <see cref="TrackTime"/> sample for loop end crossing detection.</summary>
    double _loopPrevDisplayTime = double.NegativeInfinity;

    /// <summary>Cancels delayed <see cref="OnPlayStarted"/> after record mode HTTP success (viewer intro window).</summary>
    CancellationTokenSource? _recordIntroCts;

    partial void OnIsFollowCursorChanged(bool value)
    {
        if (value && IsPlaying)
            FocusTextEditorForFollowCursor();
    }

    /// <summary>
    /// Focuses the simai editor when follow-cursor is enabled, scheduled on the UI thread.
    /// Not called on every playback poll so toolbar and waveform stay clickable.
    /// </summary>
    void FocusTextEditorForFollowCursor()
    {
        if (_textEditor is null || !IsFollowCursor)
            return;
        Dispatcher.UIThread.Post(() =>
        {
            if (_textEditor is null || !IsFollowCursor)
                return;
            _textEditor.TextArea.Focus();
        }, DispatcherPriority.Input);
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

            if (IsPlaying)
            {
                await _viewerConnection.PausePlaybackAsync();
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

            if (CurrentSimaiFile != null)
            {
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
                    _editorSetting.playSpeed,
                    _editorSetting.touchSpeed,
                    1.0f,
                    _editorSetting.backgroundCover,
                    GetCenterDisplayIndicator(),
                    _editorSetting.SmoothSlideAnime,
                    GetSelectedPlayMethod());

                OnPlayStarted();
            }
        }
        finally
        {
            if (shouldRecoverPlayControl)
                IsPlayControlEnabled = true;
        }
    }

    /// <summary>
    /// MajdataView <c>OpStart</c>: song detail / jacket intro, then delayed chart start (no FFmpeg or MP4; that is only on viewer <c>Record</c>).
    /// </summary>
    public async void RecordMode(TextEditor textEditor)
    {
        bool shouldRecoverPlayControl = true;
        try
        {
            IsPlayControlEnabled = false;
            if (!await CheckViewerConnection())
            {
                return;
            }

            if (IsPlaying)
            {
                await _viewerConnection.PausePlaybackAsync();
                if (_audioManager != null)
                {
                    _audioManager.PauseBgm();
                    _audioManager.StopSfxLoop();
                }

                IsPlaying = false;
                IsRecordModeActive = false;
                IsPlayControlEnabled = true;
                return;
            }

            shouldRecoverPlayControl = false;
            playStartTime = TrackTime;
            _textEditor = textEditor;

            if (CurrentSimaiFile != null)
            {
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
                    majson = new Models.Majson
                    {
                        title = CurrentSimaiFile.Title ?? "Test",
                        artist = CurrentSimaiFile.Artist ?? "Test",
                        timingList = new System.Collections.Generic.List<Models.SimaiTimingPoint>()
                    };
                    ChartSerializer.SaveMajdataJson(majson, _maidataDir);
                }

                var jsonPath = Path.Combine(_maidataDir, "majdata.json");

                // SFX/track_start.wav length plus 1s extra before chart start (viewer + local delay).
                float introSec = 3f;
                if (_audioManager != null)
                {
                    var d = _audioManager.GetTrackStartSfxDurationSeconds();
                    if (d > 0 && !double.IsNaN(d) && !double.IsInfinity(d))
                        introSec = (float)d;
                    else
                        introSec = _editorSetting.RecordIntroDelaySeconds ?? 3f;
                    _audioManager.PlayTrackStartSfx();
                }
                else
                {
                    introSec = _editorSetting.RecordIntroDelaySeconds ?? 3f;
                }

                introSec += 2f;

                if (introSec < 0f) introSec = 0f;
                var startAt = DateTime.Now.AddSeconds(introSec);

                // OpStart = jacket / song detail only. Record control would start ScreenRecorder + FFmpeg (out.mp4).
                var introOk = await _viewerConnection.StartOpPlaybackAsync(
                    jsonPath,
                    startAt,
                    (float)TrackTime,
                    _editorSetting.playSpeed,
                    _editorSetting.touchSpeed,
                    1.0f,
                    _editorSetting.backgroundCover,
                    GetCenterDisplayIndicator(),
                    _editorSetting.SmoothSlideAnime,
                    GetSelectedPlayMethod());

                if (!introOk)
                {
                    shouldRecoverPlayControl = true;
                    return;
                }

                IsRecordModeActive = true;

                // Do not set IsPlaying / IsPlayControlEnabled until OnPlayStarted — same as legacy Op_Button disabled
                // during intro. Otherwise Play/Pause sends Pause while viewer is still in record intro.
                _recordIntroCts?.Cancel();
                _recordIntroCts = new CancellationTokenSource();
                var introCt = _recordIntroCts.Token;

                _ = Task.Run(async () =>
                {
                    try
                    {
                        var wait = startAt - DateTime.Now;
                        if (wait > TimeSpan.Zero)
                            await Task.Delay(wait, introCt);
                        if (introCt.IsCancellationRequested)
                        {
                            await Dispatcher.UIThread.InvokeAsync(() => { IsRecordModeActive = false; });
                            return;
                        }

                        // After intro, viewer needs explicit Start (control 0) to begin chart playback.
                        var kickOk = await _viewerConnection.StartPlaybackAsync(
                            jsonPath,
                            DateTime.Now,
                            (float)playStartTime,
                            _editorSetting.playSpeed,
                            _editorSetting.touchSpeed,
                            1.0f,
                            _editorSetting.backgroundCover,
                            GetCenterDisplayIndicator(),
                            _editorSetting.SmoothSlideAnime,
                            GetSelectedPlayMethod());

                        if (!kickOk)
                        {
                            await Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                IsRecordModeActive = false;
                                IsPlayControlEnabled = true;
                            });
                            return;
                        }

                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            if (introCt.IsCancellationRequested)
                            {
                                IsRecordModeActive = false;
                                return;
                            }

                            OnPlayStarted();
                        });
                    }
                    catch (OperationCanceledException)
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            IsRecordModeActive = false;
                            if (!IsPlaying)
                                IsPlayControlEnabled = true;
                        });
                    }
                }, introCt);
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

            if (CurrentSimaiFile != null)
            {
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
                    _editorSetting.playSpeed,
                    _editorSetting.touchSpeed,
                    1.0f,
                    _editorSetting.backgroundCover,
                    GetCenterDisplayIndicator(),
                    _editorSetting.SmoothSlideAnime,
                    GetSelectedPlayMethod());

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
        _recordIntroCts?.Dispose();
        _recordIntroCts = null;

        IsPlaying = true;
        IsPlayControlEnabled = true;

        LoopViewModel.UpdateChart(CurrentSimaiChart);

        if (_audioManager != null)
        {
            _audioManager.PlayBgm(playStartTime);
            _audioManager.StartSfxLoop();
        }

        FocusTextEditorForFollowCursor();

        await Task.Run(async () =>
        {
            var watch = new Stopwatch();
            watch.Start();
            var timeA = watch.Elapsed;
            IsAnimated = false;
            _loopPrevDisplayTime = double.NegativeInfinity;
            while (IsPlaying && _viewerConnection.IsViewerRunning)
            {
                var pollTime = watch.ElapsedMilliseconds / 1000d + playStartTime;
                try
                {
                    TrackTime = pollTime;

                    if (_loopController.ShouldLoopWithOffset(_loopPrevDisplayTime, pollTime, Offset))
                    {
                        var loopStart = (_loopController.CurrentRegion?.StartTime ?? 0) + Offset;

                        await OnLoopTriggeredAsync();

                        playStartTime = loopStart;
                        watch.Restart();
                        pollTime = watch.ElapsedMilliseconds / 1000d + playStartTime;
                        TrackTime = pollTime;

                        var timeB_loop = watch.Elapsed;
                        var waitTime_loop = Math.Max(16 - (int)(timeB_loop - timeA).TotalMilliseconds, 0);
                        await Task.Delay(waitTime_loop);
                        continue;
                    }

                    if (SongTrackInfo != null && pollTime >= SongTrackInfo.Length)
                    {
                        await Dispatcher.UIThread.InvokeAsync(() => { OnPlayStopped(); });
                        break;
                    }

                    if (IsFollowCursor && CurrentSimaiChart != null && _textEditor != null)
                    {
                        var commaTimings = CurrentSimaiChart.CommaTimings;
                        if (commaTimings is null || commaTimings.Length == 0)
                            continue;

                        var notePlusOffset = commaTimings
                            .Select(o => new { Note = o, TimingPlusOffset = o.Timing + Offset })
                            .ToArray();

                        var nearestNote = notePlusOffset
                            .Where(x => x.TimingPlusOffset <= pollTime)
                            .OrderByDescending(x => x.TimingPlusOffset)
                            .Select(x => x.Note)
                            .FirstOrDefault();

                        if (nearestNote is null)
                            nearestNote = notePlusOffset.OrderBy(x => x.TimingPlusOffset).Select(x => x.Note).FirstOrDefault();
                        if (nearestNote is null)
                            continue;

                        var point = new Point(nearestNote.RawTextPositionX, nearestNote.RawTextPositionY);

                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            SeekToDocPos(point, _textEditor!, focusEditor: false);
                        });
                    }

                    var timeB = watch.Elapsed;
                    var waitTime = Math.Max(16 - (int)(timeB - timeA).TotalMilliseconds, 0);
                    await Task.Delay(waitTime);
                }
                finally
                {
                    // Use pollTime from this thread; TrackTime property can lag behind on the worker vs UI sync.
                    _loopPrevDisplayTime = pollTime;
                }
            }

            IsAnimated = true;
        });
    }

    public void Stop() => Stop(true);

    private async void Stop(bool isBackToStart)
    {
        try
        {
            _recordIntroCts?.Cancel();
            IsPlayControlEnabled = false;

            if (IsPlaying)
            {
                IsPlaying = false;
                await Task.Delay(100);

                if (_audioManager != null)
                {
                    _audioManager.StopSfxLoop();
                    _audioManager.StopBgm();
                }
            }

            if (!await CheckViewerConnection())
            {
                if (isBackToStart)
                    TrackTime = playStartTime;
                return;
            }

            await _viewerConnection.StopPlaybackAsync();

            if (isBackToStart)
                TrackTime = playStartTime;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error stopping playback: {ex.Message}");
        }
        finally
        {
            IsRecordModeActive = false;
            IsPlayControlEnabled = true;
        }
    }

    private async void OnPlayStopped()
    {
        await Task.Delay(32);
        IsPlaying = false;
        IsRecordModeActive = false;

        if (_audioManager != null)
        {
            _audioManager.StopSfxLoop();
            _audioManager.StopBgm();
        }

        if (_isBackToStartOnPlayStop)
            TrackTime = playStartTime;
        IsPlayControlEnabled = true;
    }

    /// <summary>Handles loop trigger: jump playback back to loop start.</summary>
    private async Task OnLoopTriggeredAsync()
    {
        if (_isProcessingLoop) return;
        var region = _loopController.CurrentRegion;
        if (region is null) return;

        _isProcessingLoop = true;
        try
        {
            var loopStart = region.StartTime + Offset;

            if (_audioManager != null)
            {
                _audioManager.StopSfxLoop();
                _audioManager.StopBgm();
            }

            if (CurrentSimaiFile != null)
            {
                var majson = ChartSerializer.ConvertToMajson(CurrentSimaiFile, SelectedDifficulty);
                ChartSerializer.SaveMajdataJson(majson, _maidataDir);
                var jsonPath = Path.Combine(_maidataDir, "majdata.json");

                await _viewerConnection.StopPlaybackAsync();

                playStartTime = loopStart;
                TrackTime = loopStart;

                await _viewerConnection.StartPlaybackAsync(
                    jsonPath,
                    DateTime.Now,
                    (float)loopStart,
                    _editorSetting.playSpeed,
                    _editorSetting.touchSpeed,
                    1.0f,
                    _editorSetting.backgroundCover,
                    GetCenterDisplayIndicator(),
                    _editorSetting.SmoothSlideAnime,
                    GetSelectedPlayMethod());

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
        if (!_viewerConnection.IsViewerRunning)
        {
            if (!await _viewerConnection.LaunchViewerIfNeededAsync())
            {
                OnPropertyChanged(nameof(IsConnected));
                return false;
            }
        }

        OnPropertyChanged(nameof(IsConnected));
        return _viewerConnection.IsViewerRunning;
    }

    /// <param name="focusEditor">When false (e.g. follow-cursor during playback), only scrolls and moves the caret without focusing the editor so other controls stay clickable.</param>
    public void SeekToDocPos(Point position, TextEditor editor, bool focusEditor = true)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            // SeekToDocPos is typically called on the UI thread.
        }

        _isProgrammaticCaretUpdate = true;
        try
        {
            var offset = editor.Document.GetOffset((int)position.Y + 1, (int)position.X);
            editor.Select(offset, 0);
            editor.ScrollTo((int)position.Y + 1, (int)position.X);
            if (focusEditor)
                editor.TextArea.Focus();
        }
        finally
        {
            _isProgrammaticCaretUpdate = false;
        }
    }

    /// <summary>Whether the loop start point has been set.</summary>
    public bool LoopStartSet => _loopStartSet;

    /// <summary>Whether a loop region has been set (for button styling).</summary>
    public bool HasLoopRegion => _hasLoopRegion;

    /// <summary>Sets the loop start point to the current track time.</summary>
    [RelayCommand]
    public void SetLoopStart()
    {
        _pendingLoopStart = TrackTime;
        _loopStartSet = true;
        OnPropertyChanged(nameof(LoopStartSet));
    }

    /// <summary>Sets the loop end to the current track time and creates the loop region.</summary>
    [RelayCommand]
    public void SetLoopEnd()
    {
        if (_pendingLoopStart is null) return;

        var endTime = TrackTime;
        var startTime = _pendingLoopStart.Value;

        var chartStartTime = startTime - Offset;
        var chartEndTime = endTime - Offset;

        if (chartStartTime > chartEndTime)
            (chartStartTime, chartEndTime) = (chartEndTime, chartStartTime);

        var success = LoopViewModel?.TrySetRegion(chartStartTime, chartEndTime, CurrentSimaiChart) ?? false;
        if (success)
        {
            _hasLoopRegion = true;
            OnPropertyChanged(nameof(HasLoopRegion));
        }

        _pendingLoopStart = null;
        _loopStartSet = false;
        OnPropertyChanged(nameof(LoopStartSet));
    }
}
