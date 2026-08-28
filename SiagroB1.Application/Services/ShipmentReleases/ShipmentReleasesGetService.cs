using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.ShipmentReleases;

public class ShipmentReleasesGetService(IUnitOfWork db, ILogger<ShipmentReleasesGetService> logger)
{
    public async Task<ShipmentRelease?> GetByIdAsync(Guid key)
    {
        try
        {
            logger.LogInformation("Fetching entity with ID {Id}", key);
            return await db.Context.ShipmentReleases
                .Include(x => x.PurchaseContract)
                .Include(x => x.Branch)
                .FirstOrDefaultAsync(x => x.Key == key);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,"Error fetching entity with ID {Id}", key);
            throw new DefaultException("Error fetching entity");
        }
    }

    /// <summary>
    /// Base do entity set. <b>Sem <c>Include(Transactions)</c> de propósito.</b> O Include
    /// era incondicional e trazia as ~60 colunas de STORAGE_TRANSACTIONS de TODA liberação
    /// da página, mesmo sem ninguém pedir — a carga inicial da tela, que não tem filtro,
    /// estourava o CommandTimeout de 30s contra o banco remoto.
    /// <para>
    /// Ninguém dependia dele: a lista não lê a coleção, e o Detail monta a tabela de
    /// romaneios com binding próprio em <c>/StorageTransactions</c> filtrado pela
    /// liberação. Quem precisar da navegação continua tendo — <c>[EnableQuery]</c> resolve
    /// <c>$expand=Transactions</c> por projeção, independente de Include.
    /// </para>
    /// </summary>
    public IQueryable<ShipmentRelease> QueryAll()
    {
        return db.Context.ShipmentReleases
            .AsNoTracking();
    }
}