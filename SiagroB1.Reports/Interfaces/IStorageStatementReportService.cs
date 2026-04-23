using SiagroB1.Reports.Dtos;

namespace SiagroB1.Reports.Interfaces;

public interface IStorageStatementReportService
{
    Task<(List<StorageStatementReportRowDto> rows, List<StorageStatementReportHeaderDto> header)> GetReportAsync(
        StorageStatementReportFilter filter,
        CancellationToken cancellationToken = default);
}