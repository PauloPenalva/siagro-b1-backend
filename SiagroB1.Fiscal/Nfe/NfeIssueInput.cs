using SiagroB1.Domain.Enums;
using SiagroB1.Fiscal.Payments;

namespace SiagroB1.Fiscal.Nfe;

/// <summary>Endereço já resolvido (município do IBGE, CEP e telefone só com dígitos).</summary>
public sealed record NfeAddress
{
    public required string Street { get; init; }
    public required string Number { get; init; }
    public string? Complement { get; init; }
    public required string District { get; init; }
    public required string MunicipalityCode { get; init; }
    public required string MunicipalityName { get; init; }
    public required string State { get; init; }
    public string? ZipCode { get; init; }
    public string? Phone { get; init; }
}

/// <summary>Emitente. <see cref="TaxId"/> só com [0-9A-Z]: 14 = CNPJ, 11 = CPF.</summary>
public sealed record NfeIssuer
{
    public required string TaxId { get; init; }
    public required string LegalName { get; init; }
    public string? TradeName { get; init; }
    public required string StateRegistration { get; init; }
    public required TaxRegime TaxRegime { get; init; }
    public required NfeAddress Address { get; init; }
}

public sealed record NfeRecipient
{
    public required string TaxId { get; init; }
    public required string Name { get; init; }
    public required StateRegistrationIndicator Indicator { get; init; }
    public string? StateRegistration { get; init; }
    public string? Email { get; init; }
    public required NfeAddress Address { get; init; }
}

/// <summary>Local de entrega diferente do destinatário (grupo <c>entrega</c>).</summary>
public sealed record NfeDelivery
{
    public required string TaxId { get; init; }
    public required string Name { get; init; }
    public string? StateRegistration { get; init; }
    public required NfeAddress Address { get; init; }
}

public sealed record NfeCarrier
{
    public string? TaxId { get; init; }
    public required string Name { get; init; }
    public string? StateRegistration { get; init; }
    public string? FullAddress { get; init; }
    public string? MunicipalityName { get; init; }
    public string? State { get; init; }
}

/// <summary>Placa (sem traço, maiúscula) e UF do veículo.</summary>
public sealed record NfeVehicle(string Plate, string State);

/// <summary>
/// Volume da NF-e (grupo <c>vol</c>), informado no documento — opcionais; os pesos vêm à parte
/// (<see cref="NfeIssueInput.NetWeight"/>/<see cref="NfeIssueInput.GrossWeight"/>).
/// </summary>
public sealed record NfeVolume(int? Quantity, string? Species, string? Brand, string? Numbering);

/// <summary><c>infRespTec</c> — os dados da IDX, de <c>Nfe:TechnicalResponsible</c>.</summary>
public sealed record NfeTechnicalResponsible(string Cnpj, string Contact, string Email, string Phone);

/// <summary>Sentido da NF-e (<c>ide/tpNF</c>): saída (1) ou entrada (0).</summary>
public enum NfeDirection
{
    Outgoing,
    Incoming,
}

/// <summary>
/// Finalidade da NF-e (<c>ide/finNFe</c>): normal (1) ou devolução (4). Não decide o sentido: a devolução
/// de venda é entrada e a devolução de compra é saída (<see cref="NfeDirection"/>).
/// </summary>
public enum NfePurpose
{
    Normal,
    Return,
}

/// <summary>Item da nota original que este item devolve (<c>det/DFeReferenciado</c>, regra VC02-14).</summary>
public sealed record NfeItemReference(string AccessKey, int ItemNumber);

/// <summary>Linha do documento com a fotografia dos tributos do sub-projeto 1, já gravada.</summary>
public sealed record NfeItem
{
    public required int Number { get; init; }
    public required string ItemCode { get; init; }
    public required string Description { get; init; }
    public required string Ncm { get; init; }
    public required string Cfop { get; init; }
    public required string UnitOfMeasure { get; init; }
    public required decimal Quantity { get; init; }
    public required decimal UnitPrice { get; init; }
    public required decimal Total { get; init; }
    public required byte GoodsOrigin { get; init; }
    public string? BenefitCode { get; init; }

    /// <summary>CEST (7 dígitos) do cadastro do produto; vazio = a NF-e não leva CEST.</summary>
    public string? Cest { get; init; }

    /// <summary>Na devolução, o item da venda devolvido; nulo na venda.</summary>
    public NfeItemReference? Reference { get; init; }

    /// <summary>CST (2 dígitos) ou CSOSN (3 dígitos) do ICMS.</summary>
    public required string IcmsCode { get; init; }
    public decimal IcmsBase { get; init; }
    public decimal IcmsRate { get; init; }
    public decimal IcmsValue { get; init; }
    public decimal IcmsBaseReduction { get; init; }
    public decimal IcmsDeferral { get; init; }
    public decimal IcmsOperationValue { get; init; }
    public decimal IcmsDeferredValue { get; init; }

    public required string PisCst { get; init; }
    public decimal PisBase { get; init; }
    public decimal PisRate { get; init; }
    public decimal PisValue { get; init; }
    public required string CofinsCst { get; init; }
    public decimal CofinsBase { get; init; }
    public decimal CofinsRate { get; init; }
    public decimal CofinsValue { get; init; }

    public string? IbsCbsCst { get; init; }
    public string? IbsCbsClassCode { get; init; }
    public decimal IbsCbsBase { get; init; }
    public decimal IbsStateRate { get; init; }
    public decimal IbsMunicipalRate { get; init; }
    public decimal IbsRateReduction { get; init; }
    public decimal IbsStateValue { get; init; }
    public decimal IbsMunicipalValue { get; init; }
    public decimal CbsRate { get; init; }
    public decimal CbsRateReduction { get; init; }
    public decimal CbsValue { get; init; }
}

/// <summary>
/// Tudo o que o XML precisa, já resolvido pela Application (<c>NfeIssueInputAssembler</c>). O
/// builder não decide nada de cadastro: só traduz para o modelo da Zeus.
/// </summary>
public sealed record NfeIssueInput
{
    public required NfeEnvironment Environment { get; init; }
    public required int Series { get; init; }
    public required long Number { get; init; }

    /// <summary><c>cNF</c>: 8 dígitos, gerado uma vez e guardado no documento.</summary>
    public required string RandomCode { get; init; }

    public NfeDirection Direction { get; init; } = NfeDirection.Outgoing;

    public NfePurpose Purpose { get; init; } = NfePurpose.Normal;

    /// <summary><c>ide/NFref/refNFe</c> — na devolução, a chave da nota de origem; na entrada própria, a NF-e do produtor. Só vai ao cabeçalho quando nenhum item tem <see cref="NfeItem.Reference"/> (rejeição 1010).</summary>
    public IReadOnlyList<string> ReferencedKeys { get; init; } = [];

    /// <summary><c>dhEmi</c>, já em America/Sao_Paulo.</summary>
    public required DateTimeOffset IssuedAt { get; init; }

    public required string OperationNature { get; init; }
    public required string ApplicationVersion { get; init; }
    public required NfeIssuer Issuer { get; init; }
    public required NfeRecipient Recipient { get; init; }
    public NfeDelivery? Delivery { get; init; }
    public required IReadOnlyList<NfeItem> Items { get; init; }
    public required FreightTerms FreightTerms { get; init; }
    public NfeCarrier? Carrier { get; init; }
    public NfeVehicle? Vehicle { get; init; }
    public decimal NetWeight { get; init; }
    public decimal GrossWeight { get; init; }
    public NfeVolume? Volume { get; init; }
    public required PaymentPlan Payment { get; init; }

    /// <summary><c>cobr/fat/nFat</c> — o número fiscal do documento.</summary>
    public required string BillingNumber { get; init; }

    public string? AdditionalInfo { get; init; }
    public string? FiscoInfo { get; init; }
    public NfeTechnicalResponsible? TechnicalResponsible { get; init; }
}
