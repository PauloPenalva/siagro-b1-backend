using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SalesInvoices;

/// <summary>
/// Desfaz na ORIGEM o que a criação do retorno aplicou: status "Retornado" e fechamento da
/// entrega (<see cref="SalesInvoicesReturnService"/>, linhas 56-63).
///
/// Só o CANCELAMENTO e a EXCLUSÃO do retorno chamam isto, porque só eles fazem o documento de
/// retorno deixar de valer. O estorno de confirmação NÃO chama: ele devolve o retorno para
/// Pendente, mas o documento continua existindo, e a origem continua retornada. A regra geral
/// é desfazer cada efeito no mesmo nível em que ele foi aplicado — o que a confirmação aplica
/// (os romaneios) é o estorno que desfaz.
///
/// Sem SaveChanges: roda dentro da transação de quem chamou.
/// </summary>
public static class SalesInvoicesReturnOriginRestoreService
{
    public static async Task ExecuteAsync(
        AppDbContext context, SalesInvoice returnInvoice, string userName)
    {
        if (returnInvoice.InvoiceType != SalesInvoiceType.Return
            || returnInvoice.SalesInvoiceOriginKey is not { } originKey)
        {
            return;
        }

        var originInvoice = await context.SalesInvoices
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Key == originKey);

        // Origem ausente não pode impedir o cancelamento do retorno.
        if (originInvoice is null)
        {
            return;
        }

        originInvoice.InvoiceStatus = InvoiceStatus.Confirmed;
        originInvoice.UpdatedAt = DateTime.Now;
        originInvoice.UpdatedBy = userName;

        // Reabrir a entrega: sem isso a origem fica fechada para sempre e um novo retorno
        // sobre ela é barrado por Validate com "Invoice closed.", sem saída pela tela.
        //
        // DeliveredQuantity volta a ZERO, e não ao valor anterior ao retorno: não há
        // bookkeeping desse valor. Isso descartaria uma conferência de entrega PARCIAL
        // anterior ao retorno — cenário que a operação confirmou não existir aqui (a entrega
        // é sempre integral). Se um dia passar a existir, é aqui que o valor anterior teria
        // de ser guardado.
        originInvoice.DeliveryStatus = SalesInvoiceDeliveryStatus.Open;

        foreach (var item in originInvoice.Items)
        {
            item.DeliveryStatus = SalesInvoiceDeliveryStatus.Open;
            item.DeliveredQuantity = 0;
        }

        // Reabrir muda o fator efetivo (a quebra só desconta com a entrega fechada) → mesmo
        // recálculo que o retorno faz ao fechar, no caminho de volta. Passar as chaves dos
        // itens é o que torna o resultado correto: elas saem da soma SQL e entram em memória,
        // porque SumAsync agrega no SERVIDOR e ainda leria a entrega como fechada.
        await SalesContractsRecalculateBalanceService.RecalculateForItemsAsync(
            context,
            originInvoice.Items.Where(i => i.Key != null).Select(i => i.Key!.Value).ToList());
    }
}
