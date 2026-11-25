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

    private const decimal Tolerance = 0.001m;

    /// <summary>
    /// Σ QuantityDifference dos registros Open dos documentos que esta recusa devolve POR INTEIRO
    /// (o que resta do documento depois dela ≤ 0,001). Zero para os demais.
    /// </summary>
    /// <remarks>
    /// Decisão de 25/11/2026: o documento divergente devolvido por inteiro traz de volta a carga física (o efetivo),
    /// não a quantidade da NF. Somado à quantidade devolvida, dá o que se move no armazém, no transbordo ou na quebra
    /// de trajeto. A recusa parcial move só o informado e o registro segue contando no saldo. Documento sem registro
    /// (carga sem divergência, STANDALONE) contribui com zero.
    /// </remarks>
    public static async Task<decimal> PhysicalAdjustmentAsync(
        AppDbContext context,
        IReadOnlyCollection<(Guid SalesInvoiceKey, decimal RemainingAfterRefusal)> refusedDocuments)
    {
        var fullyReturnedKeys = refusedDocuments
            .Where(d => d.RemainingAfterRefusal <= Tolerance)
            .Select(d => d.SalesInvoiceKey)
            .Distinct()
            .ToList();

        if (fullyReturnedKeys.Count == 0)
            return decimal.Zero;

        var total = await context.SalesInvoiceDivergences
            .Where(d => fullyReturnedKeys.Contains(d.SalesInvoiceKey)
                        && d.Status == SalesInvoiceDivergenceStatus.Open)
            .SumAsync(d => (decimal?)d.QuantityDifference) ?? decimal.Zero;

        return decimal.Round(total, 3, MidpointRounding.ToEven);
    }

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
