using SiagroB1.Domain.Entities;
using SiagroB1.Fiscal.Nfe;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>Cadastro já carregado e conferido pela prontidão — tudo o que a montagem da entrada usa.</summary>
/// <param name="Customer">
/// O destinatário da NF-e (<c>dest</c>): o cliente no documento de saída, o fornecedor no de entrada.
/// <c>CustomerAddress</c> e <c>CustomerMunicipality</c> são o endereço de faturamento dele.
/// </param>
/// <param name="PaymentCondition">Nula só na devolução própria, que não tem pagamento (tPag 90).</param>
/// <param name="ReturnOrigin">Na devolução própria, a venda referenciada; nula na venda.</param>
/// <param name="ProducerInvoice">Na contra-nota, a escrituração do produtor (NFref e Funrural); nula nos demais.</param>
/// <param name="CustodyReferencedKey">Na devolução simbólica do beneficiamento, a chave da entrada (NFref; spec 2026-10-12 §4.3).</param>
/// <param name="TaxableUnits">Unidade tributável de exportação por ItemCode (complemento do produto); só os itens configurados.</param>
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
    NfeReturnOrigin? ReturnOrigin = null,
    NfeCounterNoteOrigin? ProducerInvoice = null,
    string? CustodyReferencedKey = null,
    IReadOnlyDictionary<string, NfeTaxableUnitSetting>? TaxableUnits = null);

/// <summary>
/// A venda que a devolução própria referencia, já conferida: a chave vai no <c>NFref</c> e, com o
/// número de cada item vendido (<c>ItemNumbers</c>, pela chave do item da venda), no
/// <c>DFeReferenciado</c> de cada item.
/// </summary>
public sealed record NfeReturnOrigin(
    string AccessKey, string Number, string Series, DateTime? IssuedOn, IReadOnlyDictionary<Guid, int> ItemNumbers);

/// <summary>
/// A escrituração da NF-e do produtor que a contra-nota referencia (spec 2026-10-10 §6): a chave vai no
/// <c>ide/NFref/refNFe</c> e o Funrural dela no <c>infCpl</c> (a contra-nota não retém).
/// </summary>
public sealed record NfeCounterNoteOrigin(string AccessKey, string? FunruralText);
