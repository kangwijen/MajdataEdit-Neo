using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using AvaloniaEdit;
using AvaloniaEdit.Editing;
using AvaloniaEdit.TextMate;
using MajdataEdit_Neo.Controls;
using MajdataEdit_Neo.Models;
using MajdataEdit_Neo.Utils;
using MajdataEdit_Neo.ViewModels;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using TextMateSharp.Grammars;
using TextMateSharp.Registry;

namespace MajdataEdit_Neo.Views;

public partial class MainWindow : Window
{
    MainWindowViewModel? viewModel => (MainWindowViewModel?)DataContext;
    TextEditor? textEditor;
    SimaiVisualizerControl? simaiVisual;
    LoopVisualizerOverlay? loopOverlay;

    public MainWindow()
    {
        InitializeComponent();
        //setup editor
        textEditor = this.FindControl<TextEditor>("Editor");
        if (textEditor != null)
        {
            textEditor.TextChanged += TextEditor_TextChanged;
            textEditor.TextArea.Caret.PositionChanged += Caret_PositionChanged;
            textEditor.Options.HighlightCurrentLine = true;
            textEditor.Options.EnableTextDragDrop = true;
            var _registryOptions = new RegistryOptions(ThemeName.DarkPlus);
            var _install = TextMate.InstallTextMate(textEditor, _registryOptions);
            var registry = new Registry(_install.RegistryOptions);
            _install.SetGrammarFile(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "simai.tmLanguage.json"));
        }
        //setup visualizer
        simaiVisual = this.FindControl<SimaiVisualizerControl>("SimaiVisual");
        if (simaiVisual != null)
        {
            simaiVisual.PointerWheelChanged += SimaiVisual_PointerWheelChanged;
            simaiVisual.PointerMoved += SimaiVisual_PointerMoved;
        }
        //setup loop overlay
        loopOverlay = this.FindControl<LoopVisualizerOverlay>("LoopOverlay");
        if (loopOverlay != null)
        {
            loopOverlay.RegionSelected += LoopOverlay_RegionSelected;
        }
        //zoom buttons
        var zoomIn = this.FindControl<Button>("ZoomIn");
        if (zoomIn != null) zoomIn.Click += ZoomIn_Click;
        var zoomOut = this.FindControl<Button>("ZoomOut");
        if (zoomOut != null) zoomOut.Click += ZoomOut_Click;
        //this window
        this.KeyDown += MainWindow_KeyDown;
        this.KeyUp += MainWindow_KeyUp;
        this.LostFocus += MainWindow_LostFocus;
        this.Closing += MainWindow_Closing;
        this.Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object? sender, RoutedEventArgs e)
    {
        if (viewModel != null)
        {
            await viewModel.ConnectToPlayerAsync();
            // Subscribe to loop region changes for button styling
            viewModel.PropertyChanged += (s, args) =>
            {
                if (args.PropertyName == nameof(viewModel.HasLoopRegion))
                {
                    UpdateLoopButtonColors();
                }
                if (args.PropertyName == nameof(viewModel.LoopStartSet))
                {
                    UpdateLoopButtonColors();
                }
            };
        }

        ApplyEditorHotKeysFromViewModel();
    }

    /// <summary>
    /// Assigns <see cref="Button.HotKey"/> from JSON strings. XAML cannot bind string to KeyGesture; use <see cref="KeyGesture.Parse(string)"/>.
    /// </summary>
    private void ApplyEditorHotKeysFromViewModel()
    {
        if (viewModel == null) return;

        TryApplyHotKey(this.FindControl<Button>("BtnSendViewer"), viewModel.SendViewerKey);
        TryApplyHotKey(this.FindControl<Button>("BtnPlayPause"), viewModel.PlayPauseKey);
        TryApplyHotKey(this.FindControl<Button>("BtnDecSpeed"), viewModel.DecreasePlaybackSpeedKey);
        TryApplyHotKey(this.FindControl<Button>("BtnIncSpeed"), viewModel.IncreasePlaybackSpeedKey);
        TryApplyHotKey(this.FindControl<Button>("BtnMirrorLR"), viewModel.MirrorLeftRightKey);
        TryApplyHotKey(this.FindControl<Button>("BtnMirrorUD"), viewModel.MirrorUpDownKey);
        TryApplyHotKey(this.FindControl<Button>("BtnMirror180Hk"), viewModel.Mirror180Key);
        TryApplyHotKey(this.FindControl<Button>("BtnMirror45Hk"), viewModel.Mirror45Key);
        TryApplyHotKey(this.FindControl<Button>("BtnMirrorCcw45Hk"), viewModel.MirrorCcw45Key);
        TryApplyHotKey(this.FindControl<Button>("BtnSaveHk"), viewModel.SaveKey);
        TryApplyHotKey(this.FindControl<Button>("BtnPlayStopHk"), viewModel.PlayStopKey);
    }

    private static void TryApplyHotKey(Button? button, string? gestureString)
    {
        if (button == null) return;
        button.HotKey = KeyGestureUtil.TryParse(gestureString);
    }

    private void UpdateLoopButtonColors()
    {
        if (viewModel == null) return;

        var loopStartBtn = this.FindControl<Button>("LoopStart");
        var loopEndBtn = this.FindControl<Button>("LoopEnd");

        if (loopStartBtn != null)
        {
            if (viewModel.LoopStartSet)
            {
                loopStartBtn.Background = new SolidColorBrush(Color.Parse("#00CC00"));
                loopStartBtn.Foreground = new SolidColorBrush(Colors.White);
            }
            else
            {
                loopStartBtn.Background = new SolidColorBrush(Color.Parse("#DDDDDD"));
                loopStartBtn.Foreground = new SolidColorBrush(Colors.Black);
            }
        }

        if (loopEndBtn != null)
        {
            if (viewModel.HasLoopRegion)
            {
                loopEndBtn.Background = new SolidColorBrush(Color.Parse("#00CC00"));
                loopEndBtn.Foreground = new SolidColorBrush(Colors.White);
            }
            else
            {
                loopEndBtn.Background = new SolidColorBrush(Color.Parse("#DDDDDD"));
                loopEndBtn.Foreground = new SolidColorBrush(Colors.Black);
            }
        }
    }

    bool haveAsked = false;
    private async void MainWindow_Closing(object? sender, WindowClosingEventArgs e)
    {
        if (haveAsked) return;
        e.Cancel = true;
        haveAsked = true;
        if (viewModel != null && !await viewModel.AskSave())
        {
            viewModel.Dispose();
            this.Close();
        }
        else haveAsked = false;
    }

    private void MainWindow_LostFocus(object? sender, RoutedEventArgs e)
    {
        isCtrlKeyDown = false;
    }

    bool isCtrlKeyDown = false;

    private void MainWindow_KeyUp(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        isCtrlKeyDown = false;
    }

    private void MainWindow_KeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        isCtrlKeyDown = e.Key == Avalonia.Input.Key.LeftCtrl;
    }

    private void Caret_PositionChanged(object? sender, System.EventArgs e)
    {
        if (textEditor != null && viewModel != null)
        {
            var seek = textEditor.SelectionStart;
            var hasSelection = textEditor.SelectionLength > 0;
            // Ctrl during copy typically creates a selection; only treat Ctrl+click as an explicit "scrub to this caret" request.
            viewModel.SetCaretTime(seek, isCtrlKeyDown && !hasSelection);
        }
    }

    static double? lastX = null;
    private void SimaiVisual_PointerMoved(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        if (viewModel == null || textEditor == null || sender is not SimaiVisualizerControl control) return;
        var point = e.GetCurrentPoint(control);
        var x = point.Position.X;
        var isPressed = point.Properties.IsLeftButtonPressed;
        viewModel.IsPointerPressedSimaiVisual = isPressed;
        if (lastX is null) lastX = x;
        var delta = x - lastX.Value;
        if (isPressed)
        {
            var docseek = viewModel.SlideTrackTime((float)delta * 10f / Width);
            viewModel.SeekToDocPos(docseek, textEditor);
        }
        lastX = x;
    }

    private void ZoomIn_Click(object? sender, RoutedEventArgs e)
    {
        if (viewModel != null)
            viewModel.SlideZoomLevel(-0.3f);
    }
    private void ZoomOut_Click(object? sender, RoutedEventArgs e)
    {
        if (viewModel != null)
            viewModel.SlideZoomLevel(0.3f);
    }

    private void SimaiVisual_PointerWheelChanged(object? sender, Avalonia.Input.PointerWheelEventArgs e)
    {
        if (viewModel == null || textEditor == null) return;
        if (isCtrlKeyDown)
        {
            viewModel.SlideZoomLevel(-0.3f * (float)e.Delta.Y);
        }
        else
        {
            var docseek = viewModel.SlideTrackTime(e.Delta.Y);
            viewModel.SeekToDocPos(docseek, textEditor);
        }
    }

    private async void TextEditor_TextChanged(object? sender, System.EventArgs e)
    {
        if (viewModel == null || textEditor == null || sender is not TextEditor editor) return;
        //TODO: add timer
        await viewModel.SetFumenContent(editor.Text);
        var seek = textEditor.SelectionStart;
        viewModel.SetCaretTime(seek, false);
    }

    private async void FindReplace_PointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (textEditor == null) return;
        if (textEditor.SearchPanel.IsOpened)
            textEditor.SearchPanel.Close();
        else
        {
            textEditor.TextArea.Focus();
            await Task.Delay(100); // focus will cost time, or the searchpanel buttons wont work.
            textEditor.SearchPanel.Open();
        }
    }

    private void LoopOverlay_RegionSelected(object? sender, LoopSelectionEventArgs e)
    {
        if (viewModel?.LoopViewModel == null) return;
        // Only allow region selection when loop is enabled
        if (!viewModel.LoopViewModel.IsEnabled) return;

        // Convert pixel positions to time values
        // This needs to match the visualizer's time-to-pixel conversion
        var currentTime = viewModel.TrackTime;
        var zoomLevel = viewModel.TrackZoomLevel;
        var songLength = viewModel.SongTrackInfo?.Length ?? 0;
        var offset = viewModel.Offset;

        // Calculate visible time range (same formula as visualizer rendering)
        var visibleStart = currentTime - zoomLevel;
        var visibleEnd = currentTime + zoomLevel;

        // The overlay control's Bounds.Width is the full width
        // But we need to get it from the overlay since that's where the event comes from
        var overlayWidth = (sender as LoopVisualizerOverlay)?.Bounds.Width ?? 800;

        // Convert X positions to times
        var startTime = visibleStart + (e.StartPosition / overlayWidth) * (visibleEnd - visibleStart);
        var endTime = visibleStart + (e.EndPosition / overlayWidth) * (visibleEnd - visibleStart);

        // Clamp to valid range
        startTime = Math.Max(0, Math.Min(songLength, startTime));
        endTime = Math.Max(0, Math.Min(songLength, endTime));

        // Apply offset correction: the visualizer displays times with offset added
        // So we need to subtract offset to get the actual chart time
        var adjustedStartTime = startTime - offset;
        var adjustedEndTime = endTime - offset;

        // Try to set the loop region via ViewModel
        viewModel.LoopViewModel.TrySetRegion(adjustedStartTime, adjustedEndTime, viewModel.CurrentSimaiChart);
    }

}
