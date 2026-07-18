using Microsoft.EntityFrameworkCore;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;

namespace SiagroB1.Reports.PartnerSources;

/// <summary>
/// Lê o parceiro das tabelas próprias do Siagro (BUSINESS_PARTNERS /
/// BUSINESS_PARTNERS_ADDRESSES). Usada quando <c>Erp</c> = STANDALONE.
/// </summary>
public class StandalonePartnerSource(IUnitOfWork db) : IPartnerSource
{
    public async Task<ReportPartnerDto?> GetByCardCodeAsync(string cardCode, CancellationToken ct = default)
    {
        var partner = await db.Context.BusinessPartners
            .AsNoTracking()
            .Include(b => b.Addresses)
            .FirstOrDefaultAsync(b => b.CardCode == cardCode, ct);

        if (partner is null) return null;

        // AdresType "S" é o endereço de faturamento; cai para o primeiro disponível.
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
