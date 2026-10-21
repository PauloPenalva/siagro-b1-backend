using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.PurchaseDischarges;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.FiscalStock;

namespace SiagroB1.Application.Services.SalesInvoices;

public class SalesInvoicesItemsDeleteService(
    IUnitOfWork db,
    SalesInvoicesTaxApplyService taxApply,
    ILogger<SalesInvoicesItemsDeleteService> logger,
    FiscalStockPeriodGuard? periodGuard = null)
{
    private readonly FiscalStockPeriodGuard _periodGuard = periodGuard ?? new FiscalStockPeriodGuard(db);
    public async Task<bool> ExecuteAsync(Guid key)
    {
        return await DeleteAsyncWithTransaction(key, async entity =>
        {
            await db.SaveChangesAsync();
        });
    }
    
    private async Task<bool> DeleteAsyncWithTransaction(Guid key, Func<SalesInvoiceItem, Task>? preDeleteAction = null)
    {
        try
        {
            await db.BeginTransactionAsync();
            var entity = await db.Context.SalesInvoicesItems
                .FirstOrDefaultAsync(x => x.Key == key);
            
            if (entity == null)
            {
                logger.LogWarning("Entity {Entity} with ID {Id} not found.", nameof(SalesContract), key);
                return false;
            }

            
            if (preDeleteAction != null)
                await preDeleteAction(entity);

            var salesInvoiceKey = entity.SalesInvoiceKey;

            // Pelo gravado, como a trava da NF-e: nenhuma linha sai do documento confirmado que vai
            // para o XML (ver SalesInvoiceNfeLock.IsConfirmedFrozen).
            var stored = await db.Context.SalesInvoices.AsNoTracking()
                .Where(i => i.Key == salesInvoiceKey)
                .Select(i => new { i.NfeStatus, i.InvoiceStatus, i.InvoiceType, i.BranchCode, i.InvoiceDate })
                .FirstOrDefaultAsync();

            // Linha que movimenta o estoque fiscal saindo de documento que já conta: mês fechado recusa.
            if (stored is not null && entity.MovesFiscalInventory && FiscalStockPeriodGuard.IsBookable(stored.InvoiceStatus))
                await _periodGuard.EnsureOpenIfDatedAsync(stored.BranchCode, stored.InvoiceDate);

            // Complemento de ICMS (spec 2026-10-18 S6): as linhas são as escolhidas na criação; para mudar, exclua o documento.
            if (stored?.InvoiceType == SalesInvoiceType.IcmsComplement)
                throw new DefaultException(SalesInvoiceIcmsComplementLock.LinesChangeMessage);

            SalesInvoiceNfeLock.EnsureLinesChangeable(
                stored?.NfeStatus ?? NfeStatus.None,
                stored is not null && SalesInvoiceNfeLock.IsConfirmedFrozen(
                    stored.InvoiceStatus, stored.InvoiceType, await taxApply.IsBranchActiveAsync(stored.BranchCode)));

            // GAC-1171: a FK de SHIPMENT_LOAD_DISCHARGE_ITEMS (o rateio do ticket) para a linha da
            // nota é NoAction, de propósito — o ticket de descarga é a evidência física que libera
            // o pagamento do frete e não pode ser levado embora junto com a linha. Sem este guard o
            // banco devolve erro 547, a transação rola atrás e o usuário vê um 500 de corpo vazio.
            // ⚠️ O InMemory dos testes não aplica FK: só o guard faz o caminho aparecer.
            if (await db.Context.ShipmentLoadsDischargesItems.AnyAsync(x => x.SalesInvoiceItemKey == key))
                throw new DefaultException(
                    "Este item tem ticket de descarga registrado na carga. " +
                    "Exclua o registro de descarga antes.");

            // Troca nota: quebra/sobra de descarga ATIVA lançada no contrato de compra a partir
            // deste item — o acerto do fornecedor ficaria órfão se a linha sumisse debaixo dele.
            if (await PurchaseDischargeGuards.HasActiveForItemAsync(db.Context, key))
                throw new DefaultException(PurchaseDischargeGuards.ItemMessage);

            db.Context.SalesInvoicesItems.Remove(entity);

            await db.SaveChangesAsync();

            // Numa devolução o peso do cabeçalho é a soma das linhas. Depois do flush: a soma
            // agrega no SERVIDOR e ainda contaria a linha removida.
            await SalesInvoicesReturnWeightService.RecalculateAsync(db.Context, salesInvoiceKey);

            await db.SaveChangesAsync();
            await db.CommitAsync();
            return true;
        }
        catch (Exception ex)
        {
            await db.RollbackAsync();
            logger.LogError(ex, "Error deleting entity {Entity} with ID {Id}", nameof(SalesContract), key);
            throw;
        }
    }
}