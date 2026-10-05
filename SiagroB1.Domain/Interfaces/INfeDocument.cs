using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Interfaces;

/// <summary>
/// Cabeçalho de documento que emite NF-e pelo Siagro — o documento de saída e o de entrada. É o que o
/// pipeline da emissão (reserva, assinatura, transmissão, retorno, consulta) lê e escreve. As
/// propriedades já existem nas entidades com estes nomes: a interface não muda o mapeamento do EF nem
/// o EDM.
/// </summary>
public interface INfeDocument
{
    Guid Key { get; }
    string? BranchCode { get; }
    string CardCode { get; }
    InvoiceStatus? InvoiceStatus { get; }
    string? TaxDocumentNumber { get; set; }
    string? TaxDocumentSeries { get; set; }
    string? ChaveNFe { get; set; }
    NfeStatus NfeStatus { get; set; }
    NfeEnvironment? NfeEnvironment { get; set; }
    string? NfeRandomCode { get; set; }
    string? NfeProtocol { get; set; }
    DateTime? NfeAuthorizedAt { get; set; }
    string? NfeStatusCode { get; set; }
    string? NfeStatusReason { get; set; }
    string? NfeConfirmationError { get; set; }
}
