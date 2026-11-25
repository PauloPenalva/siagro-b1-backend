using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SalesInvoices;

/// <summary>
/// Ciclo de vida do registro de divergência do vínculo com NF do SAP (GAC-1226) quando o documento deixa de valer.
/// </summary>
/// <remarks>
/// O documento cancelado já sai do termo de divergência da carga pelo status; a anulação é a trilha de auditoria
/// (quem, quando e por quê) e libera o índice único filtrado de registro Open por documento.
/// </remarks>
public static class SalesInvoiceDivergenceRules
{
    public const string CancelledReason = "Documento de saída cancelado.";
    public const string UnlinkedReason = "Notas fiscais do SAP desvinculadas da carga.";

    /// <summary>Marca Voided os registros Open do documento (rastreado, sem SaveChanges).</summary>
    public static async Task VoidForInvoiceAsync(
        AppDbContext context, Guid salesInvoiceKey, string userName, string reason)
    {
        var open = await context.SalesInvoiceDivergences
            .Where(d => d.SalesInvoiceKey == salesInvoiceKey && d.Status == SalesInvoiceDivergenceStatus.Open)
            .ToListAsync();

        var now = DateTime.Now;
        foreach (var divergence in open)
        {
            divergence.Status = SalesInvoiceDivergenceStatus.Voided;
            divergence.VoidedAt = now;
            divergence.VoidedBy = userName;
            divergence.VoidReason = reason;
        }
    }
}
