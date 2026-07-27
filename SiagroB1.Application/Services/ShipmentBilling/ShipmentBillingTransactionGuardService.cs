using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ShipmentBilling;

public class ShipmentBillingTransactionGuardService(AppDbContext context)
{
    /// <summary>
    /// Recusa faturar romaneio que já esteja vinculado a um documento de saída ou que não
    /// esteja confirmado. É a trava contra duplicidade: sem ela, uma segunda submissão do
    /// mesmo faturamento (duplo clique, retentativa após erro, duas abas) re-aponta o
    /// romaneio para a nova invoice — deixando a primeira órfã — e cria uma segunda linha
    /// no ledger, descontando o saldo do contrato duas vezes. Chamado por
    /// <c>ShipmentBillingCreateSalesInvoiceService</c> antes de qualquer escrita.
    /// </summary>
    public async Task EnsureCanBillAsync(IReadOnlyCollection<Guid> transactionKeys)
    {
        if (transactionKeys.Count == 0)
            return;

        var transactions = await context.StorageTransactions
            .AsNoTracking()
            .Where(t => transactionKeys.Contains(t.Key))
            .Select(t => new
            {
                t.Key,
                t.Code,
                t.TransactionStatus,
                t.SalesInvoiceKey,
                t.InvoiceNumber,
            })
            .ToListAsync();

        var missing = transactionKeys.Except(transactions.Select(t => t.Key)).ToList();
        if (missing.Count > 0)
            throw new ApplicationException($"Romaneio {missing[0]} não encontrado.");

        var billed = transactions.FirstOrDefault(t => t.SalesInvoiceKey != null);
        if (billed != null)
            throw new ApplicationException(
                $"Romaneio {billed.Code} já foi faturado no documento de saída " +
                $"{billed.InvoiceNumber}. Atualize a lista antes de faturar novamente.");

        var notConfirmed = transactions
            .FirstOrDefault(t => t.TransactionStatus != StorageTransactionsStatus.Confirmed);

        if (notConfirmed != null)
            throw new ApplicationException(
                $"Romaneio {notConfirmed.Code} não está confirmado " +
                $"(situação atual: {notConfirmed.TransactionStatus}): não é possível faturar.");
    }
}
