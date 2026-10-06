using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Interfaces;

namespace SiagroB1.Domain.Dtos.Nfe;

/// <summary>
/// Desfecho de emitir/consultar/concluir: a tela mostra a situação e o motivo. Rejeição e falta
/// de resposta são desfechos (200), não erro de requisição.
/// </summary>
public class NfeIssueOutcomeDto
{
    public NfeStatus NfeStatus { get; set; }
    public InvoiceStatus? InvoiceStatus { get; set; }
    public string? StatusCode { get; set; }
    public string? Reason { get; set; }
    public string? AccessKey { get; set; }
    public string? ConfirmationError { get; set; }
    public string? CancellationError { get; set; }

    public static NfeIssueOutcomeDto From(INfeDocument invoice) => new()
    {
        NfeStatus = invoice.NfeStatus,
        InvoiceStatus = invoice.InvoiceStatus,
        StatusCode = invoice.NfeStatusCode,
        Reason = invoice.NfeStatusReason,
        AccessKey = invoice.ChaveNFe,
        ConfirmationError = invoice.NfeConfirmationError,
        CancellationError = invoice.NfeCancellationError,
    };
}
