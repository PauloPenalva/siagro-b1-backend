using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities.SAP;
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
        // Entre os do tipo "S", o endereço padrão do parceiro (ShipToDef) tem precedência.
        var address = partner.Addresses.FirstOrDefault(a => a.AdresType == "S" && a.AddressName == partner.ShipToDef)
                      ?? partner.Addresses.FirstOrDefault(a => a.AdresType == "S")
                      ?? partner.Addresses.FirstOrDefault();

        // CRD1.County é varchar com o AbsId de OCNT dentro; se não for numérico, ignoramos
        // e o município cai no texto livre de CRD1.City.
        var county = int.TryParse(address?.County, out var countyId)
            ? await context.Set<County>()
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.AbsId == countyId, ct)
            : null;

        // CNPJ/CPF/IE vivem em CRD7, uma linha por endereço. A escolha da linha segue a
        // prioridade da query homologada pelo cliente (ver SapPartnerMapper).
        var taxExtensions = await context.Set<AddressTaxExtension>()
            .AsNoTracking()
            .Where(x => x.CardCode == cardCode && x.AddressType == "S")
            .ToListAsync(ct);

        var fiscal = SapPartnerMapper.SelectFiscalAddress(taxExtensions, partner.ShipToDef);

        // Sócios administradores e contato para envio saem das pessoas de contato (OCPR),
        // classificadas pelo cargo digitado à mão.
        var contacts = await context.Set<ContactPerson>()
            .AsNoTracking()
            .Where(c => c.CardCode == cardCode)
            .ToListAsync(ct);

        return new ReportPartnerDto
        {
            CardCode = partner.CardCode,
            CardName = partner.CardName,
            TaxId = partner.TaxId,
            Street = address?.Street,
            City = address?.City,
            State = address?.State,
            ZipCode = address?.ZipCode,
            Cnpj = fiscal?.Cnpj,
            Cpf = fiscal?.Cpf,
            StateRegistration = fiscal?.StateRegistration,
            FullAddress = SapPartnerMapper.BuildFullAddress(address, county),
            ManagingPartners = SapPartnerMapper.BuildManagingPartners(contacts),
            ContractContact = SapPartnerMapper.BuildContractContact(contacts)
        };
    }
}
