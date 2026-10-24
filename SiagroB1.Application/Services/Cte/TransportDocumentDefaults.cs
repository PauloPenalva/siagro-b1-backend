using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Cte;

/// <summary>Parte informada na tela: papel, parceiro e (opcional) o nome do endereço; vazio = faturamento.</summary>
public sealed record TransportDocumentPartyInput(CtePartyRole Role, string CardCode, string? AddressName = null);

/// <summary>Quantidade da carga (infQ): unidade 00..05, tipo da medida e valor.</summary>
public sealed record TransportDocumentQuantityInput(string UnitCode, string MeasureType, decimal Quantity);

public sealed record TransportDocumentComponentInput(string Name, decimal Value);

/// <summary>Corpo da inclusão/edição do CT-e. Rota e seguro vazios são sugeridos/preenchidos pelo padrão.</summary>
public sealed record TransportDocumentInput
{
    public Guid? Key { get; init; }
    public required string BranchCode { get; init; }
    public int Series { get; init; }
    public int? UsageCode { get; init; }
    public int TakerRole { get; init; }
    public IReadOnlyList<TransportDocumentPartyInput> Parties { get; init; } = [];
    public string? StartMunicipalityCode { get; init; }
    public string? EndMunicipalityCode { get; init; }
    public IReadOnlyList<string> NfeKeys { get; init; } = [];
    public string? PredominantProduct { get; init; }
    public decimal CargoValue { get; init; }
    public IReadOnlyList<TransportDocumentQuantityInput> Quantities { get; init; } = [];
    public IReadOnlyList<TransportDocumentComponentInput> Components { get; init; } = [];
    public string? TruckCombinationCode { get; init; }
    public string? TractionPlate { get; init; }
    public string? DriverCpf { get; init; }
    public string? InsuranceResponsible { get; init; }
    public string? InsurerTaxId { get; init; }
    public string? InsurerName { get; init; }
    public string? PolicyNumber { get; init; }
    public bool Tracked { get; init; } = true;
    public bool OwnTransport { get; init; } = true;
    public string? Observation { get; init; }
    public string? CostCenterCode { get; init; }
    public string? AccountingItemCode { get; init; }
    public string? DistributionRuleCode { get; init; }
    /// <summary>Condição de pagamento do título; vazia = a do cadastro do tomador.</summary>
    public int? PaymentConditionCode { get; init; }
    /// <summary>Ordem de frete vinculada (opcional); precisa estar Aberta/Em execução e ser do mesmo tomador.</summary>
    public Guid? FreightOrderKey { get; init; }
}

/// <summary>Resultado da gravação: a chave do CT-e e os avisos (não bloqueiam) que a tela mostra.</summary>
public sealed record TransportDocumentSaveResult(Guid Key, IReadOnlyList<string> Warnings);

/// <summary>Padrões do CT-e: rota sugerida, indicador de IE, snapshot do parceiro, seguro e unidade da filial.</summary>
public class TransportDocumentDefaults(IUnitOfWork db, IBusinessPartnerService partners)
{
    /// <summary>Início = expedidor, senão remetente; fim = recebedor, senão destinatário.</summary>
    public static (string? Start, string? End) SuggestRoute(IEnumerable<TransportDocumentParty> parties)
    {
        var list = parties.ToList();
        string? Of(CtePartyRole role) => list.FirstOrDefault(p => p.Role == role)?.MunicipalityCode;
        return (Of(CtePartyRole.Shipper) ?? Of(CtePartyRole.Sender), Of(CtePartyRole.Receiver) ?? Of(CtePartyRole.Recipient));
    }

    /// <summary>1 contribuinte (tem IE), 2 isento ("ISENTO"), 9 não contribuinte (sem IE).</summary>
    public static int IndIE(BusinessPartnerModel partner)
    {
        var ie = partner.StateRegistration?.Trim();
        if (string.IsNullOrEmpty(ie)) return 9;
        return string.Equals(ie, "ISENTO", StringComparison.OrdinalIgnoreCase) ? 2 : 1;
    }

    public async Task<BusinessPartnerModel> LoadPartnerAsync(string cardCode) =>
        await partners.GetByIdAsync(cardCode) ?? throw new DefaultException($"Parceiro {cardCode} não encontrado.");

    /// <summary>Copia o parceiro para a parte do CT-e; sem município (IBGE) recusa quando <paramref name="requireIbge"/>.</summary>
    public async Task<TransportDocumentParty> SnapshotPartyAsync(CtePartyRole role, string cardCode, string? addressName,
        bool requireIbge = true)
    {
        var partner = await LoadPartnerAsync(cardCode);
        var address = (!string.IsNullOrWhiteSpace(addressName)
                          ? partner.Addresses.FirstOrDefault(a => a.AddressName == addressName)
                          : null)
                      ?? partner.Addresses.FirstOrDefault(a => a.AdresType == "B")
                      ?? partner.Addresses.FirstOrDefault();

        var ibge = address?.MunicipalityCode?.Trim();
        if (string.IsNullOrEmpty(ibge) && requireIbge)
            throw new DefaultException($"O endereço de {partner.CardName} está sem município (IBGE).");

        var municipality = string.IsNullOrEmpty(ibge)
            ? null
            : await db.Context.Municipalities.AsNoTracking().FirstOrDefaultAsync(m => m.Code == ibge);

        return new TransportDocumentParty
        {
            Role = role,
            CardCode = partner.CardCode,
            Name = Cut(partner.CardName, 60),
            TaxId = Cut(Digits(partner.TaxId), 14),
            StateRegistration = Cut(partner.StateRegistration?.Trim(), 14),
            IndIE = IndIE(partner),
            Street = Cut(address?.Street, 60),
            Number = Cut(address?.StreetNumber, 60),
            Complement = Cut(address?.Complement, 60),
            Neighborhood = Cut(address?.Block, 60),
            MunicipalityCode = string.IsNullOrEmpty(ibge) ? null : ibge,
            MunicipalityName = Cut(municipality?.Name ?? address?.City, 60),
            State = municipality?.StateAbbreviation ?? address?.State,
            ZipCode = Cut(Digits(address?.ZipCode), 8),
            Phone = Cut(Digits(partner.Phone), 14),
            Email = Cut(partner.NfeEmails.FirstOrDefault(), 100),
        };
    }

    /// <summary>UF do município IBGE (tabela MUNICIPALITIES); null se o código não existe.</summary>
    public async Task<string?> StateOfAsync(string? municipalityCode) =>
        string.IsNullOrWhiteSpace(municipalityCode)
            ? null
            : (await db.Context.Municipalities.AsNoTracking().FirstOrDefaultAsync(m => m.Code == municipalityCode))
              ?.StateAbbreviation;

    public Task<BranchTransportSettings?> TransportSettingsAsync(string branchCode) =>
        db.Context.BranchTransportSettings.AsNoTracking().FirstOrDefaultAsync(x => x.BranchCode == branchCode);

    /// <summary>Unidade padrão da quantidade da filial ("00" M3 quando não configurada).</summary>
    public async Task<string> DefaultUnitAsync(string branchCode) =>
        (await db.Context.BranchCteSettings.AsNoTracking().FirstOrDefaultAsync(x => x.BranchCode == branchCode))
        ?.DefaultQuantityUnit ?? "00";

    public async Task<int> DefaultSeriesAsync(string branchCode) =>
        (await db.Context.BranchCteSettings.AsNoTracking().FirstOrDefaultAsync(x => x.BranchCode == branchCode))
        ?.DefaultSeries ?? 1;

    /// <summary>Tipo da medida sugerido pela unidade.</summary>
    public static string MeasureTypeFor(string unitCode) => unitCode switch
    {
        "01" or "02" => "PESO BRUTO",
        "03" => "UNIDADE",
        "04" => "LITRAGEM",
        "05" => "MMBTU",
        _ => "VOLUME",
    };

    /// <summary>Seguro: o campo vazio do documento herda o padrão da filial.</summary>
    public static void ApplyInsuranceDefaults(TransportDocument doc, BranchTransportSettings? settings)
    {
        if (settings is null) return;
        if (string.IsNullOrWhiteSpace(doc.InsuranceResponsible) && settings.InsuranceResponsible is { } responsible)
            doc.InsuranceResponsible = ((int)responsible).ToString();
        if (string.IsNullOrWhiteSpace(doc.InsurerTaxId)) doc.InsurerTaxId = settings.InsurerTaxId;
        if (string.IsNullOrWhiteSpace(doc.InsurerName)) doc.InsurerName = Cut(settings.InsurerName, 30);
        if (string.IsNullOrWhiteSpace(doc.PolicyNumber)) doc.PolicyNumber = settings.PolicyNumber;
    }

    internal static string? Digits(string? value) =>
        value is null ? null : new string(value.Where(char.IsAsciiLetterOrDigit).ToArray()).ToUpperInvariant();

    internal static string? Cut(string? value, int max) =>
        value is null ? null : value.Length > max ? value[..max] : value;
}
