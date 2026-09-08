using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Financials;

/// <summary>
/// Cancela documento financeiro. CANCELA, NUNCA APAGA: apagar funcionaria e mataria o rastro —
/// é a tentação óbvia desta feature. Cancelar também é o que LIBERA a origem no índice único
/// filtrado, permitindo que a reabertura de contrato regenere o provisório.
/// </summary>
public class FinancialDocumentsCancelService(IUnitOfWork db)
{
    private AppDbContext Context => db.Context;

    /// <summary>Ponto de entrada da tela: cancela um documento e SALVA.</summary>
    public async Task ExecuteAsync(Guid key, string reason, string userName)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ApplicationException("Informe o motivo do cancelamento.");

        var document = await Context.FinancialDocuments.FirstOrDefaultAsync(x => x.Key == key)
                       ?? throw new NotFoundException("Documento financeiro não encontrado.");

        if (document.Status == FinancialDocumentStatus.Canceled)
            throw new ApplicationException($"O documento {document.Code} já está cancelado.");

        if (document.SettledAmount != 0m)
            throw new ApplicationException(
                $"O documento {document.Code} possui baixas. Estorne-as antes de cancelar.");

        Cancel(document, reason, userName);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Enqueue-only, para os hooks de contrato: cancela o provisório de UMA origem e não salva.
    /// </summary>
    public async Task EnqueueCancelByOriginAsync(
        FinancialDocumentOrigin originType, Guid originKey, string reason, string userName)
    {
        var documents = await Context.FinancialDocuments
            .Where(x => x.OriginType == originType &&
                        x.OriginKey == originKey &&
                        x.Nature == FinancialDocumentNature.Provisional &&
                        x.Status != FinancialDocumentStatus.Canceled)
            .ToListAsync();

        foreach (var document in documents)
            Cancel(document, reason, userName);
    }

    /// <summary>
    /// Enqueue-only: cancela TODOS os provisórios abertos de um contrato. Adiantamento não é
    /// tocado — Nature é filtrado por Provisional — porque o dinheiro do adiantamento pode já
    /// ter saído.
    /// </summary>
    public async Task EnqueueCancelByContractAsync(
        Guid? purchaseContractKey, Guid? salesContractKey, string reason, string userName)
    {
        var documents = await Context.FinancialDocuments
            .Where(x => x.Nature == FinancialDocumentNature.Provisional &&
                        x.Status != FinancialDocumentStatus.Canceled &&
                        ((purchaseContractKey != null && x.PurchaseContractKey == purchaseContractKey) ||
                         (salesContractKey != null && x.SalesContractKey == salesContractKey)))
            .ToListAsync();

        foreach (var document in documents)
            Cancel(document, reason, userName);
    }

    private static void Cancel(Domain.Entities.FinancialDocument document, string reason, string userName)
    {
        document.Status = FinancialDocumentStatus.Canceled;
        document.CancellationReason = reason;
        document.CanceledAt = DateTime.Now;
        document.CanceledBy = userName;
        document.UpdatedAt = DateTime.Now;
        document.UpdatedBy = userName;
    }
}
