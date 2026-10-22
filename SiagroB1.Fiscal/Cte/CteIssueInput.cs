using SiagroB1.Domain.Enums;
using SiagroB1.Fiscal.Nfe;

namespace SiagroB1.Fiscal.Cte;

/// <summary>Emitente do CT-e (a transportadora). <see cref="Rntrc"/> vai no modal rodoviário.</summary>
public sealed record CteIssuer
{
    public required string TaxId { get; init; }
    public required string LegalName { get; init; }
    public string? TradeName { get; init; }
    public required string StateRegistration { get; init; }
    public required TaxRegime TaxRegime { get; init; }
    public required string Rntrc { get; init; }
    public required NfeAddress Address { get; init; }
}

/// <summary>
/// Remetente, expedidor, recebedor, destinatário ou tomador "outros". <see cref="TaxId"/>:
/// 14 = CNPJ, 11 = CPF. IE vazia = sem IE (o XML omite).
/// </summary>
public sealed record CteParty
{
    public required string TaxId { get; init; }
    public required string Name { get; init; }
    public string? TradeName { get; init; }
    public string? StateRegistration { get; init; }
    public string? Email { get; init; }
    public required NfeAddress Address { get; init; }
}

/// <summary>Tomador do serviço; o valor é o código do XML (<c>toma</c>).</summary>
public enum CteTaker
{
    Sender = 0,
    Shipper = 1,
    Receiver = 2,
    Recipient = 3,
    Other = 4,
}

/// <summary>Início e fim da prestação (IBGE de 7 dígitos e UF).</summary>
public sealed record CteRoute(
    string StartMunicipalityCode, string StartMunicipalityName, string StartState,
    string EndMunicipalityCode, string EndMunicipalityName, string EndState);

/// <summary>Componente do valor da prestação (<c>vPrest/Comp</c>).</summary>
public sealed record CteComponent(string Name, decimal Value);

/// <summary><c>infQ</c>: <paramref name="UnitCode"/> é o <c>cUnid</c> do XML ("00" M3 … "05" MMBTU).</summary>
public sealed record CteCargoQuantity(string UnitCode, string MeasureType, decimal Quantity);

/// <summary><c>ObsCont</c>: observação livre do contribuinte (campo/texto).</summary>
public sealed record CteObservation(string Field, string Text);

/// <summary>
/// Tudo o que o XML do CT-e normal rodoviário precisa, já resolvido pela Application. O builder
/// não decide nada de cadastro: só traduz para o modelo da Zeus.
/// </summary>
public sealed record CteIssueInput
{
    public required NfeEnvironment Environment { get; init; }
    public required int Series { get; init; }
    public required int Number { get; init; }

    /// <summary><c>cCT</c>, 8 dígitos.</summary>
    public required string RandomCode { get; init; }

    public required DateTimeOffset IssuedAt { get; init; }
    public required string Cfop { get; init; }
    public required string OperationNature { get; init; }

    public required CteIssuer Issuer { get; init; }
    public required CteParty Sender { get; init; }
    public CteParty? Shipper { get; init; }
    public CteParty? Receiver { get; init; }
    public required CteParty Recipient { get; init; }

    public required CteTaker Taker { get; init; }

    /// <summary>Obrigatório quando <see cref="Taker"/> é <see cref="CteTaker.Other"/>.</summary>
    public CteParty? OtherTaker { get; init; }

    /// <summary><c>indIEToma</c>: 1 contribuinte, 2 isento de inscrição, 9 não contribuinte.</summary>
    public required int TakerIndicator { get; init; }

    public required CteRoute Route { get; init; }

    public required IReadOnlyList<CteComponent> Components { get; init; }
    public required decimal ServiceValue { get; init; }
    public required decimal ReceivableValue { get; init; }

    /// <summary>CST do ICMS: "00", "20", "40", "41", "51" ou "90" (outros).</summary>
    public required string IcmsCst { get; init; }
    public decimal IcmsBase { get; init; }
    public decimal IcmsRate { get; init; }
    public decimal IcmsValue { get; init; }
    public decimal IcmsBaseReduction { get; init; }

    public string? IbsCbsCst { get; init; }
    public string? IbsCbsClassCode { get; init; }
    public decimal IbsCbsBase { get; init; }
    public decimal IbsStateRate { get; init; }
    public decimal IbsStateValue { get; init; }
    public decimal IbsMunicipalRate { get; init; }
    public decimal IbsMunicipalValue { get; init; }
    public decimal CbsRate { get; init; }
    public decimal CbsValue { get; init; }

    public required string PredominantProduct { get; init; }
    public required decimal CargoValue { get; init; }
    public required IReadOnlyList<CteCargoQuantity> CargoQuantities { get; init; }

    public required IReadOnlyList<string> NfeKeys { get; init; }

    public IReadOnlyList<CteObservation> Observations { get; init; } = [];
    public string? Remarks { get; init; }

    /// <summary>CNPJ/CPF autorizados a baixar o XML (<c>autXML</c>).</summary>
    public IReadOnlyList<string> AuthorizedXmlTaxIds { get; init; } = [];

    public NfeTechnicalResponsible? TechnicalResponsible { get; init; }
}
