using SiagroB1.Domain.Entities;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>Cadastro já carregado e conferido pela prontidão — tudo o que a montagem da entrada usa.</summary>
/// <param name="Customer">
/// O destinatário da NF-e (<c>dest</c>): o cliente no documento de saída, o fornecedor no de entrada.
/// <c>CustomerAddress</c> e <c>CustomerMunicipality</c> são o endereço de faturamento dele.
/// </param>
/// <param name="PaymentCondition">Nula só na devolução própria, que não tem pagamento (tPag 90).</param>
/// <param name="ReturnOrigin">Na devolução própria, a venda referenciada; nula na venda.</param>
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
    PaymentCondition? PaymentCondition,
    IReadOnlyDictionary<int, Usage> Usages,
    IReadOnlyDictionary<string, string?> ItemCests,
    NfeReturnOrigin? ReturnOrigin = null);

/// <summary>
/// A venda que a devolução própria referencia, já conferida: a chave vai no <c>NFref</c> e, com o
/// número de cada item vendido (<c>ItemNumbers</c>, pela chave do item da venda), no
/// <c>DFeReferenciado</c> de cada item.
/// </summary>
public sealed record NfeReturnOrigin(
    string AccessKey, string Number, string Series, DateTime? IssuedOn, IReadOnlyDictionary<Guid, int> ItemNumbers);
