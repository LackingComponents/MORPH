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

        ColorGrid.Children.Clear();
        foreach (var color in StandardColorPalette.Colors)
        {
            var swatch = new Border
            {
                Margin = new Thickness(1),
                Background = new SolidColorBrush(color),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x50, 0x58, 0x60)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(1),
                Cursor = Cursors.Hand,
                ToolTip = $"#{color.R:X2}{color.G:X2}{color.B:X2}"
            };

            var picked = color;
            swatch.MouseLeftButtonUp += (_, _) =>
            {
                ApplyColor(picked);
                ColorPopup.IsOpen = false;
            };
            ColorGrid.Children.Add(swatch);
        }
    }

    private void ColorSquare_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        BuildColorPalette();
        ColorPopup.IsOpen = true;
    }

    private void OpacitySlider_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
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
