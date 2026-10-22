using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Cte;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Cte;

/// <summary>
/// Traduz o <see cref="TransportDocument"/> gravado para o <see cref="CteIssueInput"/> do Fiscal. Não decide nada de
/// cadastro: usa o snapshot das partes, a rota, os valores e a tributação já gravados.
/// </summary>
public static class CteIssueInputAssembler
{
    /// <summary>A Zeus não troca o nome no ambiente de homologação: a SEFAZ exige este texto no destinatário.</summary>
    public const string HomologationName = "CT-E EMITIDO EM AMBIENTE DE HOMOLOGACAO - SEM VALOR FISCAL";

    /// <param name="branch">Com <see cref="Branch.Municipality"/> carregado (cMun/UF do emitente).</param>
    /// <param name="municipalityNames">Nome por código IBGE dos municípios de início/fim da prestação.</param>
    public static CteIssueInput Build(
        TransportDocument doc, Branch branch, BranchCteSettings settings,
        IReadOnlyDictionary<string, string>? municipalityNames = null,
        NfeTechnicalResponsible? technicalResponsible = null)
    {
        if (doc.Number is not { } number || doc.RandomCode is null || doc.IssuedAt is not { } issuedAt)
            throw new DefaultException("Confirme o CT-e antes de transmitir: ele ainda não tem número e chave.");

        var environment = doc.Environment ?? settings.Environment;
        var names = municipalityNames ?? new Dictionary<string, string>();
        var plates = Plates(doc);

        var recipient = Party(doc, CtePartyRole.Recipient)
                        ?? throw new DefaultException("O CT-e não tem destinatário.");
        if (environment == NfeEnvironment.Homologation)
            recipient = recipient with { Name = HomologationName };

        var taker = (CteTaker)doc.TakerRole;
        var takerParty = doc.Parties.FirstOrDefault(p => (int)p.Role == doc.TakerRole);

        return new CteIssueInput
        {
            Environment = environment,
            Series = doc.Series,
            Number = number,
            RandomCode = doc.RandomCode,
            IssuedAt = new DateTimeOffset(issuedAt, NfeIssueInputAssembler.BrasiliaZone.GetUtcOffset(issuedAt)),
            Cfop = doc.Cfop ?? throw new DefaultException("Informe a natureza da operação (CFOP)."),
            OperationNature = Required(doc.NatureText, "PRESTACAO DE SERVICO DE TRANSPORTE"),
            Issuer = new CteIssuer
            {
                TaxId = Required(branch.TaxId, string.Empty).Trim(),
                LegalName = Required(branch.LegalName, branch.BranchName),
                TradeName = branch.TradeName,
                StateRegistration = Required(branch.StateRegistration, string.Empty),
                TaxRegime = branch.TaxRegime ?? TaxRegime.Normal,
                Rntrc = doc.Rntrc ?? string.Empty,
                Address = new NfeAddress
                {
                    Street = branch.Street ?? string.Empty,
                    Number = Required(branch.StreetNumber, "S/N"),
                    Complement = branch.Complement,
                    District = branch.District ?? string.Empty,
                    MunicipalityCode = branch.MunicipalityCode ?? string.Empty,
                    MunicipalityName = branch.Municipality?.Name ?? string.Empty,
                    State = branch.Municipality?.StateAbbreviation ?? branch.StateCode ?? string.Empty,
                    ZipCode = branch.ZipCode,
                    Phone = branch.Phone,
                },
            },
            Sender = Party(doc, CtePartyRole.Sender) ?? throw new DefaultException("O CT-e não tem remetente."),
            Shipper = Party(doc, CtePartyRole.Shipper),
            Receiver = Party(doc, CtePartyRole.Receiver),
            Recipient = recipient,
            Taker = taker,
            OtherTaker = taker == CteTaker.Other ? Party(doc, CtePartyRole.OtherTaker) : null,
            TakerIndicator = takerParty?.IndIE ?? 9,
            Route = new CteRoute(
                doc.StartMunicipalityCode ?? string.Empty, names.GetValueOrDefault(doc.StartMunicipalityCode ?? string.Empty, string.Empty),
                doc.StartState ?? string.Empty,
                doc.EndMunicipalityCode ?? string.Empty, names.GetValueOrDefault(doc.EndMunicipalityCode ?? string.Empty, string.Empty),
                doc.EndState ?? string.Empty),
            Components = doc.Components.Select(c => new CteComponent(c.Name, c.Value)).ToList(),
            ServiceValue = doc.ServiceTotal,
            ReceivableValue = doc.AmountToReceive,
            IcmsCst = doc.IcmsCst ?? string.Empty,
            IcmsBase = doc.IcmsBase,
            IcmsRate = doc.IcmsRate,
            IcmsValue = doc.IcmsValue,
            IcmsBaseReduction = doc.IcmsReduction,
            IbsCbsCst = doc.IbsCbsCst,
            IbsCbsClassCode = doc.IbsCbsClassCode,
            IbsCbsBase = doc.IbsCbsBase,
            IbsStateRate = doc.IbsStateRate,
            IbsStateValue = doc.IbsStateValue,
            IbsMunicipalRate = doc.IbsMunicipalRate,
            IbsMunicipalValue = doc.IbsMunicipalValue,
            CbsRate = doc.CbsRate,
            CbsValue = doc.CbsValue,
            PredominantProduct = doc.PredominantProduct ?? string.Empty,
            CargoValue = doc.CargoValue,
            CargoQuantities = doc.Quantities.Select(q => new CteCargoQuantity(q.UnitCode, q.MeasureType, q.Quantity)).ToList(),
            NfeKeys = doc.NfeKeys.OrderBy(k => k.Order).Select(k => k.AccessKey).ToList(),
            Observations = Observations(doc, plates),
            Remarks = Remarks(doc, plates),
            TechnicalResponsible = technicalResponsible,
        };
    }

    /// <summary>Nomes dos municípios da rota, para o <c>xMunIni</c>/<c>xMunFim</c>.</summary>
    public static async Task<IReadOnlyDictionary<string, string>> MunicipalityNamesAsync(IUnitOfWork db, TransportDocument doc)
    {
        var codes = new[] { doc.StartMunicipalityCode, doc.EndMunicipalityCode }.Where(c => c is not null).ToList();
        return await db.Context.Municipalities.AsNoTracking().Where(m => codes.Contains(m.Code))
            .ToDictionaryAsync(m => m.Code, m => m.Name);
    }

    /// <summary>UF do emitente: a do município da filial (o cMun do XML) vale mais que o campo UF, como na confirmação.</summary>
    public static async Task<string> IssuerStateAsync(IUnitOfWork db, string branchCode)
    {
        var branch = await db.Context.Branchs.AsNoTracking().Include(b => b.Municipality).FirstOrDefaultAsync(b => b.Code == branchCode)
                     ?? throw new DefaultException($"Filial {branchCode} não encontrada.");
        var state = branch.Municipality?.StateAbbreviation ?? branch.StateCode;
        return string.IsNullOrWhiteSpace(state) ? throw new DefaultException($"A filial {branchCode} está sem UF.") : state.Trim();
    }

    private static string Plates(TransportDocument doc) =>
        string.Join(", ", new[] { doc.TractionPlate }
            .Concat((doc.TrailerPlates ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(p => !string.IsNullOrWhiteSpace(p)));

    private static List<CteObservation> Observations(TransportDocument doc, string plates)
    {
        var list = new List<CteObservation>();
        void Add(string field, string? text)
        {
            if (!string.IsNullOrWhiteSpace(text)) list.Add(new CteObservation(field, text.Trim()));
        }

        Add("PLACA", plates);
        Add("CPFMOTORISTA", doc.DriverCpf);
        Add("NOMEMOTORISTA", doc.DriverName);
        Add("RASTREADO", doc.Tracked ? "SIM" : "NAO");
        Add("TranspProprio", doc.OwnTransport ? "SIM" : "NAO");
        return list;
    }

    private static string Remarks(TransportDocument doc, string plates)
    {
        var text = $"Motorista: {doc.DriverName} | CPF: {doc.DriverCpf} | Placa: {plates}";
        return string.IsNullOrWhiteSpace(doc.Observation) ? text : $"{text} | {doc.Observation.Trim()}";
    }

    private static CteParty? Party(TransportDocument doc, CtePartyRole role)
    {
        var p = doc.Parties.FirstOrDefault(x => x.Role == role);
        if (p is null)
            return null;

        return new CteParty
        {
            TaxId = p.TaxId ?? string.Empty,
            Name = p.Name ?? string.Empty,
            StateRegistration = p.StateRegistration,
            Email = p.Email,
            Address = new NfeAddress
            {
                Street = p.Street ?? string.Empty,
                Number = Required(p.Number, "S/N"),
                Complement = p.Complement,
                District = p.Neighborhood ?? string.Empty,
                MunicipalityCode = p.MunicipalityCode ?? string.Empty,
                MunicipalityName = p.MunicipalityName ?? string.Empty,
                State = p.State ?? string.Empty,
                ZipCode = p.ZipCode,
                Phone = p.Phone,
            },
        };
    }

    private static string Required(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;
}
