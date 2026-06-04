using System.ComponentModel;
using System.IO;
using System.Windows;

namespace OrthoPlanner.App;

/// <summary>
/// Data model for each loaded STL file in the classification dialog.
/// </summary>
public class StlFileEntry : INotifyPropertyChanged
{
    public string FilePath { get; set; } = "";
    public string FileName => Path.GetFileNameWithoutExtension(FilePath);
    public string GroupKey => $"ScanGroup_{FilePath.GetHashCode():X}";

    private bool _isUpper;
    private bool _isLower;
    private bool _isOther = true; // Default

    public bool IsUpper
    {
        get => _isUpper;
        set { _isUpper = value; if (value) { _isLower = false; _isOther = false; } OnPropertyChanged(nameof(IsUpper)); OnPropertyChanged(nameof(IsLower)); OnPropertyChanged(nameof(IsOther)); }
    }
    public bool IsLower
    {
        get => _isLower;
        set { _isLower = value; if (value) { _isUpper = false; _isOther = false; } OnPropertyChanged(nameof(IsUpper)); OnPropertyChanged(nameof(IsLower)); OnPropertyChanged(nameof(IsOther)); }
    }
    public bool IsOther
    {
        get => _isOther;
        set { _isOther = value; if (value) { _isUpper = false; _isLower = false; } OnPropertyChanged(nameof(IsUpper)); OnPropertyChanged(nameof(IsLower)); OnPropertyChanged(nameof(IsOther)); }
    }

    public string Classification => IsUpper ? "Upper" : IsLower ? "Lower" : "Other";

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public partial class StlClassificationDialog : Window
{
    public List<StlFileEntry> Entries { get; } = new();
    public bool Accepted { get; private set; }
    private bool _isUpdating;

    public StlClassificationDialog(string[] filePaths)
    {
        InitializeComponent();

        foreach (var path in filePaths)
        {
            var entry = new StlFileEntry { FilePath = path };
            entry.PropertyChanged += Entry_PropertyChanged;
            Entries.Add(entry);
        }
        FileList.ItemsSource = Entries;
    }

    private void Entry_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isUpdating) return;
        if (Entries.Count != 2) return;
        if (sender is not StlFileEntry changedEntry) return;

        if (e.PropertyName == nameof(StlFileEntry.IsUpper) || e.PropertyName == nameof(StlFileEntry.IsLower))
        {
            _isUpdating = true;
            try
            {
                var otherEntry = Entries.FirstOrDefault(x => x != changedEntry);
                if (otherEntry != null)
                {
                    if (e.PropertyName == nameof(StlFileEntry.IsUpper) && changedEntry.IsUpper)
                    {
                        otherEntry.IsLower = true;
                    }
                    else if (e.PropertyName == nameof(StlFileEntry.IsLower) && changedEntry.IsLower)
                    {
                        otherEntry.IsUpper = true;
                    }
                }
            }
            finally
            {
                _isUpdating = false;
            }
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Accepted = true;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Accepted = false;
        DialogResult = false;
        Close();
    }
}
