using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Threading;
using MajdataEdit_Neo.Models;
using SkiaSharp;

namespace MajdataEdit_Neo.Controls;

/// <summary>
/// Event arguments for loop region selection.
/// </summary>
public class LoopSelectionEventArgs : EventArgs
{
    /// <summary>
    /// The start position of the selection (in pixels from left).
    /// </summary>
    public double StartPosition { get; }

    /// <summary>
    /// The end position of the selection (in pixels from left).
    /// </summary>
    public double EndPosition { get; }

    public LoopSelectionEventArgs(double startPosition, double endPosition)
    {
        StartPosition = startPosition;
        EndPosition = endPosition;
    }
}

/// <summary>
/// Overlay control for the visualizer that handles loop region selection and rendering.
/// </summary>
public class LoopVisualizerOverlay : Control
{
    /// <summary>
    /// The loop region property.
    /// </summary>
    public static readonly DirectProperty<LoopVisualizerOverlay, LoopRegion?> LoopRegionProperty =
        AvaloniaProperty.RegisterDirect<LoopVisualizerOverlay, LoopRegion?>(
            nameof(LoopRegion),
            o => o.LoopRegion,
            (o, v) => o.LoopRegion = v,
            defaultBindingMode: Avalonia.Data.BindingMode.OneWay);

    private LoopRegion? _loopRegion;
    /// <summary>
    /// Gets or sets the current loop region.
    /// </summary>
    public LoopRegion? LoopRegion
    {
        get => _loopRegion;
        set => SetAndRaise(LoopRegionProperty, ref _loopRegion, value);
    }

    /// <summary>
    /// The current playback time property.
    /// </summary>
    public static readonly DirectProperty<LoopVisualizerOverlay, double> CurrentTimeProperty =
        AvaloniaProperty.RegisterDirect<LoopVisualizerOverlay, double>(
            nameof(CurrentTime),
            o => o.CurrentTime,
            (o, v) => o.CurrentTime = v);

    private double _currentTime;
    /// <summary>
    /// Gets or sets the current playback time in seconds.
    /// </summary>
    public double CurrentTime
    {
        get => _currentTime;
        set => SetAndRaise(CurrentTimeProperty, ref _currentTime, value);
    }

    /// <summary>
    /// The zoom level property (for time-to-pixel conversion).
    /// </summary>
    public static readonly DirectProperty<LoopVisualizerOverlay, float> ZoomLevelProperty =
        AvaloniaProperty.RegisterDirect<LoopVisualizerOverlay, float>(
            nameof(ZoomLevel),
            o => o.ZoomLevel,
            (o, v) => o.ZoomLevel = v);

    private float _zoomLevel = 4f;
    /// <summary>
    /// Gets or sets the zoom level.
    /// </summary>
    public float ZoomLevel
    {
        get => _zoomLevel;
        set => SetAndRaise(ZoomLevelProperty, ref _zoomLevel, value);
    }

    /// <summary>
    /// The song length property (for boundary validation).
    /// </summary>
    public static readonly DirectProperty<LoopVisualizerOverlay, double> SongLengthProperty =
        AvaloniaProperty.RegisterDirect<LoopVisualizerOverlay, double>(
            nameof(SongLength),
            o => o.SongLength,
            (o, v) => o.SongLength = v);

    private double _songLength = 0;
    /// <summary>
    /// Gets or sets the song length in seconds.
    /// </summary>
    public double SongLength
    {
        get => _songLength;
        set => SetAndRaise(SongLengthProperty, ref _songLength, value);
    }

    /// <summary>
    /// The audio offset property (for converting chart time to display time).
    /// </summary>
    public static readonly DirectProperty<LoopVisualizerOverlay, float> OffsetProperty =
        AvaloniaProperty.RegisterDirect<LoopVisualizerOverlay, float>(
            nameof(Offset),
            o => o.Offset,
            (o, v) => o.Offset = v);

    private float _offset = 0f;
    /// <summary>
    /// Gets or sets the audio offset in seconds.
    /// </summary>
    public float Offset
    {
        get => _offset;
        set => SetAndRaise(OffsetProperty, ref _offset, value);
    }

    /// <summary>
    /// Event fired when a loop region is selected via right-click drag.
    /// </summary>
    public event EventHandler<LoopSelectionEventArgs>? RegionSelected;

    private Point? _selectionStart;
    private Point? _selectionCurrent;
    private bool _isRightDragging;

    /// <summary>
    /// Initializes a new instance of the LoopVisualizerOverlay.
    /// </summary>
    public LoopVisualizerOverlay()
    {
        ClipToBounds = true;
        AffectsRender<LoopVisualizerOverlay>(
            LoopRegionProperty,
            CurrentTimeProperty,
            ZoomLevelProperty,
            SongLengthProperty,
            OffsetProperty);
    }

    /// <summary>
    /// Handles pointer pressed events for right-click drag initiation.
    /// </summary>
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        var point = e.GetCurrentPoint(this);
        if (point.Properties.IsRightButtonPressed)
        {
            _selectionStart = point.Position;
            _isRightDragging = true;
            e.Handled = true;
        }
    }

    /// <summary>
    /// Handles pointer moved events for drag feedback.
    /// </summary>
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (_isRightDragging && _selectionStart is not null)
        {
            _selectionCurrent = e.GetCurrentPoint(this).Position;
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Handles pointer released events for completing the drag selection.
    /// </summary>
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_isRightDragging && _selectionStart is not null)
        {
            var endPoint = e.GetCurrentPoint(this).Position;
            var startPosition = _selectionStart.Value.X;
            var endPosition = endPoint.X;

            // Fire event with selected region
            RegionSelected?.Invoke(this, new LoopSelectionEventArgs(startPosition, endPosition));

            _selectionStart = null;
            _selectionCurrent = null;
            _isRightDragging = false;
            InvalidateVisual();
            e.Handled = true;
        }
    }

    /// <summary>
    /// Renders the loop visualization.
    /// </summary>
    public override void Render(DrawingContext context)
    {
        if (LoopRegion is null && !_isRightDragging)
            return;

        context.Custom(new LoopDrawOperation(
            new Rect(0, 0, Bounds.Width, Bounds.Height),
            LoopRegion,
            CurrentTime,
            ZoomLevel,
            SongLength,
            Offset,
            _selectionStart,
            _selectionCurrent,
            _isRightDragging
        ));

        Dispatcher.UIThread.InvokeAsync(InvalidateVisual, DispatcherPriority.Background);
    }

    /// <summary>
    /// Custom draw operation for rendering the loop region.
    /// </summary>
    private class LoopDrawOperation : ICustomDrawOperation
    {
        private readonly LoopRegion? _loopRegion;
        private readonly double _currentTime;
        private readonly float _zoomLevel;
        private readonly double _songLength;
        private readonly float _offset;
        private readonly Point? _selectionStart;
        private readonly Point? _selectionCurrent;
        private readonly bool _isDragging;

        public Rect Bounds { get; }

        public LoopDrawOperation(Rect bounds,
            LoopRegion? region,
            double currentTime,
            float zoomLevel,
            double songLength,
            float offset,
            Point? selectionStart,
            Point? selectionCurrent,
            bool isDragging)
        {
            Bounds = bounds;
            _loopRegion = region;
            _currentTime = currentTime;
            _zoomLevel = zoomLevel;
            _songLength = songLength;
            _offset = offset;
            _selectionStart = selectionStart;
            _selectionCurrent = selectionCurrent;
            _isDragging = isDragging;
        }

        public void Dispose() { }

        public bool HitTest(Point p) => false;

        public bool Equals(ICustomDrawOperation? other) => false;

        public void Render(ImmediateDrawingContext context)
        {
            var leaseFeature = context.TryGetFeature(typeof(ISkiaSharpApiLeaseFeature)) as ISkiaSharpApiLeaseFeature;
            if (leaseFeature == null) return;

            using var lease = leaseFeature.Lease();
            var canvas = lease.SkCanvas;
            var width = Bounds.Width;
            var height = Bounds.Height;

            // Calculate visible time range
            var visibleStart = _currentTime - _zoomLevel;
            var visibleEnd = _currentTime + _zoomLevel;

            // Time to pixel conversion
            double TimeToX(double time)
            {
                if (_zoomLevel == 0) return width / 2;
                return ((time - visibleStart) / (visibleEnd - visibleStart)) * width;
            }

            using var paint = new SKPaint();

            if (_loopRegion is not null)
            {
                // Convert chart times to display times by adding offset
                var startX = TimeToX(_loopRegion.StartTime + _offset);
                var endX = TimeToX(_loopRegion.EndTime + _offset);

                // Clamp to visible bounds
                var drawStart = Math.Max(0, startX);
                var drawEnd = Math.Min(width, endX);

                if (drawEnd > drawStart)
                {
                    // Draw transparent highlight
                    paint.Color = new SKColor(0, 200, 200, 40); // Cyan/turquoise, transparent
                    paint.Style = SKPaintStyle.Fill;
                    canvas.DrawRect((float)drawStart, 0, (float)(drawEnd - drawStart), (float)height, paint);

                    // Draw start line
                    paint.Color = new SKColor(0, 200, 200, 255); // Cyan/turquoise, solid
                    paint.StrokeWidth = 2;
                    paint.Style = SKPaintStyle.Stroke;
                    canvas.DrawLine((float)startX, 0, (float)startX, (float)height, paint);

                    // Draw end line
                    canvas.DrawLine((float)endX, 0, (float)endX, (float)height, paint);
                }
            }

            // Draw drag selection
            if (_isDragging && _selectionStart is not null && _selectionCurrent is not null)
            {
                var drawStart = Math.Min(_selectionStart.Value.X, _selectionCurrent.Value.X);
                var drawEnd = Math.Max(_selectionStart.Value.X, _selectionCurrent.Value.X);

                paint.Color = new SKColor(0, 255, 255, 60); // Cyan with alpha
                paint.Style = SKPaintStyle.Fill;
                canvas.DrawRect((float)drawStart, 0, (float)(drawEnd - drawStart), (float)height, paint);

                paint.Color = new SKColor(0, 255, 255, 180);
                paint.StrokeWidth = 2;
                paint.Style = SKPaintStyle.Stroke;
                canvas.DrawLine((float)drawStart, 0, (float)drawStart, (float)height, paint);
                canvas.DrawLine((float)drawEnd, 0, (float)drawEnd, (float)height, paint);
            }
        }
    }
}
