using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.StorageAddresses;

/// <summary>
/// Encerra o lote de armazenagem a qualquer tempo. A data de encerramento é o último dia calculado:
/// depois dela o job diário e o reprocessamento ignoram o lote (<see cref="StorageAddressCalculationScope"/>),
/// e os value helps de lançamento deixam de oferecê-lo.
/// </summary>
public class StorageAddressesCloseService(IUnitOfWork db)
{
    /// <summary>Prefixo dos romaneios de quebra técnica gerados pelo job (ver StorageAddressesTechnicalLossCalculatorService).</summary>
    private const string AutomaticTechnicalLossPrefix = "QUEBRA_TECNICA_";

    public async Task ExecuteAsync(string code, DateTime closingDate, string userName, CancellationToken ct = default)
    {
        var date = closingDate.Date;

        var lot = await db.Context.StorageAddresses.FirstOrDefaultAsync(x => x.Code == code, ct)
                  ?? throw new NotFoundException($"Lote de armazenagem {code} não encontrado.");

        if (lot.Status == StorageAddressStatus.Closed)
            throw new ApplicationException($"O lote {code} já está encerrado.");

        if (date > DateTime.Today)
            throw new ApplicationException("A data de encerramento não pode ser futura.");

        // Romaneio depois da data mudaria o saldo de um lote que já não é calculado. Cancelados e
        // devolvidos não contam; a quebra técnica automática é desfeita logo abaixo.
        var lastMovement = await db.Context.StorageTransactions
            .Where(x =>
                x.StorageAddressCode == code &&
                x.TransactionDate > date &&
                x.TransactionStatus != StorageTransactionsStatus.Cancelled &&
                x.TransactionStatus != StorageTransactionsStatus.Returned &&
                !(x.TransactionType == StorageTransactionType.TechnicalLoss &&
                  x.Comments != null && x.Comments.StartsWith(AutomaticTechnicalLossPrefix) &&
                  !x.IsInvoiced))
            .MaxAsync(x => (DateTime?)x.TransactionDate, ct);

        if (lastMovement.HasValue)
            throw new ApplicationException(
                $"O lote {code} tem movimentação em {lastMovement:dd/MM/yyyy}, depois da data de encerramento.");

        var lastInvoicedCharge = await db.Context.StorageCharges
            .Where(x => x.StorageAddressCode == code && x.IsInvoiced && x.PeriodEnd > date)
            .MaxAsync(x => (DateTime?)x.PeriodEnd, ct);

        if (lastInvoicedCharge.HasValue)
            throw new ApplicationException(
                $"O lote {code} já tem cobrança faturada até {lastInvoicedCharge:dd/MM/yyyy}, depois da data de encerramento.");

        // Encerramento retroativo: desfaz o que o job já calculou depois da data e não foi faturado,
        // inclusive a cobrança cujo período atravessa a data (não há cobrança proporcional).
        db.Context.StorageDailyBalances.RemoveRange(
            db.Context.StorageDailyBalances.Where(x => x.StorageAddressCode == code && x.BalanceDate > date));

        db.Context.StorageCharges.RemoveRange(
            db.Context.StorageCharges.Where(x => x.StorageAddressCode == code && !x.IsInvoiced && x.PeriodEnd > date));

        db.Context.StorageTransactions.RemoveRange(
            db.Context.StorageTransactions.Where(x =>
                x.StorageAddressCode == code &&
                x.TransactionDate > date &&
                x.TransactionType == StorageTransactionType.TechnicalLoss &&
                x.Comments != null && x.Comments.StartsWith(AutomaticTechnicalLossPrefix) &&
                !x.IsInvoiced));

        lot.Status = StorageAddressStatus.Closed;
        lot.ClosingDate = date;
        lot.UpdatedAt = DateTime.Now;
        lot.UpdatedBy = userName;

        await db.Context.SaveChangesAsync(ct);
    }
}
