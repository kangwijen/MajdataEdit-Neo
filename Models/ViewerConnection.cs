using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Threading;
using MajdataEdit_Neo.Utils;
using Newtonsoft.Json;

namespace MajdataEdit_Neo.Models;

internal class ViewerConnection
{
    private static readonly HttpClient _httpClient = new();
    private const string VIEWER_URL = "http://localhost:8013/";
    private static DateTime _lastErrorPopupTime = DateTime.MinValue;
    private const int ERROR_POPUP_COOLDOWN_SECONDS = 5; // Only show error popup once every 5 seconds
    private bool _lastConnectionCheckResult = false;
    private DateTime _lastConnectionCheckTime = DateTime.MinValue;
    private const int CONNECTION_CHECK_CACHE_SECONDS = 2; // Cache connection check results for 2 seconds
    private DateTime _lastLaunchAttemptTime = DateTime.MinValue;
    private const int LAUNCH_ATTEMPT_COOLDOWN_SECONDS = 10; // Don't try to launch more than once every 10 seconds

    public bool IsViewerRunning => CheckViewerRunning();

    public async Task<bool> SendControlCommandAsync(EditRequestjson request)
    {
        // Quick check: if we recently determined MajdataView isn't available, don't bother trying
        if ((DateTime.Now - _lastConnectionCheckTime).TotalSeconds < CONNECTION_CHECK_CACHE_SECONDS &&
            !_lastConnectionCheckResult)
        {
            return false; // Don't show popup for cached failures
        }

        try
        {
            var json = JsonConvert.SerializeObject(request);
            var content = new StringContent(json, Encoding.UTF8);

            var response = await _httpClient.PostAsync(VIEWER_URL, content);
            var responseString = await response.Content.ReadAsStringAsync();

            if (responseString == "ERROR")
            {
                _lastConnectionCheckResult = false;
                _lastConnectionCheckTime = DateTime.Now;
                await ShowErrorPopupIfAllowed();
                return false;
            }

            _lastConnectionCheckResult = true;
            _lastConnectionCheckTime = DateTime.Now;
            return true;
        }
        catch (Exception)
        {
            _lastConnectionCheckResult = false;
            _lastConnectionCheckTime = DateTime.Now;
            await ShowErrorPopupIfAllowed();
            return false;
        }
    }

    private async Task ShowErrorPopupIfAllowed()
    {
        // Only show error popup if enough time has passed since the last one
        if ((DateTime.Now - _lastErrorPopupTime).TotalSeconds >= ERROR_POPUP_COOLDOWN_SECONDS)
        {
            _lastErrorPopupTime = DateTime.Now;
            await Dispatcher.UIThread.Invoke(async () => {
                await MessageBox.ShowAsync("Please make sure MajdataView is open and port 8013 is available", "Connection Error");
            });
        }
    }

    public async Task<bool> StartPlaybackAsync(string jsonPath, DateTime startAt, float startTime,
        float noteSpeed, float touchSpeed, float audioSpeed, float backgroundCover,
        EditorComboIndicator comboStatusType, bool smoothSlideAnime, EditorPlayMethod editorPlayMethod)
    {
        var request = new EditRequestjson
        {
            control = EditorControlMethod.Start,
            jsonPath = jsonPath,
            startAt = startAt.Ticks,
            startTime = startTime,
            noteSpeed = noteSpeed,
            touchSpeed = touchSpeed,
            audioSpeed = audioSpeed,
            backgroundCover = backgroundCover,
            comboStatusType = comboStatusType,
            smoothSlideAnime = smoothSlideAnime,
            editorPlayMethod = editorPlayMethod
        };

        return await SendControlCommandAsync(request);
    }

    public async Task<bool> StartOpPlaybackAsync(string jsonPath, DateTime startAt, float startTime,
        float noteSpeed, float touchSpeed, float audioSpeed, float backgroundCover,
        EditorComboIndicator comboStatusType, bool smoothSlideAnime, EditorPlayMethod editorPlayMethod)
    {
        var request = new EditRequestjson
        {
            control = EditorControlMethod.OpStart,
            jsonPath = jsonPath,
            startAt = startAt.Ticks,
            startTime = startTime,
            noteSpeed = noteSpeed,
            touchSpeed = touchSpeed,
            audioSpeed = audioSpeed,
            backgroundCover = backgroundCover,
            comboStatusType = comboStatusType,
            smoothSlideAnime = smoothSlideAnime,
            editorPlayMethod = editorPlayMethod
        };

        return await SendControlCommandAsync(request);
    }

    public async Task<bool> StartRecordingAsync(string jsonPath, DateTime startAt, float startTime,
        float noteSpeed, float touchSpeed, float audioSpeed, float backgroundCover,
        EditorComboIndicator comboStatusType, bool smoothSlideAnime, EditorPlayMethod editorPlayMethod)
    {
        var request = new EditRequestjson
        {
            control = EditorControlMethod.Record,
            jsonPath = jsonPath,
            startAt = startAt.Ticks,
            startTime = startTime,
            noteSpeed = noteSpeed,
            touchSpeed = touchSpeed,
            audioSpeed = audioSpeed,
            backgroundCover = backgroundCover,
            comboStatusType = comboStatusType,
            smoothSlideAnime = smoothSlideAnime,
            editorPlayMethod = editorPlayMethod
        };

        return await SendControlCommandAsync(request);
    }

    public async Task<bool> PausePlaybackAsync()
    {
        var request = new EditRequestjson
        {
            control = EditorControlMethod.Pause
        };

        return await SendControlCommandAsync(request);
    }

    public async Task<bool> ContinuePlaybackAsync(DateTime startAt, float startTime, float audioSpeed)
    {
        var request = new EditRequestjson
        {
            control = EditorControlMethod.Continue,
            startAt = startAt.Ticks,
            startTime = startTime,
            audioSpeed = audioSpeed
        };

        return await SendControlCommandAsync(request);
    }

    public async Task<bool> StopPlaybackAsync()
    {
        var request = new EditRequestjson
        {
            control = EditorControlMethod.Stop
        };

        return await SendControlCommandAsync(request);
    }

    private bool CheckViewerRunning()
    {
        var processes = Process.GetProcessesByName("MajdataView");
        var unityProcesses = Process.GetProcessesByName("Unity");
        return processes.Length > 0 || unityProcesses.Length > 0;
    }

    public bool LaunchViewerIfNeeded()
    {
        if (!CheckViewerRunning())
        {
            // Don't try to launch if we attempted recently
            if ((DateTime.Now - _lastLaunchAttemptTime).TotalSeconds < LAUNCH_ATTEMPT_COOLDOWN_SECONDS)
            {
                return false;
            }

            _lastLaunchAttemptTime = DateTime.Now;

            try
            {
                // Check if MajdataView.exe exists before trying to start it
                if (!System.IO.File.Exists("MajdataView.exe"))
                {
                    return false;
                }

                var process = Process.Start("MajdataView.exe");
                if (process != null)
                {
                    // Wait a bit for the process to start
                    Task.Delay(2000).Wait();
                    return true;
                }
            }
            catch (Exception)
            {
                // Silently fail if MajdataView.exe cannot be started
            }
            return false;
        }
        return true;
    }
}