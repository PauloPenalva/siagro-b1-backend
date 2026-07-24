using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.PurchaseContracts;

public class PurchaseContractsChangeLogsGetService(AppDbContext context)
{
    /// <summary>
    /// Log de alterações de um contrato, do mais recente para o mais antigo — é a ordem em
    /// que a tela pergunta "o que mudou por último?".
    /// </summary>
    public IQueryable<PurchaseContractChangeLog> QueryAll(Guid purchaseContractKey) =>
        context.PurchaseContractsChangeLogs
            .AsNoTracking()
            .Where(x => x.PurchaseContractKey == purchaseContractKey)
            .OrderByDescending(x => x.ChangedAt);
}
