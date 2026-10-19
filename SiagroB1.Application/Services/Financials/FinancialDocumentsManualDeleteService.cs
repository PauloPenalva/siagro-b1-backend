using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Financials;

/// <summary>
/// Exclusão do título lançado à mão. Só sai se nunca teve movimento de baixa (nem estornado): com histórico de dinheiro o
/// caminho é estornar e cancelar, que preserva o rastro. Título gerado pelo sistema e adiantamento nunca saem por aqui.
/// O log de alterações do título vai junto.
/// </summary>
public class FinancialDocumentsManualDeleteService(IUnitOfWork db)
{
    public async Task ExecuteAsync(Guid key)
    {
        var document = await db.Context.FinancialDocuments.FirstOrDefaultAsync(x => x.Key == key)
                       ?? throw new NotFoundException("Título financeiro não encontrado.");

        if (!FinancialDocumentsManualRules.IsManualTitle(document))
            throw new ApplicationException(
                $"O título {document.Code} não foi lançado manualmente e não pode ser excluído.");

        var hasSettlements = document.SettledAmount != 0m ||
                             await db.Context.FinancialSettlements.AnyAsync(x =>
                                 x.FinancialDocumentKey == key || x.RelatedDocumentKey == key);
        if (hasSettlements)
            throw new ApplicationException(
                $"O título {document.Code} possui movimento de baixa financeira. Exclusão não permitida: estorne as baixas e cancele o título.");

        var logs = await db.Context.FinancialDocumentChangeLogs.Where(x => x.FinancialDocumentKey == key).ToListAsync();
        db.Context.FinancialDocumentChangeLogs.RemoveRange(logs);
        db.Context.FinancialDocuments.Remove(document);

        await db.Context.SaveChangesAsync();
    }
}
