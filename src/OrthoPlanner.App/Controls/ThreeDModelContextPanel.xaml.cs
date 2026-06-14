using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using OrthoPlanner.App.ViewModels;

namespace OrthoPlanner.App.Controls;

public partial class ThreeDModelContextPanel : UserControl
{
    private bool _paletteBuilt;

    public ThreeDModelContextPanel()
    {
        InitializeComponent();
        Loaded += (_, _) => BuildColorPalette();
    }

    private void BuildColorPalette()
    {
        if (_paletteBuilt) return;
        _paletteBuilt = true;

        ColorPaletteMenu.Items.Clear();
        foreach (var color in StandardColorPalette.Colors)
        {
            var swatch = new MenuItem
            {
                Header = new Border
                {
                    Width = 18, Height = 18, Margin = new Thickness(1),
                    Background = new SolidColorBrush(color),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x50, 0x58, 0x60)),
                    BorderThickness = new Thickness(1),
                    ToolTip = $"#{color.R:X2}{color.G:X2}{color.B:X2}"
                },
                Padding = new Thickness(2),
                StaysOpenOnClick = false
            };

            var picked = color;
            swatch.Click += (_, _) => ApplyColor(picked);
            ColorPaletteMenu.Items.Add(swatch);
        }
    }

    private void ColorSquare_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        BuildColorPalette();
        ColorSquare.ContextMenu!.PlacementTarget = ColorSquare;
        ColorSquare.ContextMenu.IsOpen = true;
    }

    private void OpacitySlider_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Keep the parent context menu open while dragging transparency.
        e.Handled = false;
        var menu = FindParentContextMenu(this);
        if (menu != null)
        {
            foreach (var item in menu.Items.OfType<MenuItem>())
                item.StaysOpenOnClick = true;
        }
    }

    private void ExpButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        var model = DataContext;
        if (model == null) return;

        var mainVm = GetMainViewModel();
        if (mainVm == null) return;

        // Close menus so the save dialog is not blocked.
        CloseAllContextMenus(this);

        if (mainVm.ExportSingleModelCommand.CanExecute(model))
            mainVm.ExportSingleModelCommand.Execute(model);
    }

    private void ApplyColor(Color color)
    {
        var model = DataContext;
        if (model == null) return;

        var mainVm = GetMainViewModel();
        mainVm?.ApplyModelColor(model, color);
    }

    private static MainViewModel? GetMainViewModel()
    {
        if (Application.Current.MainWindow?.DataContext is MainViewModel vm)
            return vm;
        return null;
    }

    private static ContextMenu? FindParentContextMenu(DependencyObject child)
    {
        while (child != null)
        {
            if (child is ContextMenu menu) return menu;
            child = LogicalTreeHelper.GetParent(child) ?? VisualTreeHelper.GetParent(child);
        }
        return null;
    }

    private static void CloseAllContextMenus(DependencyObject element)
    {
        if (element is ContextMenu menu)
            menu.IsOpen = false;

        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
            CloseAllContextMenus(VisualTreeHelper.GetChild(element, i));
    }
}
