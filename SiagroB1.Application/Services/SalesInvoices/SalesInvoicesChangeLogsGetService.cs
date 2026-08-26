using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SalesInvoices;

public class SalesInvoicesChangeLogsGetService(AppDbContext context)
{
    /// <summary>
    /// Log de alterações de um documento de saída, do mais recente para o mais antigo — é a ordem
    /// em que a tela pergunta "o que mudou por último?".
    /// </summary>
    public IQueryable<SalesInvoiceChangeLog> QueryAll(Guid salesInvoiceKey) =>
        context.SalesInvoicesChangeLogs
            .AsNoTracking()
            .Where(x => x.SalesInvoiceKey == salesInvoiceKey)
            .OrderByDescending(x => x.ChangedAt);

    /// <summary>
    /// Log completo, para o entity set. Quem filtra é o $filter da requisição — hoje o diálogo
    /// da conferência de entregas, que pede as linhas de UM item.
    /// </summary>
    public IQueryable<SalesInvoiceChangeLog> QueryAll() =>
        context.SalesInvoicesChangeLogs.AsNoTracking();
}
