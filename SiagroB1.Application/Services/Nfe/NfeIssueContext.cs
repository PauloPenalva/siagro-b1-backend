using SiagroB1.Domain.Entities;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>Cadastro já carregado e conferido pela prontidão — tudo o que a montagem da entrada usa.</summary>
public sealed record NfeIssueContext(
    Branch Branch,
    Municipality BranchMunicipality,
    BranchNfeSettings Settings,
    BusinessPartner Customer,
    Address CustomerAddress,
    Municipality CustomerMunicipality,
    BusinessPartner? DeliveryPartner,
    Address? DeliveryAddress,
    Municipality? DeliveryMunicipality,
    BusinessPartner? Carrier,
    Address? CarrierAddress,
    string? TruckPlate,
    string? TruckState,
    PaymentCondition PaymentCondition,
    IReadOnlyDictionary<int, Usage> Usages,
    IReadOnlyDictionary<string, string?> ItemCests);
