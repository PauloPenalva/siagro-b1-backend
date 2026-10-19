using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Financials;

/// <summary>Fecha a reconciliação quando abertura + marcado bate com o saldo do extrato.</summary>
public class FinancialAccountReconciliationsCloseService(IUnitOfWork db)
{
    public async Task ExecuteAsync(Guid key, string userName)
    {
        var r = await FinancialAccountReconciliationsLoader.LoadAsync(db, key);

        if (r.Status != FinancialAccountReconciliationStatus.Open)
            throw new ApplicationException("A reconciliação já está fechada.");

        var lines = await new FinancialAccountReconciliationsGetLinesService(db).ExecuteAsync(key);
        if (lines.Difference != 0m)
            throw new ApplicationException(string.Format(
                System.Globalization.CultureInfo.GetCultureInfo("pt-BR"),
                "Diferença de R$ {0:N2} entre o saldo conciliado e o saldo do extrato.", Math.Abs(lines.Difference)));

        r.Status = FinancialAccountReconciliationStatus.Closed;
        r.ClosedAt = DateTime.Now;
        r.UpdatedAt = DateTime.Now;
        r.UpdatedBy = userName;
        await db.SaveChangesAsync();
    }
}
