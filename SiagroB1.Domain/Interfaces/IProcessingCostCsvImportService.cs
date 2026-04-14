using Microsoft.AspNetCore.Http;
using SiagroB1.Domain.Dtos;

namespace SiagroB1.Domain.Interfaces;

public interface IProcessingCostCsvImportService
{
    Task<ImportResultDto> ImportDryingParametersAsync(
        string processingCostCode,
        IFormFile file,
        CancellationToken cancellationToken = default);

    Task<ImportResultDto> ImportDryingDetailsAsync(
        string processingCostCode,
        IFormFile file,
        CancellationToken cancellationToken = default);
}