namespace SiagroB1.Reports.Services;

public interface IFastReportService
{
    Task<byte[]> GeneratePdfAsync(
        string reportName,
        Dictionary<string, object> parameters);

    Task<byte[]> GeneratePdfAsync<T>(
        string reportName,
        ICollection<T> data,
        string dataSourceName,
        string refName,
        Dictionary<string, object> parameters);
}
