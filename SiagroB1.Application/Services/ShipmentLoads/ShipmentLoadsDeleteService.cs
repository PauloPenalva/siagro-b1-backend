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
/// A permissão é deliberadamente ESTREITA e as condições são verificadas separadamente, não
/// deduzidas uma da outra: status <c>Planned</c>, nenhum romaneio vinculado, nenhuma nota — nem
/// mesmo cancelada —, nenhuma troca de liberação (GAC-1177 v2; ver
/// <see cref="Domain.Entities.ShippingReleaseChange"/>) e nenhum transbordo (GAC-1181). Em
/// qualquer outro caso a resposta é cancelar a carga, e <see cref="ShipmentLoadsCancelService"/>
/// continua sendo o caminho.
/// </para>
/// <para>
/// ⚠️ <b>O número consumido da sequência NÃO volta.</b> <c>DocNumberSequenceService</c> não tem
/// devolução, e fabricar uma abriria a porta para dois documentos com o mesmo número. O buraco
/// na numeração é o preço, e é o preço certo.
/// </para>
/// </remarks>
public class ShipmentLoadsDeleteService(
    IUnitOfWork db, ShipmentLoadsCompositionGuardService compositionGuard)
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

        // GAC-1177 v2: a troca de liberação não deixa rastro em ShipmentLoadKey de
        // StorageTransaction/SalesInvoice — só no próprio documento ShippingReleaseChange. Sem
        // este guard, excluir apagaria o histórico da troca (e, com FK real, quebraria o delete
        // como InvalidOperationException em vez do ApplicationException esperado aqui).
        var hasReleaseChange = await db.Context.ShippingReleaseChanges
            .AnyAsync(x => x.ShipmentLoadKey == key);

        if (hasReleaseChange)
            throw new ApplicationException(
                $"A carga {load.Code} teve troca de liberação e não pode ser excluída. Cancele a carga.");

        // GAC-1181: defesa em profundidade — hoje inalcançável (uma carga com transbordo já tem
        // volume, e uma carga apenas planejada não tem como ter transbordo pelas trilhas do
        // app), mas usa a MESMA trava e mensagem de Cancel, e evita que o delete estoure 547
        // silenciosamente se algum dia a exceção de "sem romaneio" deixar de valer.
        await compositionGuard.EnsureNoTransshipmentAsync(load);

        var movements = await db.Context.ShipmentLoadMovements
            .Where(x => x.ShipmentLoadKey == key)
            .ToListAsync();

        var comments = await db.Context.ShipmentLoadsComments
            .Where(x => x.ShipmentLoadKey == key)
            .ToListAsync();

        var changeLogs = await db.Context.ShipmentLoadsChangeLogs
            .Where(x => x.ShipmentLoadKey == key)
            .ToListAsync();

        var discharges = await db.Context.ShipmentLoadsDischarges
            .Where(x => x.ShipmentLoadKey == key)
            .ToListAsync();

        var attachments = await db.Context.ShipmentLoadsAttachments
            .Where(x => x.ShipmentLoadKey == key)
            .ToListAsync();

        var transshipments = await db.Context.ShipmentLoadsTransshipments
            .Where(x => x.ShipmentLoadKey == key)
            .ToListAsync();

        try
        {
            await db.BeginTransactionAsync();

            // Movimentos, comentários e linhas do log de alterações têm FK real para a carga e
            // todas as FKs deste projeto são NoAction: sem remover os filhos primeiro, o delete
            // do pai quebra.
            db.Context.ShipmentLoadMovements.RemoveRange(movements);
            db.Context.ShipmentLoadsComments.RemoveRange(comments);
            db.Context.ShipmentLoadsChangeLogs.RemoveRange(changeLogs);

            // GAC-1171: ticket de descarga e anexo também têm FK NoAction para a carga — de
            // propósito, para que cancelar/excluir a nota não leve embora a evidência física que
            // libera o pagamento do frete.
            //
            // A ordem TEXTUAL destas duas linhas é indiferente: quem garante que o anexo saia
            // depois da descarga que o referencia é o sort topológico do grafo de FK que o EF faz
            // dentro do SaveChanges, não a sequência em que RemoveRange foi chamado.
            //
            // E, hoje, o RemoveRange(discharges) é ramo MORTO: o guard acima recusa qualquer carga
            // que tenha documento de saída, inclusive cancelado, e toda descarga exige uma nota
            // desta carga (ShipmentLoadDischargesCreateService). Uma carga que chega até aqui
            // nunca tem descarga. Fica como defesa em profundidade, para o dia em que aquele
            // guard afrouxar — só o RemoveRange(attachments) é caminho vivo.
            db.Context.ShipmentLoadsDischarges.RemoveRange(discharges);
            db.Context.ShipmentLoadsAttachments.RemoveRange(attachments);

            // GAC-1181: mesma FK NoAction, mesmo motivo — e mesmo ramo MORTO que os tickets de
            // descarga: o guard acima já recusa excluir com transbordo. Defesa em profundidade,
            // não caminho vivo.
            db.Context.ShipmentLoadsTransshipments.RemoveRange(transshipments);

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
