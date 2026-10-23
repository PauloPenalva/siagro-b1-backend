using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Mdfe;

/// <summary>Qual MDF-e leva o CT-e (para o detalhe do CT-e): o ativo; sem ativo, o mais recente.</summary>
public class CteManifestLinkQuery(IUnitOfWork db, IConfiguration configuration)
{
    public async Task<CteManifestLink?> GetAsync(Guid cteKey)
    {
        TransportManifestsCreateFromCtesService.EnsureStandalone(configuration);
        return await db.Context.TransportManifestCtes.AsNoTracking()
            .Where(l => l.TransportDocumentKey == cteKey)
            .OrderByDescending(l => l.IsActive).ThenByDescending(l => l.Manifest!.CreatedAt)
            .Select(l => new CteManifestLink
            {
                ManifestKey = l.ManifestKey, Number = l.Manifest!.Number, Series = l.Manifest.Series, Status = l.Manifest.Status,
            })
            .FirstOrDefaultAsync();
    }
}

/// <summary>Vínculo CT-e → MDF-e. Status é o mesmo enum MdfeStatus do TransportManifest (serializa como nome).</summary>
public class CteManifestLink
{
    public Guid ManifestKey { get; set; }
    public int? Number { get; set; }
    public int Series { get; set; }
    public MdfeStatus Status { get; set; }
}
