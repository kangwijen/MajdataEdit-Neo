using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using ManagedBass;
using MajdataEdit_Neo.Models;

namespace MajdataEdit_Neo.Models;

class AudioManager : IDisposable
{
    [DllImport("winmm")]
    private static extern void timeBeginPeriod(int t);

    [DllImport("winmm")]
    private static extern void timeEndPeriod(int t);
    private static AudioManager? _instance;
    private readonly string sfxPath;
    private readonly SynchronizationContext? uiContext;

    // BGM Stream
    private int bgmStream = 0;

    // SFX Streams
    private int answerStream = 0;
    private int judgeStream = 0;
    private int judgeBreakStream = 0;
    private int judgeExStream = 0;
    private int breakStream = 0;
    private int hanabiStream = 0;
    private int holdRiserStream = 0;
    private int trackStartStream = 0;
    private int slideStream = 0;
    private int touchStream = 0;
    private int allperfectStream = 0;
    private int fanfareStream = 0;
    private int clockStream = 0;
    private int breakSlideStartStream = 0;
    private int breakSlideStream = 0;
    private int judgeBreakSlideStream = 0;

    // SFX Timing
    private List<SoundEffectTiming> sfxTimings = new();
    private Thread? sfxThread;
    private bool isSfxLoopRunning = false;
    private double sfxOffset = 0; // Offset to add to SFX timing (from majson.first)

    // Volume levels (0.0 to 1.0)
    public float BgmLevel { get; set; } = 0.7f;
    public float AnswerLevel { get; set; } = 0.7f;
    public float JudgeLevel { get; set; } = 0.7f;
    public float BreakLevel { get; set; } = 0.7f;
    public float BreakSlideLevel { get; set; } = 0.7f;
    public float SlideLevel { get; set; } = 0.7f;
    public float ExLevel { get; set; } = 0.7f;
    public float TouchLevel { get; set; } = 0.7f;
    public float HanabiLevel { get; set; } = 0.7f;

    // SFX timing compensation (in seconds)
    public double SfxLatencyCompensation { get; set; } = 0.0545;

    public AudioManager(string sfxDirectoryPath)
    {
        if (_instance != null)
        {
            Console.WriteLine("AudioManager: Instance already exists, disposing old instance");
            _instance.Dispose();
        }

        _instance = this;
        sfxPath = Path.GetFullPath(sfxDirectoryPath);
        uiContext = SynchronizationContext.Current;

        // Initialize BASS
        try
        {
            if (!Bass.Init(-1, 44100, DeviceInitFlags.Default))
            {
                // Check if BASS is already initialized by trying to get device info
                Bass.GetInfo(out var info);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Audio initialization failed: {ex.Message}");
            throw;
        }

        // Set high precision timing for SFX
        Bass.Configure(Configuration.DeviceBufferLength, 10);
        Bass.Configure(Configuration.UpdatePeriod, 5);
    }

    public void Dispose()
    {
        StopSfxLoop();
        FreeAllStreams();
        // Only free BASS if this is the current instance
        if (_instance == this)
        {
            Bass.Free();
            _instance = null;
        }
    }

    #region BGM Methods

    public bool LoadBgm(string filePath)
    {
        try
        {
            // Free existing stream
            if (bgmStream != 0)
            {
                Bass.StreamFree(bgmStream);
                bgmStream = 0;
            }

            if (!File.Exists(filePath))
                return false;

            // Create stream with tempo support (if BASS_FX is available)
            // For now, use basic stream
            bgmStream = Bass.CreateStream(filePath, 0, 0, BassFlags.Prescan | BassFlags.Float);

            if (bgmStream == 0)
                return false;

            // Set initial volume
            Bass.ChannelSetAttribute(bgmStream, ChannelAttribute.Volume, BgmLevel);

            return true;
        }
        catch
        {
            return false;
        }
    }

    public void PlayBgm(double startTime = 0)
    {
        if (bgmStream == 0) return;

        Bass.ChannelSetPosition(bgmStream, Bass.ChannelSeconds2Bytes(bgmStream, startTime));
        Bass.ChannelPlay(bgmStream, false);
    }

    public void PauseBgm()
    {
        if (bgmStream == 0) return;
        Bass.ChannelPause(bgmStream);
    }

    public void StopBgm()
    {
        if (bgmStream == 0) return;
        Bass.ChannelStop(bgmStream);
    }

    public void SetBgmPosition(double time)
    {
        if (bgmStream == 0) return;
        Bass.ChannelSetPosition(bgmStream, Bass.ChannelSeconds2Bytes(bgmStream, time));
    }

    public double GetBgmPosition()
    {
        if (bgmStream == 0) return 0;
        return Bass.ChannelBytes2Seconds(bgmStream, Bass.ChannelGetPosition(bgmStream));
    }

    public bool IsBgmPlaying()
    {
        if (bgmStream == 0) return false;
        return Bass.ChannelIsActive(bgmStream) == PlaybackState.Playing;
    }

    public double GetBgmLength()
    {
        if (bgmStream == 0) return 0;
        return Bass.ChannelBytes2Seconds(bgmStream, Bass.ChannelGetLength(bgmStream));
    }

    #endregion

    #region SFX Methods

    public bool LoadSfx()
    {
        try
        {
            FreeSfxStreams();

            // Load all SFX files
            answerStream = LoadSfxFile("answer.wav");
            judgeStream = LoadSfxFile("judge.wav");
            judgeBreakStream = LoadSfxFile("judge_break.wav");
            judgeExStream = LoadSfxFile("judge_ex.wav");
            breakStream = LoadSfxFile("break.wav");
            hanabiStream = LoadSfxFile("hanabi.wav");
            holdRiserStream = LoadSfxFile("touchHold_riser.wav");
            trackStartStream = LoadSfxFile("track_start.wav");
            slideStream = LoadSfxFile("slide.wav");
            touchStream = LoadSfxFile("touch.wav");
            allperfectStream = LoadSfxFile("all_perfect.wav");
            fanfareStream = LoadSfxFile("fanfare.wav");
            clockStream = LoadSfxFile("clock.wav");
            breakSlideStartStream = LoadSfxFile("break_slide_start.wav");
            breakSlideStream = LoadSfxFile("break_slide.wav");
            judgeBreakSlideStream = LoadSfxFile("judge_break_slide.wav");

            // Set initial volumes
            UpdateAllVolumes();

            return true;
        }
        catch
        {
            FreeSfxStreams();
            return false;
        }
    }

    private int LoadSfxFile(string fileName)
    {
        var filePath = Path.Combine(sfxPath, fileName);
        if (!File.Exists(filePath))
            return 0;

        return Bass.CreateStream(filePath, 0, 0, BassFlags.Float);
    }

    public void SetSfxOffset(double offset)
    {
        sfxOffset = offset;
    }

    public void GenerateSfxTimings(List<SimaiTimingPoint> timingPoints, double startTime = 0)
    {
        sfxTimings.Clear();

        for (var i = 0; i < timingPoints.Count; i++)
        {
            var timingPoint = timingPoints[i];
            if (timingPoint.time < startTime) continue; // Skip notes before start time

            SoundEffectTiming stObj;

            // If a timing at this exact time already exists, use it
            var combIndex = sfxTimings.FindIndex(o => Math.Abs(o.Time - timingPoint.time) < 0.001f);
            if (combIndex != -1)
                stObj = sfxTimings[combIndex];
            else
                stObj = new SoundEffectTiming(timingPoint.time);

            stObj.NoteGroupIndex = i;

            foreach (var note in timingPoint.noteList)
            {
                switch (note.noteType)
                {
                    case MajsonNoteType.Tap:
                        stObj.HasAnswer = true;
                        if (note.isBreak)
                        {
                            stObj.HasBreak = true;
                            stObj.HasJudgeBreak = true;
                        }
                        if (note.isEx)
                            stObj.HasJudgeEx = true;
                        if (!note.isBreak && !note.isEx)
                            stObj.HasJudge = true;
                        break;

                    case MajsonNoteType.Hold:
                        stObj.HasAnswer = true;
                        if (note.isBreak)
                        {
                            stObj.HasBreak = true;
                            stObj.HasJudgeBreak = true;
                        }
                        if (note.isEx)
                            stObj.HasJudgeEx = true;
                        if (!note.isBreak && !note.isEx)
                            stObj.HasJudge = true;

                        // Hold tail sound
                        if (note.holdTime > 0.00f)
                        {
                            var targetTime = timingPoint.time + note.holdTime;
                            var nearIndex = sfxTimings.FindIndex(o => Math.Abs(o.Time - targetTime) < 0.001f);
                            if (nearIndex != -1)
                            {
                                sfxTimings[nearIndex].HasAnswer = true;
                                if (!note.isBreak && !note.isEx)
                                    sfxTimings[nearIndex].HasJudge = true;
                            }
                            else
                            {
                                var holdRelease = new SoundEffectTiming(targetTime, true, !note.isBreak && !note.isEx);
                                sfxTimings.Add(holdRelease);
                            }
                        }
                        break;

                    case MajsonNoteType.Slide:
                        if (!note.isSlideNoHead)
                        {
                            stObj.HasAnswer = true;
                            if (note.isBreak)
                            {
                                stObj.HasBreak = true;
                                stObj.HasJudgeBreak = true;
                            }
                            if (note.isEx)
                                stObj.HasJudgeEx = true;
                            if (!note.isBreak && !note.isEx)
                                stObj.HasJudge = true;
                        }

                        // Slide start sound
                        var slideTargetTime = note.slideStartTime;
                        var slideNearIndex = sfxTimings.FindIndex(o => Math.Abs(o.Time - slideTargetTime) < 0.001f);
                        if (slideNearIndex != -1)
                        {
                            if (note.isSlideBreak)
                                sfxTimings[slideNearIndex].HasBreakSlideStart = true;
                            else
                                sfxTimings[slideNearIndex].HasSlide = true;
                        }
                        else
                        {
                            SoundEffectTiming slide;
                            if (note.isSlideBreak)
                                slide = new SoundEffectTiming(slideTargetTime, hasBreakSlideStart: true);
                            else
                                slide = new SoundEffectTiming(slideTargetTime, hasSlide: true);
                            sfxTimings.Add(slide);
                        }

                        // Slide tail (break slide only)
                        if (note.isSlideBreak)
                        {
                            var slideTailTargetTime = note.slideStartTime + note.slideTime;
                            var slideTailNearIndex = sfxTimings.FindIndex(o => Math.Abs(o.Time - slideTailTargetTime) < 0.001f);
                            if (slideTailNearIndex != -1)
                            {
                                sfxTimings[slideTailNearIndex].HasBreakSlide = true;
                                sfxTimings[slideTailNearIndex].HasJudgeBreakSlide = true;
                            }
                            else
                            {
                                var slide = new SoundEffectTiming(slideTailTargetTime, hasBreakSlide: true,
                                    hasJudgeBreakSlide: true);
                                sfxTimings.Add(slide);
                            }
                        }
                        break;

                    case MajsonNoteType.Touch:
                        stObj.HasAnswer = true;
                        stObj.HasTouch = true;
                        if (note.isHanabi)
                            stObj.HasHanabi = true;
                        break;

                    case MajsonNoteType.TouchHold:
                        stObj.HasAnswer = true;
                        stObj.HasTouch = true;
                        stObj.HasTouchHold = true;

                        // Touch hold ending
                        var touchHoldTargetTime = timingPoint.time + note.holdTime;
                        var touchHoldNearIndex = sfxTimings.FindIndex(o => Math.Abs(o.Time - touchHoldTargetTime) < 0.001f);
                        if (touchHoldNearIndex != -1)
                        {
                            if (note.isHanabi)
                                sfxTimings[touchHoldNearIndex].HasHanabi = true;
                            sfxTimings[touchHoldNearIndex].HasAnswer = true;
                            sfxTimings[touchHoldNearIndex].HasTouchHoldEnd = true;
                        }
                        else
                        {
                            var tHoldRelease = new SoundEffectTiming(touchHoldTargetTime, true,
                                hasHanabi: note.isHanabi, hasTouchHoldEnd: true);
                            sfxTimings.Add(tHoldRelease);
                        }
                        break;
                }
            }

            if (combIndex != -1)
                sfxTimings[combIndex] = stObj;
            else
                sfxTimings.Add(stObj);
        }

        // Sort by time
        sfxTimings = sfxTimings.OrderBy(t => t.Time).ToList();

    }

    public void StartSfxLoop()
    {
        if (isSfxLoopRunning) return;

        isSfxLoopRunning = true;

        sfxThread = new Thread(SfxLoop)
        {
            Priority = ThreadPriority.Highest,
            IsBackground = true
        };
        sfxThread.Start();
    }

    public void StopSfxLoop()
    {
        isSfxLoopRunning = false;

        if (sfxThread != null && sfxThread.IsAlive)
        {
            sfxThread.Join(100); // Wait up to 100ms
        }

        sfxThread = null;

        // Stop any playing SFX
        StopAllSfx();
    }

    private void SfxLoop()
    {
        timeBeginPeriod(1); // Set Windows timer to 1ms precision
        try
        {
            while (isSfxLoopRunning)
            {
                try
                {
                    if (!IsBgmPlaying())
                    {
                        Thread.Sleep(10);
                        continue;
                    }

                    var currentTime = GetBgmPosition();

                    // Process SFX timings - offset is now baked into note times
                    // Note: timing.Time already includes offset, no need to add sfxOffset
                    if (sfxTimings.Count > 0)
                    {
                        var timing = sfxTimings[0];
                        var lag = timing.Time - currentTime;

                        // Don't touch this!!!!! this related to delay (from old code)
                        if (lag <= SfxLatencyCompensation)
                        {
                            PlaySfxForTiming(timing);
                            sfxTimings.RemoveAt(0);
                        }
                    }

                    Thread.Sleep(1); // ~1ms loop
                }
                catch
                {
                    Thread.Sleep(10);
                }
            }
        }
        finally
        {
            timeEndPeriod(1); // Restore Windows timer
        }
    }

    private void PlaySfxForTiming(SoundEffectTiming timing)
    {
        // Play all applicable sounds at once (like the old implementation)
        if (timing.HasAnswer)
            PlaySfx(answerStream);
        if (timing.HasJudge)
            PlaySfx(judgeStream);
        if (timing.HasJudgeBreak)
            PlaySfx(judgeBreakStream);
        if (timing.HasJudgeEx)
            PlaySfx(judgeExStream);
        if (timing.HasBreak)
            PlaySfx(breakStream);
        if (timing.HasTouch)
            PlaySfx(touchStream);
        if (timing.HasHanabi)
            PlaySfx(hanabiStream);
        if (timing.HasTouchHold)
            PlaySfx(holdRiserStream);
        if (timing.HasTouchHoldEnd)
            Bass.ChannelStop(holdRiserStream);
        if (timing.HasSlide)
            PlaySfx(slideStream);
        if (timing.HasBreakSlideStart)
            PlaySfx(breakSlideStartStream);
        if (timing.HasBreakSlide)
            PlaySfx(breakSlideStream);
        if (timing.HasJudgeBreakSlide)
            PlaySfx(judgeBreakSlideStream);
        if (timing.HasAllPerfect)
        {
            PlaySfx(allperfectStream);
            PlaySfx(fanfareStream);
        }
        if (timing.HasClock)
            PlaySfx(clockStream);
    }

    private void PlaySfx(int stream)
    {
        if (stream != 0)
        {
            Bass.ChannelPlay(stream, true); // Restart if already playing
        }
    }

    private void StopAllSfx()
    {
        var streams = new[] {
            answerStream, judgeStream, judgeBreakStream, judgeExStream,
            breakStream, hanabiStream, holdRiserStream, trackStartStream,
            slideStream, touchStream, allperfectStream, fanfareStream,
            clockStream, breakSlideStartStream, breakSlideStream, judgeBreakSlideStream
        };

        foreach (var stream in streams)
        {
            if (stream != 0)
            {
                Bass.ChannelStop(stream);
            }
        }
    }

    #endregion

    #region Volume Control

    public void UpdateAllVolumes()
    {
        if (bgmStream != 0) Bass.ChannelSetAttribute(bgmStream, ChannelAttribute.Volume, BgmLevel);
        if (answerStream != 0) Bass.ChannelSetAttribute(answerStream, ChannelAttribute.Volume, AnswerLevel);
        if (judgeStream != 0) Bass.ChannelSetAttribute(judgeStream, ChannelAttribute.Volume, JudgeLevel);
        if (judgeBreakStream != 0) Bass.ChannelSetAttribute(judgeBreakStream, ChannelAttribute.Volume, BreakLevel);
        if (judgeExStream != 0) Bass.ChannelSetAttribute(judgeExStream, ChannelAttribute.Volume, ExLevel);
        if (breakStream != 0) Bass.ChannelSetAttribute(breakStream, ChannelAttribute.Volume, BreakLevel);
        if (breakSlideStream != 0) Bass.ChannelSetAttribute(breakSlideStream, ChannelAttribute.Volume, BreakSlideLevel);
        if (slideStream != 0) Bass.ChannelSetAttribute(slideStream, ChannelAttribute.Volume, SlideLevel);
        if (touchStream != 0) Bass.ChannelSetAttribute(touchStream, ChannelAttribute.Volume, TouchLevel);
        if (hanabiStream != 0) Bass.ChannelSetAttribute(hanabiStream, ChannelAttribute.Volume, HanabiLevel);
        if (holdRiserStream != 0) Bass.ChannelSetAttribute(holdRiserStream, ChannelAttribute.Volume, HanabiLevel);
        if (trackStartStream != 0) Bass.ChannelSetAttribute(trackStartStream, ChannelAttribute.Volume, BgmLevel);
        if (allperfectStream != 0) Bass.ChannelSetAttribute(allperfectStream, ChannelAttribute.Volume, BgmLevel);
        if (fanfareStream != 0) Bass.ChannelSetAttribute(fanfareStream, ChannelAttribute.Volume, BgmLevel);
        if (clockStream != 0) Bass.ChannelSetAttribute(clockStream, ChannelAttribute.Volume, BgmLevel);
        if (breakSlideStartStream != 0) Bass.ChannelSetAttribute(breakSlideStartStream, ChannelAttribute.Volume, SlideLevel);
        if (judgeBreakSlideStream != 0) Bass.ChannelSetAttribute(judgeBreakSlideStream, ChannelAttribute.Volume, BreakSlideLevel);
    }

    #endregion

    #region Helper Methods

    private void FreeAllStreams()
    {
        FreeBgmStream();
        FreeSfxStreams();
    }

    private void FreeBgmStream()
    {
        if (bgmStream != 0)
        {
            Bass.StreamFree(bgmStream);
            bgmStream = 0;
        }
    }

    private void FreeSfxStreams()
    {
        var streams = new[] {
            answerStream, judgeStream, judgeBreakStream, judgeExStream,
            breakStream, hanabiStream, holdRiserStream, trackStartStream,
            slideStream, touchStream, allperfectStream, fanfareStream,
            clockStream, breakSlideStartStream, breakSlideStream, judgeBreakSlideStream
        };

        foreach (var stream in streams)
        {
            if (stream != 0)
            {
                Bass.StreamFree(stream);
            }
        }

        // Reset all stream handles
        answerStream = judgeStream = judgeBreakStream = judgeExStream = 0;
        breakStream = hanabiStream = holdRiserStream = trackStartStream = 0;
        slideStream = touchStream = allperfectStream = fanfareStream = 0;
        clockStream = breakSlideStartStream = breakSlideStream = judgeBreakSlideStream = 0;
    }

    #endregion
}

class SoundEffectTiming
{
    public double Time { get; set; }
    public int NoteGroupIndex { get; set; } = -1;

    // Multiple SFX flags per timing (like the old implementation)
    public bool HasAnswer { get; set; }
    public bool HasJudge { get; set; }
    public bool HasJudgeBreak { get; set; }
    public bool HasJudgeEx { get; set; }
    public bool HasBreak { get; set; }
    public bool HasTouch { get; set; }
    public bool HasHanabi { get; set; }
    public bool HasTouchHold { get; set; }
    public bool HasTouchHoldEnd { get; set; }
    public bool HasSlide { get; set; }
    public bool HasBreakSlideStart { get; set; }
    public bool HasBreakSlide { get; set; }
    public bool HasJudgeBreakSlide { get; set; }
    public bool HasClock { get; set; }
    public bool HasAllPerfect { get; set; }

    public SoundEffectTiming(double time, bool hasAnswer = false, bool hasJudge = false,
        bool hasJudgeBreak = false, bool hasBreak = false, bool hasTouch = false,
        bool hasHanabi = false, bool hasJudgeEx = false, bool hasTouchHold = false,
        bool hasSlide = false, bool hasTouchHoldEnd = false, bool hasAllPerfect = false,
        bool hasClock = false, bool hasBreakSlideStart = false, bool hasBreakSlide = false,
        bool hasJudgeBreakSlide = false)
    {
        Time = time;
        HasAnswer = hasAnswer;
        HasJudge = hasJudge;
        HasJudgeBreak = hasJudgeBreak;
        HasBreak = hasBreak;
        HasTouch = hasTouch;
        HasHanabi = hasHanabi;
        HasJudgeEx = hasJudgeEx;
        HasTouchHold = hasTouchHold;
        HasSlide = hasSlide;
        HasTouchHoldEnd = hasTouchHoldEnd;
        HasAllPerfect = hasAllPerfect;
        HasClock = hasClock;
        HasBreakSlideStart = hasBreakSlideStart;
        HasBreakSlide = hasBreakSlide;
        HasJudgeBreakSlide = hasJudgeBreakSlide;
    }
}