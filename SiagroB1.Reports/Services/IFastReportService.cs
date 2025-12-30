namespace SiagroB1.Reports.Services;

public interface IFastReportService
{
    Task<byte[]> GeneratePdfAsync(
        string reportName,
        Dictionary<string, object> parameters);
}
