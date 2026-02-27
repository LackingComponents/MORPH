using System.Collections.Generic;
using System.Linq;
using System.Windows;
using OrthoPlanner.App.ViewModels;

namespace OrthoPlanner.App
{
    public partial class ExportWindow : Window
    {
        private readonly IEnumerable<SegmentViewModel> _availableSegments;
        public List<SegmentViewModel> SelectedSegments { get; private set; } = new();

        public ExportWindow(IEnumerable<SegmentViewModel> availableSegments)
        {
            InitializeComponent();
            _availableSegments = availableSegments;
            SegmentsList.ItemsSource = _availableSegments;
            
            foreach (var seg in _availableSegments)
                seg.IsSelectedForExport = true;
        }

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            SelectedSegments = _availableSegments.Where(s => s.IsSelectedForExport).ToList();
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
