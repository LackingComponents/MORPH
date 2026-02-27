using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OrthoPlanner.Core.Imaging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OrthoPlanner.App.ViewModels;

public class SeriesItemViewModel : ObservableObject
{
    public DicomSeriesInfo Info { get; }
    public WriteableBitmap? PreviewImage { get; }

    public string Title => $"{Info.SeriesDescription} ({Info.ImageCount} images)";
    public string PatientDetails => $"Name: {Info.PatientName} | DOB: {Info.PatientDOB}";
    public string StudyDetails => $"Study Date: {Info.StudyDate}";

    public SeriesItemViewModel(DicomSeriesInfo info)
    {
        Info = info;
        if (info.PreviewPixels != null && info.PreviewWidth > 0 && info.PreviewHeight > 0)
        {
            var bmp = new WriteableBitmap(info.PreviewWidth, info.PreviewHeight, 96, 96, PixelFormats.Gray8, null);
            bmp.WritePixels(new Int32Rect(0, 0, info.PreviewWidth, info.PreviewHeight), info.PreviewPixels, info.PreviewWidth, 0);
            PreviewImage = bmp;
        }
    }
}

public partial class DicomSelectorViewModel : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<SeriesItemViewModel> _seriesList = new();

    [ObservableProperty]
    private SeriesItemViewModel? _selectedSeries;

    public Action? OnClose;
    public bool Accepted { get; private set; }

    public DicomSelectorViewModel(List<DicomSeriesInfo> series)
    {
        foreach (var s in series) SeriesList.Add(new SeriesItemViewModel(s));
        if (SeriesList.Count > 0) SelectedSeries = SeriesList[0];
    }

    [RelayCommand]
    private void Accept()
    {
        if (SelectedSeries == null) return;
        Accepted = true;
        OnClose?.Invoke();
    }

    [RelayCommand]
    private void Cancel()
    {
        Accepted = false;
        OnClose?.Invoke();
    }
}
