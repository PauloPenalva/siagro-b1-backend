using SiagroB1.Reports.Services;

namespace SiagroB1.Application.Tests.Support;

/// <summary>Não gera PDF: guarda o que o serviço de relatório pediu, para o teste conferir.</summary>
public sealed class RecordingFastReportService : IFastReportService
{
    public string? LastReportName { get; private set; }
    public Dictionary<string, object>? LastParameters { get; private set; }
    public object? LastData { get; private set; }

    public Task<byte[]> GeneratePdfAsync(string reportName, Dictionary<string, object> parameters)
    {
        LastReportName = reportName;
        LastParameters = parameters;
        return Task.FromResult(Array.Empty<byte>());
    }

    public Task<byte[]> GeneratePdfAsync<T>(
        string reportName,
        ICollection<T> data,
        string dataSourceName,
        string refName,
        Dictionary<string, object> parameters)
    {
        LastReportName = reportName;
        LastParameters = parameters;
        LastData = data;
        return Task.FromResult(Array.Empty<byte>());
    }
}
