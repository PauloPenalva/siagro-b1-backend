using SiagroB1.Domain.Enums;

namespace SiagroB1.Fiscal.Reinf.Events;

public enum R1000Operation { Include, Update, Delete }

/// <summary>Dados comuns do evento: ambiente (tpAmb), raiz do CNPJ do contribuinte e versão do aplicativo.</summary>
public sealed record ReinfHeader(ReinfEnvironment Environment, string TaxIdRoot, string AppVersion);

/// <summary>Software house informada no R-1000.</summary>
public sealed record SoftwareHouse(string Cnpj, string Name, string Contact, string? Phone, string? Email);

public sealed record R1000Input(
    ReinfHeader H, R1000Operation Operation, string ValidFrom, string TaxClassification,
    bool KeepsBookkeeping, bool PayrollRelief, bool FineExemptionAgreement, int? CompanySituation,
    string ContactName, string ContactCpf, string? ContactPhone, string? ContactEmail,
    SoftwareHouse SoftHouse);

public sealed record R2055Detail(
    int AcquisitionIndicator, decimal Gross, decimal Inss, decimal Rat, decimal Senar,
    string? JudicialProcess, string? SuspensionCode, decimal InssNotWithheld, decimal RatNotWithheld);

public sealed record R2055Input(
    ReinfHeader H, string Period, int RectificationIndicator, string? PreviousReceipt,
    string EstablishmentTaxId, int ProducerInscriptionType, string ProducerTaxId,
    bool PayrollOption, IReadOnlyList<R2055Detail> Details);

public sealed record R2099Input(
    ReinfHeader H, string Period, string RespName, string RespCpf, string? RespPhone, string? RespEmail,
    bool HasAcquisitions);

public sealed record R2098Input(ReinfHeader H, string Period);

public sealed record R9000Input(ReinfHeader H, string EventType, string Receipt, string Period);
