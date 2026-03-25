using System.Windows;
using System.Windows.Media;
using OrthoPlanner.App.ViewModels;

namespace OrthoPlanner.App.Views;

public partial class DicomSelectorWindow : Window
{
    public DicomSelectorWindow(DicomSelectorViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.OnClose = Close;
    }

    private void ListBox_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        if (sender is System.Windows.Controls.ListBox listBox)
        {
            var scrollViewer = GetScrollViewer(listBox);
            if (scrollViewer != null)
            {
                if (e.Delta > 0)
                    scrollViewer.LineUp();
                else
                    scrollViewer.LineDown();
                
                e.Handled = true;
            }
        }
    }
    
    private void ListBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (sender is System.Windows.Controls.ListBox listBox)
        {
            if (e.Key == System.Windows.Input.Key.Up && listBox.SelectedIndex == 0)
            {
                e.Handled = true;
            }
            else if (e.Key == System.Windows.Input.Key.Down && listBox.SelectedIndex == listBox.Items.Count - 1)
            {
                e.Handled = true;
            }
        }
    }

    private System.Windows.Controls.ScrollViewer? GetScrollViewer(DependencyObject depObj)
    {
        if (depObj is System.Windows.Controls.ScrollViewer sv) return sv;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
        {
            var child = VisualTreeHelper.GetChild(depObj, i);
            var result = GetScrollViewer(child);
            if (result != null) return result;
        }
        return null;
    }
}
