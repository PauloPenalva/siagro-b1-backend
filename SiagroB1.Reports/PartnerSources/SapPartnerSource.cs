using Microsoft.EntityFrameworkCore;
using SiagroB1.Infra.Context;
using SiagroB1.Reports.Dtos;

namespace SiagroB1.Reports.PartnerSources;

/// <summary>
/// Lê o parceiro direto das tabelas do SAP Business One (OCRD / CRD1).
/// Usada quando <c>Erp</c> = SAPB1, caso em que as tabelas BUSINESS_PARTNERS do
/// Siagro ficam vazias e o dado real vive apenas no banco do SAP.
/// </summary>
public class SapPartnerSource(SapErpDbContext context) : IPartnerSource
{
    public async Task<ReportPartnerDto?> GetByCardCodeAsync(string cardCode, CancellationToken ct = default)
    {
        var partner = await context.BusinessPartners
            .AsNoTracking()
            .Include(b => b.Addresses)
            .FirstOrDefaultAsync(b => b.CardCode == cardCode, ct);

        if (partner is null) return null;

        // No SAP, AdresType "S" corresponde ao endereço FATURAMENTO — filtrar por
        // tipo em vez de pelo nome, que aparece como "FATURAMENTO" e "Faturamento".
        var address = partner.Addresses.FirstOrDefault(a => a.AdresType == "S")
                      ?? partner.Addresses.FirstOrDefault();

        return new ReportPartnerDto
        {
            CardCode = partner.CardCode,
            CardName = partner.CardName,
            TaxId = partner.TaxId,
            Street = address?.Street,
            City = address?.City,
            State = address?.State,
            ZipCode = address?.ZipCode
        };
    }
}
