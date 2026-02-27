namespace OrthoPlanner.Core.Imaging;

public class DicomSeriesInfo
{
    public string SeriesInstanceUID { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string PatientDOB { get; set; } = string.Empty;
    public string StudyDate { get; set; } = string.Empty;
    public string SeriesDescription { get; set; } = string.Empty;
    
    public int ImageCount { get; set; }
    public List<string> FilePaths { get; set; } = new();

    // Data for UI thumbnail preview
    public byte[]? PreviewPixels { get; set; }
    public int PreviewWidth { get; set; }
    public int PreviewHeight { get; set; }
}
