using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.Enums;

namespace SiagroB1.Application.Services.StorageTransactions;

/// <summary>
/// Confirmação de romaneio pela tela Romaneios de Movimentação (action
/// <c>StorageTransactionsConfirmed</c>): confirma e, se o romaneio for a SAÍDA de um lote de
/// transbordo em armazém próprio, vincula ao transbordo da carga e emite a liberação (GAC-1232).
/// </summary>
/// <remarks>
/// <para>
/// <b>Por que existe.</b> Até o GAC-1232 só a conclusão do ticket de pesagem emitia a liberação
/// do transbordo em armazém próprio. A Yokotobi não usa o módulo de Pesagem e lança a saída do lote
/// por esta tela: o lote era baixado, mas a carga ficava sem liberação e o contrato de compra não
/// aparecia na Expedição.
/// </para>
/// <para>
/// <b>Por que um serviço à parte e não dentro de <see cref="StorageTransactionsConfirmedService"/>.</b>
/// Aquele serviço é chamado por uns dez fluxos (pesagem, recusa, troca de liberação, conferência
/// de saldo...), vários deles dentro da transação de quem chamou. O gatilho só pertence aos dois
/// pontos onde a saída do lote é confirmada: a pesagem (que já o chama) e esta tela.
/// </para>
/// <para>
/// <b>Uma transação só.</b> Confirmar e vincular andam juntos: se o vínculo for recusado (caminhão
/// sem transbordo aberto no lote, ou mais de um candidato), o romaneio não fica confirmado. A
/// confirmação roda em <see cref="CommitMode.Auto"/>, que só grava (não comita), e o vínculo em
/// <see cref="CommitMode.Deferred"/>, mesmo arranjo de <c>WeighingTicketsCompletedService</c>.
/// </para>
/// </remarks>
public class StorageTransactionsConfirmWithTransshipmentService(
    IUnitOfWork db,
    StorageTransactionsConfirmedService confirmed,
    ShipmentLoadsTransshipmentAttachLotExitService attachLotExit)
{
    public async Task ExecuteAsync(Guid key, string userName)
    {
        try
        {
            await db.BeginTransactionAsync();

            await confirmed.ExecuteAsync(key, userName);

            var st = await db.Context.StorageTransactions.FirstOrDefaultAsync(x => x.Key == key) ??
                     throw new NotFoundException("Storage transaction not found.");

            await attachLotExit.AttachIfTransshipmentLotExitAsync(st, userName, CommitMode.Deferred);

            await db.CommitAsync();
        }
        catch
        {
            await db.RollbackAsync();
            throw;
        }
    }
}
