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

                        var sorted = commaTimings.OrderBy(o => o.Timing).ThenBy(o => o.RawTextPosition).ToArray();
                        var idx = -1;
                        for (var i = 0; i < sorted.Length; i++)
                        {
                            if (sorted[i].Timing + Offset <= pollTime)
                                idx = i;
                            else
                                break;
                        }

                        if (idx < 0)
                            idx = 0;
                        var currentNote = sorted[idx];
                        var nextNote = idx + 1 < sorted.Length ? sorted[idx + 1] : null;

                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            ApplyFollowCursorNoteHighlight(_textEditor!, currentNote, nextNote, focusEditor: false);
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

    /// <summary>
    /// Selects the source text for the active timing point and scrolls it into view (Follow Cursor / highlight mode).
    /// Range is [current, next timing) with trailing commas/spaces trimmed so e.g. <c>{8}1,2,3</c> highlights <c>1</c> then <c>2</c>.
    /// Selection never crosses a newline so the next line's <c>{n}</c> prefix is not included when the next note is on another line.
    /// </summary>
    void ApplyFollowCursorNoteHighlight(TextEditor editor, MajSimai.SimaiTimingPoint current, MajSimai.SimaiTimingPoint? nextNote, bool focusEditor)
    {
        var text = editor.Text ?? string.Empty;
        var rawStart = GetSimaiTimingDocumentOffset(editor, current, text);
        // Parser may put RawTextPosition on a line break or on "{n}"; normalize so we highlight the tap token (e.g. "1").
        var startAfterBreaks = SkipLeadingLineBreaks(text, rawStart);
        var lineEndExclusive = GetExclusiveEndOfLineAfterOffset(text, startAfterBreaks);
        if (lineEndExclusive == startAfterBreaks && startAfterBreaks < text.Length &&
            (text[startAfterBreaks] == '\r' || text[startAfterBreaks] == '\n'))
        {
            startAfterBreaks = SkipLeadingLineBreaks(text, startAfterBreaks);
            lineEndExclusive = GetExclusiveEndOfLineAfterOffset(text, startAfterBreaks);
        }

        // Same normalization for current and next so exclusive end is the true next token start (nested {16}, commas, etc.).
        var start = TrimRawToNoteStart(text, rawStart, lineEndExclusive);

        int endExclusive;
        if (nextNote is { } n)
        {
            var nRaw = GetSimaiTimingDocumentOffset(editor, n, text);
            // Do not clip to the current line before normalizing the next offset, or the next line's index is past lineEnd.
            endExclusive = TrimRawToNoteStart(text, nRaw, int.MaxValue);
        }
        else
        {
            endExclusive = text.Length;
        }

        endExclusive = Math.Min(endExclusive, lineEndExclusive);
        endExclusive = TrimSeparatorsBeforeNextNote(text, start, endExclusive);
        TrimCommasSpacesAroundRange(text, ref start, ref endExclusive);
        // Parser sometimes normalizes the next note to the same index as the current; never expand to full line (that caused whole-line flash then shrink).
        if (endExclusive <= start)
        {
            endExclusive = ExclusiveEndAtNextCommaOrEol(text, start, lineEndExclusive);
            endExclusive = TrimSeparatorsBeforeNextNote(text, start, endExclusive);
            TrimCommasSpacesAroundRange(text, ref start, ref endExclusive);
        }

        var len = Math.Max(0, endExclusive - start);
        // Parser offset can sit on "}" at end of a line while the tap starts after CRLF on the next editor line.
        if (len == 0 && start == lineEndExclusive)
        {
            var ns = SkipLeadingLineBreaks(text, lineEndExclusive);
            if (ns > lineEndExclusive && ns < text.Length)
            {
                var startAfterBreaks2 = SkipLeadingLineBreaks(text, ns);
                lineEndExclusive = GetExclusiveEndOfLineAfterOffset(text, startAfterBreaks2);
                if (lineEndExclusive == startAfterBreaks2 && startAfterBreaks2 < text.Length &&
                    (text[startAfterBreaks2] == '\r' || text[startAfterBreaks2] == '\n'))
                {
                    startAfterBreaks2 = SkipLeadingLineBreaks(text, startAfterBreaks2);
                    lineEndExclusive = GetExclusiveEndOfLineAfterOffset(text, startAfterBreaks2);
                }

                start = TrimRawToNoteStart(text, ns, lineEndExclusive);
                if (nextNote is { } n2)
                {
                    var nRaw2 = GetSimaiTimingDocumentOffset(editor, n2, text);
                    endExclusive = TrimRawToNoteStart(text, nRaw2, int.MaxValue);
                }
                else
                {
                    endExclusive = text.Length;
                }

                endExclusive = Math.Min(endExclusive, lineEndExclusive);
                endExclusive = TrimSeparatorsBeforeNextNote(text, start, endExclusive);
                TrimCommasSpacesAroundRange(text, ref start, ref endExclusive);
                if (endExclusive <= start)
                {
                    endExclusive = ExclusiveEndAtNextCommaOrEol(text, start, lineEndExclusive);
                    endExclusive = TrimSeparatorsBeforeNextNote(text, start, endExclusive);
                    TrimCommasSpacesAroundRange(text, ref start, ref endExclusive);
                }

                len = Math.Max(0, endExclusive - start);
            }
        }

        _isProgrammaticCaretUpdate = true;
        try
        {
            editor.Select(start, len);
            var loc = editor.Document.GetLocation(start);
            editor.ScrollTo(loc.Line, loc.Column);
            if (focusEditor)
                editor.TextArea.Focus();
        }
        finally
        {
            _isProgrammaticCaretUpdate = false;
        }
    }

    /// <summary>
    /// Maps a raw parser offset to the first character of the note token on this line (measure prefix, nested measures, commas).
    /// <paramref name="lineEndExclusive"/> caps the line (caller uses the current row's end when clipping the selection).
    /// </summary>
    static int TrimRawToNoteStart(string text, int rawStart, int lineEndExclusive)
    {
        var afterBreaks = SkipLeadingLineBreaks(text, rawStart);
        var lineEnd = Math.Min(GetExclusiveEndOfLineAfterOffset(text, afterBreaks), lineEndExclusive);
        var s = SkipLeadingMeasurePrefix(text, afterBreaks, lineEnd);
        while (s < lineEnd && text[s] == '{')
            s = SkipLeadingMeasurePrefix(text, s, lineEnd);
        if (s < lineEnd && text[s] == '}')
        {
            s++;
            s = TrimLeadingCommasSpaces(text, s, lineEnd);
        }

        return TrimLeadingCommasSpaces(text, s, lineEnd);
    }

    /// <summary>Exclusive end of the token on this line: stops before the first comma or line break (does not cross lines).</summary>
    static int ExclusiveEndAtNextCommaOrEol(string text, int start, int lineEndExclusive)
    {
        for (var i = start; i < lineEndExclusive; i++)
        {
            var c = text[i];
            if (c == ',' || c == '\r' || c == '\n')
                return i;
        }

        return lineEndExclusive;
    }

    static int GetSimaiTimingDocumentOffset(TextEditor editor, MajSimai.SimaiTimingPoint note, string text)
    {
        var n = text.Length;
        // Prefer document line/column so indices match the editor (CRLF); linear can land on "}" at EOL while the tap is on the next line.
        try
        {
            var ly = (int)note.RawTextPositionY;
            var lx = (int)note.RawTextPositionX;
            if (ly >= 0 && lx >= 0)
            {
                var o = editor.Document.GetOffset(ly + 1, lx);
                if (o >= 0 && o <= n)
                    return o;
            }
        }
        catch
        {
            /* fall back */
        }

        var linear = note.RawTextPosition;
        if (linear >= 0 && linear <= n &&
            (linear >= n || (text[linear] != '\r' && text[linear] != '\n')))
            return linear;

        var start = linear;
        if (start < 0 || start > n)
        {
            try
            {
                start = editor.Document.GetOffset((int)note.RawTextPositionY + 1, (int)note.RawTextPositionX);
            }
            catch
            {
                start = 0;
            }
        }

        if (start < 0)
            start = 0;
        if (start > n)
            start = n;
        if (start < n && (text[start] == '\r' || text[start] == '\n'))
            start = SkipLeadingLineBreaks(text, start);
        return start;
    }

    /// <summary>First index at or after <paramref name="start"/> that is not part of the same line (exclusive end of line content).</summary>
    static int GetExclusiveEndOfLineAfterOffset(string text, int start)
    {
        if (string.IsNullOrEmpty(text) || start < 0)
            return 0;
        if (start >= text.Length)
            return text.Length;
        for (var i = start; i < text.Length; i++)
        {
            if (text[i] == '\r' || text[i] == '\n')
                return i;
        }

        return text.Length;
    }

    static int SkipLeadingLineBreaks(string text, int offset)
    {
        var n = text.Length;
        while (offset < n && (text[offset] == '\r' || text[offset] == '\n'))
            offset++;
        return offset;
    }

    /// <summary>
    /// When the parser points at the start of a measure (<c>{4}</c>), advance to the first character after the closing <c>}</c>
    /// so the highlight covers taps like <c>1</c>, not the prefix.
    /// </summary>
    static int SkipLeadingMeasurePrefix(string text, int start, int lineEndExclusive)
    {
        if (start >= lineEndExclusive || text[start] != '{')
            return start;
        var i = start + 1;
        while (i < lineEndExclusive && char.IsDigit(text[i]))
            i++;
        if (i < lineEndExclusive && text[i] == '}')
        {
            var after = i + 1;
            while (after < lineEndExclusive && (text[after] == ' ' || text[after] == '\t'))
                after++;
            return after;
        }

        return start;
    }

    static int TrimLeadingCommasSpaces(string text, int start, int maxExclusive)
    {
        while (start < maxExclusive && start < text.Length && (text[start] == ',' || text[start] == ' ' || text[start] == '\t'))
            start++;
        return start;
    }

    static void TrimCommasSpacesAroundRange(string text, ref int start, ref int endExclusive)
    {
        var n = text.Length;
        if (endExclusive > n)
            endExclusive = n;
        while (start < endExclusive && (text[start] == ',' || text[start] == ' ' || text[start] == '\t'))
            start++;
        while (endExclusive > start && (text[endExclusive - 1] == ',' || text[endExclusive - 1] == ' ' || text[endExclusive - 1] == '\t'))
            endExclusive--;
    }

    static int TrimSeparatorsBeforeNextNote(string text, int start, int endExclusive)
    {
        var n = text.Length;
        if (endExclusive > n)
            endExclusive = n;
        while (endExclusive > start)
        {
            var c = text[endExclusive - 1];
            // Do not trim newlines here; line bounds are handled separately.
            if (c == ',' || c == ' ' || c == '\t')
            {
                endExclusive--;
                continue;
            }

            break;
        }

        return endExclusive;
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
