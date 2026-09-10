using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.ShipmentLoads;

public class ShipmentLoadsGetService(IUnitOfWork db, ILogger<ShipmentLoadsGetService> logger)
{
    public async Task<ShipmentLoad?> GetByIdAsync(Guid key)
    {
        try
        {
            logger.LogInformation("Fetching entity with ID {Id}", key);
            return await db.Context.ShipmentLoads
                .Include(x => x.Branch)
                .Include(x => x.Transactions)
                .Include(x => x.Invoices)
                .FirstOrDefaultAsync(x => x.Key == key);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching entity with ID {Id}", key);
            throw new DefaultException("Error fetching entity");
        }
    }

    public IQueryable<ShipmentLoad> QueryAll()
    {
        return db.Context.ShipmentLoads.AsNoTracking();
    }


    /// <summary>
    /// Romaneios montados nesta carga. Raiz de <c>DbSet</c> (e não <c>SelectMany</c> sobre a
    /// coleção do pai) para que o <c>$expand</c> gerado pelo UI5 — motorista, liberação e
    /// contrato de compra do romaneio — incida sobre uma consulta de entidade.
    /// </summary>
    public IQueryable<StorageTransaction> QueryTransactions(Guid shipmentLoadKey)
    {
        return db.Context.StorageTransactions
            .AsNoTracking()
            .Where(x => x.ShipmentLoadKey == shipmentLoadKey);
    }

    /// <summary>
    /// Documentos de saída desta carga, com as linhas e o contrato de venda de cada linha:
    /// é esse <c>Include</c> que alimenta <c>SalesContractCode</c>/<c>SalesContractComplement</c>/
    /// <c>SalesContractFreightCostStandard</c>, que são derivadas e voltam nulas sem ele.
    /// <c>SalesContractKey</c> é anulável, então o Include é LEFT JOIN e não descarta linha.
    /// </summary>
    public IQueryable<SalesInvoice> QueryInvoices(Guid shipmentLoadKey)
    {
        return db.Context.SalesInvoices
            .AsNoTracking()
            .Where(x => x.ShipmentLoadKey == shipmentLoadKey)
            .Include(x => x.Items)
            .ThenInclude(i => i.SalesContract);
    }

    public IQueryable<ShipmentLoadMovement> QueryMovements()
    {
        return db.Context.ShipmentLoadMovements.AsNoTracking();
    }

    /// <summary>
    /// Devoluções em armazém geradas pela recusa da carga. O filtro é por
    /// <c>RefusedFromShipmentLoadKey</c> — nunca por <c>ShipmentLoadKey</c>, que significa o
    /// oposto (romaneio montado NA carga).
    /// </summary>
    public IQueryable<StorageTransaction> QueryRefusalReturns(Guid shipmentLoadKey)
    {
        return db.Context.StorageTransactions
            .AsNoTracking()
            .Where(x => x.RefusedFromShipmentLoadKey == shipmentLoadKey);
    }
}
