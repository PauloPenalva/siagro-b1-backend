using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Financials;

/// <summary>
/// Corrige o vencimento sem reabrir o contrato. É o único campo mutável do documento na Fase 1,
/// e por isso é o primeiro consumidor do log de alterações.
/// </summary>
public class FinancialDocumentsSetDueDateService(
    IUnitOfWork db,
    FinancialDocumentChangeLogService changeLog)
{
    public async Task ExecuteAsync(Guid key, DateTime dueDate, string userName)
    {
        var document = await db.Context.FinancialDocuments.FirstOrDefaultAsync(x => x.Key == key)
                       ?? throw new NotFoundException("Documento financeiro não encontrado.");

        if (document.Status == FinancialDocumentStatus.Canceled)
            throw new ApplicationException($"O documento {document.Code} está cancelado.");

        if (document.DueDate.Date == dueDate.Date) return;

        // Registrado ANTES do SaveChanges, para nascer e morrer com a alteração.
        changeLog.Register(
            key,
            FinancialDocumentChangeLogFields.DueDate,
            document.DueDate.ToString("dd/MM/yyyy"),
            dueDate.ToString("dd/MM/yyyy"),
            userName);

        document.DueDate = dueDate;
        document.UpdatedAt = DateTime.Now;
        document.UpdatedBy = userName;

        await db.SaveChangesAsync();
    }
}
