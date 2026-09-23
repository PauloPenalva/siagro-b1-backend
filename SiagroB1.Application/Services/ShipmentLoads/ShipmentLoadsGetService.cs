using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
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
    /// Romaneios do grid da carga: a Expedição vigente (<see cref="StorageTransaction.ShipmentLoadKey"/>
    /// ainda aponta a carga) e, depois de uma troca de liberação (GAC-1177 v2), a original que ela
    /// substituiu e o estorno (12) que ela gerou — ambos sem <c>ShipmentLoadKey</c>, alcançados só
    /// pelo <see cref="ShippingReleaseChange"/> cujo <c>ShipmentLoadKey</c> é esta carga. Restrito a
    /// <see cref="StorageTransactionType.SalesShipment"/>/<see cref="StorageTransactionType.SalesShipmentReturn"/>
    /// (7/12): a perna de compra da troca (8/9) não aparece nesta tela. Alcança também a ENTRADA de
    /// cada transbordo da carga (GAC-1181), que não carrega <c>ShipmentLoadKey</c> nenhum.
    /// </summary>
    /// <remarks>
    /// Raiz de <c>DbSet</c> (e não <c>SelectMany</c> sobre a coleção do pai) para que o
    /// <c>$expand</c> gerado pelo UI5 — motorista, liberação e contrato de compra do romaneio —
    /// incida sobre uma consulta de entidade. Os subqueries sobre <c>ShippingReleaseChanges</c> e
    /// <c>ShipmentLoadsTransshipments</c> permanecem traduzíveis pelo EF (viram <c>IN</c>/<c>EXISTS</c>
    /// no SQL Server).
    /// <para>
    /// ⚠️ O ramo por <c>ShippingReleaseChangeKey</c> só pode casar
    /// <see cref="StorageTransactionType.SalesShipmentReturn"/> (o estorno 12). A Expedição NOVA
    /// (tipo 7) que a troca criou também carrega <c>ShippingReleaseChangeKey</c>, mas quando ela
    /// solta da carga (cancelamento/desvínculo) seu <c>ShipmentLoadKey</c> volta a <c>null</c> e
    /// ela deixa de pertencer a esta carga — sem a restrição de tipo aqui, ela reapareceria no
    /// grid da carga ORIGINAL mesmo já estando livre para ser vinculada a outra (ver
    /// <c>Attach.controller.ts</c> no frontend).
    /// </para>
    /// <para>
    /// ⚠️ <b>O ramo do transbordo é um OR de topo</b>, e não mais um termo dentro do filtro de
    /// tipo: a entrada é <see cref="StorageTransactionType.TransshipmentReceipt"/> (armazém de
    /// terceiro) ou <see cref="StorageTransactionType.Receipt"/> (armazém próprio) — nenhum dos
    /// dois é 7/12 — então precisa escapar do <c>&amp;&amp;</c> que restringe o resto da consulta
    /// a Expedição/estorno. A saída do transbordo, por já carregar <c>ShipmentLoadKey</c> (ver
    /// <c>StorageTransaction.ShipmentLoadTransshipmentKey</c>), já entra pelo primeiro ramo — o
    /// OR aqui não a duplica, só alcança a entrada que o primeiro ramo não vê.
    /// </para>
    /// </remarks>
    public IQueryable<StorageTransaction> QueryTransactions(Guid shipmentLoadKey)
    {
        // GAC-1175: a carga de REMOÇÃO vincula Recebimentos e não conhece troca de liberação
        // nem devolução — o grid dela é a FK pura. O tipo da carga é lido do banco (e não
        // recebido por parâmetro) para o controller e o grid continuarem com uma única rota.
        var loadType = db.Context.ShipmentLoads
            .Where(x => x.Key == shipmentLoadKey)
            .Select(x => x.LoadType)
            .FirstOrDefault();

        if (loadType == ShipmentLoadType.Removal)
        {
            return db.Context.StorageTransactions
                .AsNoTracking()
                .Where(x => x.ShipmentLoadKey == shipmentLoadKey &&
                            x.TransactionType == StorageTransactionType.Receipt);
        }

        var changeKeysForLoad = db.Context.ShippingReleaseChanges
            .Where(c => c.ShipmentLoadKey == shipmentLoadKey)
            .Select(c => c.Key);

        // GAC-1181, no molde do changeKeysForLoad acima (GAC-1177): a entrada de um transbordo só
        // é alcançada pela chave do PRÓPRIO transbordo, nunca por ShipmentLoadKey.
        var transshipmentKeysForLoad = db.Context.ShipmentLoadsTransshipments
            .Where(t => t.ShipmentLoadKey == shipmentLoadKey)
            .Select(t => t.Key);

        return db.Context.StorageTransactions
            .AsNoTracking()
            .Where(x =>
                ((x.TransactionType == StorageTransactionType.SalesShipment ||
                  x.TransactionType == StorageTransactionType.SalesShipmentReturn) &&
                 (x.ShipmentLoadKey == shipmentLoadKey ||
                  (x.ReplacedByShippingReleaseChangeKey != null &&
                   changeKeysForLoad.Contains(x.ReplacedByShippingReleaseChangeKey.Value)) ||
                  (x.TransactionType == StorageTransactionType.SalesShipmentReturn &&
                   x.ShippingReleaseChangeKey != null &&
                   changeKeysForLoad.Contains(x.ShippingReleaseChangeKey.Value)))) ||
                (x.ShipmentLoadTransshipmentKey != null &&
                 transshipmentKeysForLoad.Contains(x.ShipmentLoadTransshipmentKey.Value)));
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
