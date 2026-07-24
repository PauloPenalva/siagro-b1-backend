using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SalesContracts;

public class SalesContractsChangeLogsGetService(AppDbContext context)
{
    /// <summary>
    /// Log de alterações de um contrato, do mais recente para o mais antigo — é a ordem em
    /// que a tela pergunta "o que mudou por último?".
    /// </summary>
    public IQueryable<SalesContractChangeLog> QueryAll(Guid salesContractKey) =>
        context.SalesContractsChangeLogs
            .AsNoTracking()
            .Where(x => x.SalesContractKey == salesContractKey)
            .OrderByDescending(x => x.ChangedAt);
}
