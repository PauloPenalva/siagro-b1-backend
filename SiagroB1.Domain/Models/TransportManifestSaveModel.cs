namespace SiagroB1.Domain.Models;

/// <summary>
/// Campos editáveis do MDF-e em rascunho. O que vem dos CT-e (UFs, carregamento, descarga, tração, condutor
/// principal, seguro, produto e valor) não se edita aqui: muda escolhendo outros CT-e.
/// </summary>
public class TransportManifestSaveModel
{
    public List<string> RouteUfs { get; set; } = [];
    public List<TransportManifestDriverModel> AdditionalDrivers { get; set; } = [];
    /// <summary>Placas dos reboques; os dados do veículo são copiados do cadastro.</summary>
    public List<string> TrailerPlates { get; set; } = [];
    public List<string> Seals { get; set; } = [];
    public string? LoadingZip { get; set; }
    public string? UnloadingZip { get; set; }
    /// <summary>Peso bruto; 0 = não informado.</summary>
    public decimal GrossWeight { get; set; }
    /// <summary>"01" KG, "02" TON.</summary>
    public string WeightUnit { get; set; } = "01";
    public string? CiotNumber { get; set; }
    public string? CiotResponsibleTaxId { get; set; }
    public List<TransportManifestContractorModel> Contractors { get; set; } = [];
    public string? TollSupplierCnpj { get; set; }
    public string? TollVoucherNumber { get; set; }
    public decimal? TollValue { get; set; }
    public List<TransportManifestPaymentModel> Payments { get; set; } = [];
    public string? AdditionalInfo { get; set; }
}

public class TransportManifestDriverModel
{
    public string Cpf { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public class TransportManifestContractorModel
{
    public string TaxId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public class TransportManifestPaymentModel
{
    public string ContractorTaxId { get; set; } = string.Empty;
    public string ContractorName { get; set; } = string.Empty;
    public decimal Total { get; set; }
    /// <summary>0 à vista, 1 a prazo.</summary>
    public int IndPag { get; set; }
    public string? Pix { get; set; }
    public string? BankCode { get; set; }
    public string? Agency { get; set; }
    public List<TransportManifestPaymentComponentModel> Components { get; set; } = [];
    public List<TransportManifestPaymentInstallmentModel> Installments { get; set; } = [];
}

public class TransportManifestPaymentComponentModel
{
    public string Type { get; set; } = "99";
    public decimal Value { get; set; }
    public string? Description { get; set; }
}

public class TransportManifestPaymentInstallmentModel
{
    public int Number { get; set; }
    public DateOnly DueDate { get; set; }
    public decimal Value { get; set; }
}
