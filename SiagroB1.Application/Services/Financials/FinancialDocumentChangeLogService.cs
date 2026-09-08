using SiagroB1.Domain.Entities;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Financials;

/// <summary>
/// Porta ÚNICA de escrita do log do documento financeiro. Só enfileira a linha no
/// ChangeTracker — quem salva é o serviço de mutação que chamou. É isso que faz o log nascer e
/// morrer junto com a alteração: operação revertida não deixa log, operação salva nunca o perde.
/// </summary>
public class FinancialDocumentChangeLogService(AppDbContext context)
{
    public void Register(Guid documentKey, string field, string? oldValue, string? newValue, string userName)
    {
        context.FinancialDocumentChangeLogs.Add(new FinancialDocumentChangeLog
        {
            FinancialDocumentKey = documentKey,
            Field = field,
            OldValue = oldValue,
            NewValue = newValue,
            ChangedAt = DateTime.Now,
            ChangedBy = userName
        });
    }
}
