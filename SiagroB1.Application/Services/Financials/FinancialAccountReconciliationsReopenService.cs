using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Financials;

/// <summary>
/// Reabre a ÚLTIMA reconciliação fechada da conta, mantendo as marcações. É o caminho para remover a conciliação de
/// um lançamento (e então estorná-lo ou excluí-lo). Reabrir uma do meio mudaria o saldo de abertura das seguintes.
/// </summary>
public class FinancialAccountReconciliationsReopenService(IUnitOfWork db)
{
    public async Task ExecuteAsync(Guid key, string userName)
    {
        var r = await FinancialAccountReconciliationsLoader.LoadAsync(db, key);

        if (r.Status != FinancialAccountReconciliationStatus.Closed)
            throw new ApplicationException("A reconciliação já está aberta.");

        var ofAccount = db.Context.FinancialAccountReconciliations.Where(x => x.FinancialAccountCode == r.FinancialAccountCode);

        if (await ofAccount.AnyAsync(x => x.Status == FinancialAccountReconciliationStatus.Open))
            throw new ApplicationException("Feche ou exclua a reconciliação aberta antes de reabrir outra.");

        if (await ofAccount.AnyAsync(x => x.Status == FinancialAccountReconciliationStatus.Closed &&
                                          x.StatementDate > r.StatementDate))
            throw new ApplicationException("Somente a última reconciliação fechada pode ser reaberta.");

        r.Status = FinancialAccountReconciliationStatus.Open;
        r.ClosedAt = null;
        r.UpdatedAt = DateTime.Now;
        r.UpdatedBy = userName;
        await db.SaveChangesAsync();
    }
}
