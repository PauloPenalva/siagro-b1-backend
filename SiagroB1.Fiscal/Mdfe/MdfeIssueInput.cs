using SiagroB1.Domain.Enums;
using SiagroB1.Fiscal.Nfe;

namespace SiagroB1.Fiscal.Mdfe;

/// <summary>Emitente do MDF-e (a transportadora). <see cref="Rntrc"/> vai em <c>infANTT</c>.</summary>
public sealed record MdfeIssuer
{
    public required string TaxId { get; init; }
    public required string StateRegistration { get; init; }
    public required string LegalName { get; init; }
    public string? TradeName { get; init; }
    public required string Rntrc { get; init; }
    public required NfeAddress Address { get; init; }
}

/// <summary>Município de carregamento (<c>infMunCarrega</c>), IBGE de 7 dígitos.</summary>
public sealed record MdfeLoadingCity(string IbgeCode, string Name);

/// <summary>Município de descarregamento (<c>infMunDescarga</c>) com as chaves dos CT-e entregues nele.</summary>
public sealed record MdfeUnloadingCity(string IbgeCode, string Name, IReadOnlyList<string> CteKeys);

/// <summary>
/// Veículo de tração ou reboque. <see cref="BodyType"/> é o <c>tpCar</c> ("00" não aplicável …
/// "05" sider); <see cref="WheelType"/> é o <c>tpRod</c> ("01" truck … "06" outros), só na tração.
/// <see cref="Uf"/> é a sigla de licenciamento.
/// </summary>
public sealed record MdfeVehicle(
    string Plate, string? Renavam, int TareKg, int CapacityKg, int CapacityM3,
    string BodyType, string WheelType, string Uf);

/// <summary>Condutor (CPF de 11 dígitos).</summary>
public sealed record MdfeDriver(string Cpf, string Name);

/// <summary><c>infCIOT</c>: <see cref="ResponsibleTaxId"/> 14 = CNPJ, 11 = CPF.</summary>
public sealed record MdfeCiot(string Number, string ResponsibleTaxId);

/// <summary><c>infContratante</c>: <see cref="TaxId"/> 14 = CNPJ, 11 = CPF.</summary>
public sealed record MdfeContractor(string TaxId, string Name);

/// <summary>Vale-pedágio (<c>valePed/disp</c>): fornecedor, nº do comprovante e valor.</summary>
public sealed record MdfeTollVoucher(string SupplierCnpj, string Number, decimal Value);

/// <summary>
/// Componente do pagamento do frete (<c>infPag/Comp</c>). <see cref="Type"/> é o <c>tpComp</c>:
/// "01" vale-pedágio, "02" impostos, "03" despesas, "99" outros (exige <see cref="Description"/>).
/// ⚠️ O XSD copiado não aceita "04" (frete): use "99".
/// </summary>
public sealed record MdfePaymentComponent(string Type, decimal Value, string? Description = null);

/// <summary>Parcela do pagamento a prazo (<c>infPrazo</c>).</summary>
public sealed record MdfePaymentInstallment(int Number, DateOnly DueDate, decimal Value);

/// <summary>
/// <c>infPag</c>: contratante, componentes e total, <see cref="IndPag"/> 0 (à vista) / 1 (a prazo,
/// com <see cref="Installments"/>) e o meio de pagamento: <see cref="Pix"/> ou banco e agência.
/// </summary>
public sealed record MdfePayment
{
    public required MdfeContractor Contractor { get; init; }
    public required IReadOnlyList<MdfePaymentComponent> Components { get; init; }
    public required decimal Total { get; init; }
    public required int IndPag { get; init; }
    public IReadOnlyList<MdfePaymentInstallment> Installments { get; init; } = [];
    public string? Pix { get; init; }
    public string? BankCode { get; init; }
    public string? Agency { get; init; }
}

/// <summary>
/// Seguro da carga (<c>seg</c>). <see cref="Responsible"/> é o <c>respSeg</c>: 1 emitente, 2
/// contratante (exige <see cref="ResponsibleTaxId"/>).
/// </summary>
public sealed record MdfeInsurance(
    int Responsible, string? ResponsibleTaxId, string InsurerName, string InsurerCnpj,
    string PolicyNumber, IReadOnlyList<string> Endorsements);

/// <summary>
/// Produto predominante (<c>prodPred</c>). <see cref="CargoType"/> é o <c>tpCarga</c> ("01" granel
/// sólido …). Os CEPs alimentam <c>infLotacao</c> quando o MDF-e leva um único CT-e.
/// </summary>
public sealed record MdfePredominantProduct(
    string CargoType, string Description, string? Ncm = null, string? LoadingZip = null, string? UnloadingZip = null);

/// <summary>Totais (<c>tot</c>): <see cref="WeightUnit"/> "01" KG / "02" TON.</summary>
public sealed record MdfeTotals(int CteCount, decimal CargoValue, string WeightUnit, decimal GrossWeight);

/// <summary>
/// Tudo o que o XML do MDF-e rodoviário (emitente prestador de serviço, tpEmit 1, emissão normal)
/// precisa, já resolvido pela Application. O builder só traduz para o modelo da Zeus.
/// </summary>
public sealed record MdfeIssueInput
{
    public required MdfeIssuer Issuer { get; init; }
    public required NfeEnvironment Environment { get; init; }
    public required int Series { get; init; }
    public required int Number { get; init; }

    /// <summary><c>cMDF</c>, 8 dígitos.</summary>
    public required string RandomCode { get; init; }

    public required DateTimeOffset IssuedAt { get; init; }
    public required string UfStart { get; init; }
    public required string UfEnd { get; init; }

    /// <summary>UFs de percurso entre início e fim, na ordem (sem as UFs de início e fim).</summary>
    public IReadOnlyList<string> RouteUfs { get; init; } = [];

    public required IReadOnlyList<MdfeLoadingCity> LoadingCities { get; init; }
    public required IReadOnlyList<MdfeUnloadingCity> UnloadingCities { get; init; }

    public required MdfeVehicle Traction { get; init; }
    public IReadOnlyList<MdfeVehicle> Trailers { get; init; } = [];
    public required IReadOnlyList<MdfeDriver> Drivers { get; init; }

    public MdfeCiot? Ciot { get; init; }
    public IReadOnlyList<MdfeContractor> Contractors { get; init; } = [];
    public MdfeTollVoucher? TollVoucher { get; init; }
    public MdfePayment? Payment { get; init; }

    public MdfeInsurance? Insurance { get; init; }
    public required MdfePredominantProduct Product { get; init; }
    public required MdfeTotals Totals { get; init; }

    public IReadOnlyList<string> Seals { get; init; } = [];
    public string? AdditionalInfo { get; init; }
    public NfeTechnicalResponsible? InfRespTec { get; init; }

    /// <summary>
    /// Ponto de gancho da homologação: se informado, substitui o <c>xNome</c> do emitente (ex.: a
    /// frase "MDF-E EMITIDO EM AMBIENTE DE HOMOLOGACAO - SEM VALOR FISCAL", caso a SEFAZ exija).
    /// Nulo por padrão — o nome do emitente vai como está.
    /// </summary>
    public string? IssuerNameOverride { get; init; }
}
