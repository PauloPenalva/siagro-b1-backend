using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Services.SalesShipmentReleases;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.SalesInvoices;

public class SalesInvoicesItemsUpdateService(
    IUnitOfWork db, 
    IItemService itemService,
    ILogger<SalesInvoicesUpdateService> logger)
{
    public async Task<SalesInvoiceItem?> ExecuteAsync(Guid key, SalesInvoiceItem entity, string userName)
    {
        var existingEntity = await db.Context.SalesInvoicesItems
            .FirstOrDefaultAsync(tc => tc.Key == key) ?? throw new KeyNotFoundException("Entity not found.");
        
        try
        {
            entity.ItemName = (await itemService.GetByIdAsync(entity.ItemCode))?.ItemName;

            // No PATCH a entidade chega JÁ rastreada e mutada — o controller a carrega e
            // aplica o Delta nela no MESMO DbContext, então o FirstOrDefault acima devolve
            // essa mesma instância (identity map) e comparar existingEntity com entity
            // compararia o objeto com ele mesmo, nunca acusando mudança. O "antes"
            // verdadeiro está no OriginalValues do rastreador, que serve aos dois caminhos
            // (PATCH aliasado e PUT com entidade desanexada).
            var original = db.Context.Entry(existingEntity).OriginalValues;

            var deliveryChanged =
                (decimal)original[nameof(SalesInvoiceItem.DeliveredQuantity)]! != entity.DeliveredQuantity ||
                (decimal)original[nameof(SalesInvoiceItem.QuantityLoss)]! != entity.QuantityLoss ||
                (SalesInvoiceDeliveryStatus)original[nameof(SalesInvoiceItem.DeliveryStatus)]! != entity.DeliveryStatus;

            // Encerrar com líquido zerado/negativo faria o fator efetivo virar 0 ou negativo
            // e devolver todo o volume ao contrato. Só barra quando a entrega está sendo
            // mexida: linha Closed legada com líquido <= 0 segue editável nos demais campos.
            if (deliveryChanged &&
                entity.DeliveryStatus == SalesInvoiceDeliveryStatus.Closed &&
                entity.DeliveredQuantity - entity.QuantityLoss <= 0)
                throw new DefaultException(
                    "Não é possível encerrar a entrega com peso líquido zerado ou negativo. " +
                    "Informe a quantidade entregue e o desconto antes de encerrar.");

            // Log e carimbo ANTES do SaveChanges, mas montados a partir do OriginalValues:
            // depois do SetValues o "de" já teria sido sobrescrito. Nada disso chega ao banco
            // se o guard acima tiver estourado.
            var logs = deliveryChanged ? BuildDeliveryLogs(existingEntity, entity, original, userName) : [];

            db.Context.Entry(existingEntity).CurrentValues.SetValues(entity);

            if (deliveryChanged)
            {
                // Depois do SetValues: os carimbos não vêm no corpo do PATCH, e o SetValues
                // sobrescreveria com o nulo da entidade recebida no caminho PUT.
                existingEntity.UpdatedAt = DateTime.Now;
                existingEntity.UpdatedBy = userName;
                db.Context.SalesInvoicesChangeLogs.AddRange(logs);
            }

            await db.SaveChangesAsync();

            // Numa devolução o peso do cabeçalho é a soma das linhas — quem devolve saldo ao
            // contrato é a Quantity do item, e deixar os dois números seguirem caminhos
            // separados foi o que fez uma devolução de 20 estornar 30. Depois do flush: a soma
            // agrega no SERVIDOR e leria a quantidade anterior.
            await SalesInvoicesReturnWeightService.RecalculateAsync(
                db.Context, existingEntity.SalesInvoiceKey);

            await db.SaveChangesAsync();

            // Entrega/quebra mudou → o fator efetivo do item mudou; recalcula os contratos
            // com alocação neste item no ledger (inclui destinos de realocação).
            if (deliveryChanged)
            {
                await SalesContractsRecalculateBalanceService.RecalculateForItemsAsync(
                    db.Context, [key]);
                await db.SaveChangesAsync();

                // A liberação de entrega segue a MESMA regra do contrato, então precisa do
                // mesmo gatilho. Depois do flush acima: a titularidade da diferença é
                // reeleita em memória lá, e a projeção SQL daqui leria a flag antiga.
                await SalesShipmentReleasesRecalculateShippedService.RecalculateForItemsAsync(
                    db.Context, [key]);
                await db.SaveChangesAsync();
            }
        }
        catch (DbUpdateConcurrencyException)
        {
            logger.Log(LogLevel.Error, "Failed to update entity.");
            throw new DefaultException("Error updating entity due to concurrency issues.");
        }

        return entity;
    }
    

    /// <summary>
    /// Uma linha de log por campo da conferência que realmente mudou. O "de" sai do
    /// <paramref name="original"/> (no PATCH a entidade chega rastreada e mutada, então o
    /// valor anterior só existe no rastreador) e os dois lados são formatados aqui, para
    /// "de" e "para" ficarem comparáveis mesmo que a máscara da tela mude depois.
    /// </summary>
    private static List<SalesInvoiceChangeLog> BuildDeliveryLogs(
        SalesInvoiceItem existingEntity,
        SalesInvoiceItem entity,
        PropertyValues original,
        string userName)
    {
        var logs = new List<SalesInvoiceChangeLog>();

        void Add(string field, string oldValue, string newValue)
        {
            if (oldValue == newValue) return;

            logs.Add(new SalesInvoiceChangeLog
            {
                SalesInvoiceKey = existingEntity.SalesInvoiceKey,
                SalesInvoiceItemKey = existingEntity.Key,
                ChangedBy = userName,
                Field = field,
                OldValue = oldValue,
                NewValue = newValue,
            });
        }

        Add(ContractChangeLogFields.DeliveredQuantity,
            ContractChangeLogFields.DescribeQuantity(
                (decimal)original[nameof(SalesInvoiceItem.DeliveredQuantity)]!),
            ContractChangeLogFields.DescribeQuantity(entity.DeliveredQuantity));

        Add(ContractChangeLogFields.QuantityLoss,
            ContractChangeLogFields.DescribeQuantity(
                (decimal)original[nameof(SalesInvoiceItem.QuantityLoss)]!),
            ContractChangeLogFields.DescribeQuantity(entity.QuantityLoss));

        Add(ContractChangeLogFields.DeliveryStatus,
            ContractChangeLogFields.DescribeDeliveryStatus(
                (SalesInvoiceDeliveryStatus)original[nameof(SalesInvoiceItem.DeliveryStatus)]!),
            ContractChangeLogFields.DescribeDeliveryStatus(entity.DeliveryStatus));

        return logs;
    }
}
