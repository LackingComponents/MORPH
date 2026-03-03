using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using OrthoPlanner.App.ViewModels.Photogrammetry;

namespace OrthoPlanner.App.Views;

public partial class PhotogrammetryView : UserControl
{
    private bool _isDragging = false;
    private Point _lastMousePosition;
    
    // Tool interaction state
    private Point? _toolStartPoint = null;
    private Line? _activeInteractionLine = null;

    public PhotogrammetryView()
    {
        InitializeComponent();
        DataContextChanged += PhotogrammetryView_DataContextChanged;
    }

    private void PhotogrammetryView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is PhotogrammetryViewModel oldVm)
        {
            oldVm.PropertyChanged -= ViewModel_PropertyChanged;
        }
        if (e.NewValue is PhotogrammetryViewModel newVm)
        {
            newVm.PropertyChanged += ViewModel_PropertyChanged;
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PhotogrammetryViewModel.ActivePhoto))
        {
            if (DataContext is PhotogrammetryViewModel vm && vm.ActivePhoto != null)
            {
                vm.ActivePhoto.PropertyChanged -= ActivePhoto_PropertyChanged;
                vm.ActivePhoto.PropertyChanged += ActivePhoto_PropertyChanged;
                
                // Try initial fit
                if (vm.ActivePhoto.Scale == 0)
                {
                    FitImageToViewport(vm.ActivePhoto);
                }
                
                UpdateGrid();
            }
        }
        else if (e.PropertyName == nameof(PhotogrammetryViewModel.ShowGridOverlay))
        {
            UpdateGrid();
        }
    }

    private void ActivePhoto_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PhotoViewModel.Scale) ||
            e.PropertyName == nameof(PhotoViewModel.PanX) ||
            e.PropertyName == nameof(PhotoViewModel.PanY) ||
            e.PropertyName == nameof(PhotoViewModel.PixelsPerMm))
        {
            UpdateGrid();
        }
    }

    private void FitImageToViewport(PhotoViewModel photo)
    {
        if (photo.ImageSource == null) return;
        
        double viewW = ViewportGrid.ActualWidth;
        double viewH = ViewportGrid.ActualHeight;
        
        if (viewW == 0 || viewH == 0)
        {
            // If layout hasn't happened yet, defer to SizeChanged
            ViewportGrid.SizeChanged += ViewportGrid_SizeChanged;
            return;
        }

        double imgW = photo.ImageSource.Width;
        double imgH = photo.ImageSource.Height;
        
        if (imgW == 0 || imgH == 0) return;
        
        double scaleX = viewW / imgW;
        double scaleY = viewH / imgH;
        
        // Fit entirely with 90% padding
        photo.Scale = Math.Min(scaleX, scaleY) * 0.95;
        photo.PanX = (viewW - imgW) / 2;
        photo.PanY = (viewH - imgH) / 2;
    }

    private void ViewportGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ViewportGrid.SizeChanged -= ViewportGrid_SizeChanged;
        
        if (DataContext is PhotogrammetryViewModel vm && vm.ActivePhoto != null && vm.ActivePhoto.Scale == 0)
        {
            FitImageToViewport(vm.ActivePhoto);
        }
    }

    private void Viewport_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (DataContext is not PhotogrammetryViewModel vm || vm.ActivePhoto == null) return;

        double zoomDelta = e.Delta > 0 ? 1.1 : 0.9;
        
        // Adjust pan to zoom towards mouse cursor
        Point mousePos = e.GetPosition(ImageContainer);
        
        double oldScale = vm.ActivePhoto.Scale;
        double newScale = Math.Max(0.1, Math.Min(50.0, oldScale * zoomDelta));
        
        vm.ActivePhoto.Scale = newScale;

        // Note: Real zoom to pointer requires adjusting PanX/PanY based on the center of the viewport 
        // relative to the mouse position. For simplicity, we just scale from center here since 
        // RenderTransformOrigin is 0.5,0.5.
        // If we want exact pointer zoom, we adjust Pan:
        /*
        double ratio = newScale / oldScale;
        double dx = (mousePos.X * oldScale) - (mousePos.X * newScale);
        double dy = (mousePos.Y * oldScale) - (mousePos.Y * newScale);
        vm.ActivePhoto.PanX += dx;
        vm.ActivePhoto.PanY += dy;
        */
        
        e.Handled = true;
    }

    private void Viewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not PhotogrammetryViewModel vm || vm.ActivePhoto == null) return;

        _lastMousePosition = e.GetPosition(this);
        
        if (vm.ActiveTool == PhotogrammetryToolMode.None)
        {
            _isDragging = true;
            CaptureMouse();
        }
        else
        {
            // Tool usage (Normalize, Horizon, Measure)
            _toolStartPoint = e.GetPosition(DrawingCanvas); // Get position relative to the image itself
            
            _activeInteractionLine = new Line
            {
                X1 = _toolStartPoint.Value.X,
                Y1 = _toolStartPoint.Value.Y,
                X2 = _toolStartPoint.Value.X,
                Y2 = _toolStartPoint.Value.Y,
                Stroke = new SolidColorBrush(vm.ActiveTool == PhotogrammetryToolMode.Measure ? Colors.SpringGreen : Colors.DeepSkyBlue),
                StrokeThickness = 2 / vm.ActivePhoto.Scale, // Keep line thickness visually constant
                Opacity = 0.8
            };
            DrawingCanvas.Children.Add(_activeInteractionLine);
            CaptureMouse();
        }
    }

    private void Viewport_MouseMove(object sender, MouseEventArgs e)
    {
        if (DataContext is not PhotogrammetryViewModel vm || vm.ActivePhoto == null) return;

        if (_isDragging && e.LeftButton == MouseButtonState.Pressed)
        {
            Point currentPos = e.GetPosition(this);
            double dx = currentPos.X - _lastMousePosition.X;
            double dy = currentPos.Y - _lastMousePosition.Y;

            vm.ActivePhoto.PanX += dx;
            vm.ActivePhoto.PanY += dy;

            _lastMousePosition = currentPos;
        }
        else if (_toolStartPoint.HasValue && _activeInteractionLine != null && e.LeftButton == MouseButtonState.Pressed)
        {
            Point currentPos = e.GetPosition(DrawingCanvas);
            _activeInteractionLine.X2 = currentPos.X;
            _activeInteractionLine.Y2 = currentPos.Y;
            _activeInteractionLine.StrokeThickness = 2 / vm.ActivePhoto.Scale;
        }
    }

    private void Viewport_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not PhotogrammetryViewModel vm || vm.ActivePhoto == null) return;

        if (_isDragging)
        {
            _isDragging = false;
            ReleaseMouseCapture();
        }
        else if (_toolStartPoint.HasValue && _activeInteractionLine != null)
        {
            Point endPoint = e.GetPosition(DrawingCanvas);
            ReleaseMouseCapture();
            
            double dx = endPoint.X - _toolStartPoint.Value.X;
            double dy = endPoint.Y - _toolStartPoint.Value.Y;
            double pixelDistance = Math.Sqrt(dx * dx + dy * dy);

            if (pixelDistance > 5) // Minimum drag threshold
            {
                if (vm.ActiveTool == PhotogrammetryToolMode.Normalize)
                {
                    ShowNormalizationDialog(vm.ActivePhoto, pixelDistance);
                }
                else if (vm.ActiveTool == PhotogrammetryToolMode.Horizon)
                {
                    vm.ActivePhoto.ReHorizon(_toolStartPoint.Value, endPoint);
                }
                else if (vm.ActiveTool == PhotogrammetryToolMode.Measure)
                {
                    if (vm.ActivePhoto.IsNormalized)
                    {
                        var mv = new MeasurementViewModel
                        {
                            Name = $"M{vm.ActivePhoto.Measurements.Count + 1}",
                            StartPoint = _toolStartPoint.Value,
                            EndPoint = endPoint,
                            DistanceMm = pixelDistance / vm.ActivePhoto.PixelsPerMm
                        };
                        vm.ActivePhoto.Measurements.Add(mv);
                        
                        // Keep the line for measures
                        var permLine = new Line
                        {
                            X1 = _toolStartPoint.Value.X,
                            Y1 = _toolStartPoint.Value.Y,
                            X2 = endPoint.X,
                            Y2 = endPoint.Y,
                            Stroke = Brushes.SpringGreen,
                            StrokeThickness = 2 / vm.ActivePhoto.Scale
                        };
                        DrawingCanvas.Children.Add(permLine);
                    }
                    else
                    {
                        MessageBox.Show("Please normalize the scale first.", "Cannot Measure");
                    }
                }
            }

            // Cleanup the temporary interaction line
            DrawingCanvas.Children.Remove(_activeInteractionLine);
            _activeInteractionLine = null;
            _toolStartPoint = null;
            
            // Reset tool to Pan/Zoom naturally after one use? Or keep it active.
            // Let's keep it active unless they reset.
        }
    }

    private void ShowNormalizationDialog(PhotoViewModel photo, double pixelDistance)
    {
        var ownerWindow = Window.GetWindow(this);
        // Use a simple InputBox equivalent
        var dialog = new Window
        {
            Title = "Normalize Scale",
            Width = 300,
            Height = 150,
            WindowStartupLocation = ownerWindow != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            Owner = ownerWindow,
            Background = new SolidColorBrush(Color.FromRgb(30, 33, 40)),
            Foreground = Brushes.White
        };

        var stack = new StackPanel { Margin = new Thickness(15) };
        stack.Children.Add(new TextBlock { Text = "Enter the known physical length in mm:", Foreground = Brushes.White, Margin = new Thickness(0,0,0,10) });
        
        var txtInput = new TextBox { Width = 100, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0,0,0,15) };
        stack.Children.Add(txtInput);

        var btnOk = new Button { Content = "OK", Width = 80, HorizontalAlignment = HorizontalAlignment.Right, Background = new SolidColorBrush(Color.FromRgb(27, 152, 224)), Foreground = Brushes.White, BorderThickness = new Thickness(0) };
        btnOk.Click += (s, e) =>
        {
            if (double.TryParse(txtInput.Text, out double mmDistance) && mmDistance > 0)
            {
                photo.NormalizeScale(pixelDistance, mmDistance);
                dialog.DialogResult = true;
            }
            else
            {
                MessageBox.Show("Please enter a valid positive number.");
            }
        };
        stack.Children.Add(btnOk);

        dialog.Content = stack;
        dialog.ShowDialog();
    }

    private void UpdateGrid()
    {
        GridOverlayCanvas.Children.Clear();

        if (DataContext is not PhotogrammetryViewModel vm || !vm.ShowGridOverlay || vm.ActivePhoto == null || !vm.ActivePhoto.IsNormalized)
            return;

        double ppmm = vm.ActivePhoto.PixelsPerMm;
        double scale = vm.ActivePhoto.Scale;
        
        if (ppmm == 0 || scale == 0) return;

        double canvasWidth = GridOverlayCanvas.ActualWidth;
        double canvasHeight = GridOverlayCanvas.ActualHeight;

        if (canvasWidth == 0 || canvasHeight == 0) return;

        // Visual screen pixels per physical mm
        double scaledPpmm = ppmm * scale;

        // If zoomed out too far, stop drawing 1mm lines to prevent dense mess
        bool drawSmall = scaledPpmm > 5;     // Only draw 1mm if they are at least 5px apart visually
        bool drawMedium = scaledPpmm > 1;    // Only draw 5mm if 1mm is at least 1px apart

        // Image Center in Viewport coords
        double imgW = vm.ActivePhoto.ImageSource.Width;
        double imgH = vm.ActivePhoto.ImageSource.Height;
        Point imgCenter = new Point(imgW / 2 + vm.ActivePhoto.PanX, imgH / 2 + vm.ActivePhoto.PanY);
        
        // Let's draw the grid extending outwards from the image center
        // Grid spacing in visual pixels
        double step1mm = scaledPpmm;
        double step5mm = scaledPpmm * 5;
        double step10mm = scaledPpmm * 10;

        var brush1mm = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
        var brush5mm = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255));
        var brush10mm = new SolidColorBrush(Color.FromArgb(140, 255, 255, 255));

        // Start from center and go both ways
        
        // Verticals
        for (double x = imgCenter.X; x < canvasWidth; x += step1mm)
            if(x >= 0) DrawVertical(x, step1mm, step5mm, step10mm, imgCenter.X, brush1mm, brush5mm, brush10mm, canvasHeight, drawSmall, drawMedium);
        for (double x = imgCenter.X - step1mm; x > 0; x -= step1mm)
            if(x < canvasWidth) DrawVertical(x, step1mm, step5mm, step10mm, imgCenter.X, brush1mm, brush5mm, brush10mm, canvasHeight, drawSmall, drawMedium);

        // Horizontals
        for (double y = imgCenter.Y; y < canvasHeight; y += step1mm)
            if(y >= 0) DrawHorizontal(y, step1mm, step5mm, step10mm, imgCenter.Y, brush1mm, brush5mm, brush10mm, canvasWidth, drawSmall, drawMedium);
        for (double y = imgCenter.Y - step1mm; y > 0; y -= step1mm)
            if(y < canvasHeight) DrawHorizontal(y, step1mm, step5mm, step10mm, imgCenter.Y, brush1mm, brush5mm, brush10mm, canvasWidth, drawSmall, drawMedium);
    }

    private void DrawVertical(double x, double step1, double step5, double step10, double originX, Brush b1, Brush b5, Brush b10, double height, bool drawSmall, bool drawMedium)
    {
        double dist = Math.Abs(x - originX);
        Brush stroke = b1;
        double thickness = 1;
        
        if (Math.Abs((dist % step10) / step10) < 0.01) { stroke = b10; thickness = 2; }
        else if (Math.Abs((dist % step5) / step5) < 0.01) { if (!drawMedium) return; stroke = b5; thickness = 1.5; }
        else { if (!drawSmall) return; stroke = b1; thickness = 1; }

        var line = new Line { X1 = x, Y1 = 0, X2 = x, Y2 = height, Stroke = stroke, StrokeThickness = thickness };
        GridOverlayCanvas.Children.Add(line);
    }

    private void DrawHorizontal(double y, double step1, double step5, double step10, double originY, Brush b1, Brush b5, Brush b10, double width, bool drawSmall, bool drawMedium)
    {
        double dist = Math.Abs(y - originY);
        Brush stroke = b1;
        double thickness = 1;
        
        if (Math.Abs((dist % step10) / step10) < 0.01) { stroke = b10; thickness = 2; }
        else if (Math.Abs((dist % step5) / step5) < 0.01) { if (!drawMedium) return; stroke = b5; thickness = 1.5; }
        else { if (!drawSmall) return; stroke = b1; thickness = 1; }

        var line = new Line { X1 = 0, Y1 = y, X2 = width, Y2 = y, Stroke = stroke, StrokeThickness = thickness };
        GridOverlayCanvas.Children.Add(line);
    }
}
