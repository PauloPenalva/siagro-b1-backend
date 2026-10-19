using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Interfaces;

/// <summary>
/// PDF do DANFE para o e-mail da NF-e. Fica no Domain (e não no Application) porque a implementação HTTP
/// mora no Infra, que não enxerga o Application. Lança quando o Reports não responde: o job conta a tentativa.
/// </summary>
public interface IDanfeProvider
{
    Task<byte[]> GetPdfAsync(NfeEmailDocumentType type, Guid key, string companyCode, CancellationToken ct = default);
}
