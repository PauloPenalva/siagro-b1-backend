using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
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

    public const string ReversalOutOfOrderMessage = "Estorne primeiro a recusa mais recente deste documento.";

    /// <summary>
    /// Estorno de devolução de documento DIVERGENTE só em ordem inversa: recusa a devolução se houver outra devolução
    /// Confirmed da mesma origem criada depois dela (<c>CreatedAt</c>). Sem registro Open na origem, não restringe nada.
    /// </summary>
    /// <remarks>
    /// O ajuste do efetivo (<see cref="PhysicalAdjustmentAsync"/>) fica no romaneio da recusa que COMPLETOU o documento.
    /// Estornar uma recusa anterior deixaria a origem parcialmente devolvida — o termo de divergência volta a contar —
    /// com a diferença ainda presa no romaneio da recusa posterior: saldo negativo. Em ordem inversa, o romaneio com o
    /// ajuste sai primeiro e o saldo fecha a cada passo.
    /// <para>
    /// Só as devoluções Confirmed contam: a estornada volta a Pending e o romaneio dela é cancelado, então ela já não
    /// segura o ajuste — e contar Pending travaria a própria ordem inversa (estornada a mais recente, a anterior
    /// continuaria barrada por ela). <paramref name="reversedTogether"/> são as devoluções estornadas na mesma operação
    /// (irmãs da mesma recusa), que não barram umas às outras.
    /// </para>
    /// </remarks>
    public static async Task EnsureReversalInOrderAsync(
        AppDbContext context, SalesInvoice returnInvoice, IReadOnlyCollection<Guid>? reversedTogether = null)
    {
        if (returnInvoice.InvoiceType != SalesInvoiceType.Return || returnInvoice.SalesInvoiceOriginKey is not { } originKey)
            return;

        var divergent = await context.SalesInvoiceDivergences
            .AnyAsync(d => d.SalesInvoiceKey == originKey && d.Status == SalesInvoiceDivergenceStatus.Open);

        if (!divergent)
            return;

        var excluded = (reversedTogether ?? []).Append(returnInvoice.Key).ToList();
        var createdAt = returnInvoice.CreatedAt;

        var hasLaterReturn = await context.SalesInvoices
            .AnyAsync(i => i.SalesInvoiceOriginKey == originKey
                           && i.InvoiceType == SalesInvoiceType.Return
                           && i.InvoiceStatus == InvoiceStatus.Confirmed
                           && !excluded.Contains(i.Key)
                           && i.CreatedAt > createdAt);

        if (hasLaterReturn)
            throw new DefaultException(ReversalOutOfOrderMessage);
    }

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
