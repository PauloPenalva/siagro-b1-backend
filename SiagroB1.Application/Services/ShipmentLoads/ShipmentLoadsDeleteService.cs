using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// Exclui uma carga que nunca saiu do planejamento — a criada por engano, ainda vazia.
/// </summary>
/// <remarks>
/// Existe porque a carga passou a nascer do planejamento: antes, ela só podia ser montada a
/// partir de romaneios reais, e cancelar era a resposta certa para qualquer arrependimento.
/// Uma carga que nunca teve romaneio nem nota não tem história que valha preservar, e
/// transformá-la num cancelamento só polui a lista.
/// <para>
/// A permissão é deliberadamente ESTREITA e as três condições são verificadas separadamente, não
/// deduzidas uma da outra: status <c>Planned</c>, nenhum romaneio vinculado e nenhuma nota — nem
/// mesmo cancelada. Em qualquer outro caso a resposta é cancelar a carga, e
/// <see cref="ShipmentLoadsCancelService"/> continua sendo o caminho.
/// </para>
/// <para>
/// ⚠️ <b>O número consumido da sequência NÃO volta.</b> <c>DocNumberSequenceService</c> não tem
/// devolução, e fabricar uma abriria a porta para dois documentos com o mesmo número. O buraco
/// na numeração é o preço, e é o preço certo.
/// </para>
/// </remarks>
public class ShipmentLoadsDeleteService(IUnitOfWork db)
{
    public async Task ExecuteAsync(Guid key)
    {
        var load = await db.Context.ShipmentLoads
                       .FirstOrDefaultAsync(x => x.Key == key) ??
                   throw new NotFoundException($"Shipment load not found key {key}");

        if (load.Status != ShipmentLoadStatus.Planned)
            throw new ApplicationException(
                $"Somente uma carga apenas planejada pode ser excluída. A carga {load.Code} " +
                "já teve movimento — cancele-a em vez de excluí-la.");

        var hasShipments = await db.Context.StorageTransactions
            .AnyAsync(x => x.ShipmentLoadKey == key);

        if (hasShipments)
            throw new ApplicationException(
                $"A carga {load.Code} possui romaneios vinculados. Desvincule-os antes de excluí-la.");

        var hasInvoices = await db.Context.SalesInvoices
            .AnyAsync(x => x.ShipmentLoadKey == key);

        if (hasInvoices)
            throw new ApplicationException(
                $"A carga {load.Code} possui documentos de saída e não pode ser excluída.");

        var movements = await db.Context.ShipmentLoadMovements
            .Where(x => x.ShipmentLoadKey == key)
            .ToListAsync();

        try
        {
            await db.BeginTransactionAsync();

            // Os movimentos têm FK real para a carga e todas as FKs deste projeto são
            // NoAction: sem remover os filhos primeiro, o delete do pai quebra.
            db.Context.ShipmentLoadMovements.RemoveRange(movements);
            db.Context.ShipmentLoads.Remove(load);

            await db.SaveChangesAsync();

            await db.CommitAsync();
        }
        catch
        {
            await db.RollbackAsync();
            throw;
        }
    }
}
