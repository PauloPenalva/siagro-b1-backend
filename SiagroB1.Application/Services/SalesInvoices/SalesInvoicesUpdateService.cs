using SiagroB1.Application.Services.Accounting;
using SiagroB1.Application.Services.StorageInvoices;
using SiagroB1.Application.Services.StorageCustody;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;
using SiagroB1.Infra.FiscalStock;

namespace SiagroB1.Application.Services.SalesInvoices;

public class SalesInvoicesUpdateService(
    IUnitOfWork db, 
    IBusinessPartnerService businessPartnerService,
    SalesInvoicesTaxApplyService taxApply,
    SalesInvoicesFiscalComplementApplier fiscalComplement,
    ILogger<SalesInvoicesUpdateService> logger,
    FiscalStockPeriodGuard? periodGuard = null,
    AccountingAssignmentValidator? dimensionValidator = null,
    AccountingAssignmentDefaultsResolver? dimensionDefaults = null)
{
    private readonly FiscalStockPeriodGuard _periodGuard = periodGuard ?? new FiscalStockPeriodGuard(db);
    public async Task<SalesInvoice?> ExecuteAsync(Guid key, SalesInvoice entity, string userName)
    {
        var existingEntity = await db.Context.SalesInvoices
            .FirstOrDefaultAsync(tc => tc.Key == key) ?? throw new KeyNotFoundException("Entity not found.");

        // O CT-e é só documento de entrada (spec 2026-10-17): o enum é compartilhado, o documento de saída o recusa.
        if (entity.TaxDocumentKind == TaxDocumentKind.Cte)
            throw new DefaultException("CT-e não é documento de saída.");
        
        try
        {
            // O "antes" vem do rastreador: no PATCH a entidade chega já mutada (é a mesma
            // instância de existingEntity), então compará-las não acusaria mudança nenhuma.
            var original = db.Context.Entry(existingEntity).OriginalValues;
            var fiscalInputsChanged =
                !Equals(original[nameof(SalesInvoice.InvoiceDate)], entity.InvoiceDate) ||
                !Equals(original[nameof(SalesInvoice.CardCode)], entity.CardCode) ||
                !Equals(original[nameof(SalesInvoice.BranchCode)], entity.BranchCode);

            // Pelo GRAVADO: no PATCH a instância já chega mutada, e um corpo trocando situação, tipo ou filial não pode
            // escapar da trava do documento confirmado.
            var confirmedFrozen = SalesInvoiceNfeLock.IsConfirmedFrozen(
                (InvoiceStatus?)original[nameof(SalesInvoice.InvoiceStatus)],
                (SalesInvoiceType)original[nameof(SalesInvoice.InvoiceType)]!,
                await taxApply.IsBranchActiveAsync((string?)original[nameof(SalesInvoice.BranchCode)]));

            // Documento que conta no estoque fiscal: trocar data/filial tira a saída de um mês e põe em outro — os dois
            // precisam estar abertos (pelo gravado: no PATCH a instância já chega mutada).
            var originalBranch = (string?)original[nameof(SalesInvoice.BranchCode)];
            var originalDate = (DateTime?)original[nameof(SalesInvoice.InvoiceDate)];
            if (FiscalStockPeriodGuard.IsBookable((InvoiceStatus?)original[nameof(SalesInvoice.InvoiceStatus)]) &&
                (!Equals(originalDate, entity.InvoiceDate) || !Equals(originalBranch, entity.BranchCode)) &&
                await _periodGuard.SalesMovesStockAsync(existingEntity.Key))
            {
                await _periodGuard.EnsureOpenIfDatedAsync(originalBranch, originalDate);
                await _periodGuard.EnsureOpenIfDatedAsync(entity.BranchCode, entity.InvoiceDate);
            }

            // O "antes" das dimensões, do rastreador (no PATCH a instância já chega mutada).
            var previousDims = AccountingAssignmentSnapshot.FromOriginal(n => db.Context.Entry(existingEntity).OriginalValues[n]);

            var entry = db.Context.Entry(existingEntity);
            entry.CurrentValues.SetValues(entity);

            // Dimensões do cabeçalho: padrão do parceiro e validação do que mudou. As linhas existentes NÃO são reescritas.
            // Fora de nota pendente só valida (nota confirmada não ganha dimensão por baixo).
            await AccountingAssignmentWiring.ApplyAsync(dimensionDefaults, dimensionValidator, existingEntity, null,
                existingEntity.CardCode, previousDims,
                applyDefaults: existingEntity.InvoiceStatus is null or InvoiceStatus.Pending,
                documentDate: existingEntity.InvoiceDate);

            SalesInvoiceNfeLock.EnsureHeaderEditable(entry, confirmedFrozen);
            SalesInvoiceNfeLock.RestoreIssuanceFields(entry);
            SalesInvoiceNfeReturnLock.RestoreHeader(entry);
            SalesInvoiceIcmsComplementLock.RestoreHeader(entry);

            // O vínculo com a liberação (remessa para depósito) só nasce no "Emitir remessa": PUT/PATCH não o move.
            entry.Property(nameof(SalesInvoice.ShipmentReleaseKey)).CurrentValue =
                entry.OriginalValues[nameof(SalesInvoice.ShipmentReleaseKey)];

            // O vínculo da nota de custódia só nasce no "Gerar nota" do romaneio: PUT/PATCH não o move.
            entry.Property(nameof(SalesInvoice.CustodyStorageTransactionKey)).CurrentValue =
                entry.OriginalValues[nameof(SalesInvoice.CustodyStorageTransactionKey)];
            entry.Property(nameof(SalesInvoice.CustodyRole)).CurrentValue =
                entry.OriginalValues[nameof(SalesInvoice.CustodyRole)];

            // Complemento fiscal do contrato (spec 2026-10-07 D6): o PATCH reenvia o cabeçalho inteiro, então a
            // condição e o texto do contrato voltam em silêncio. No-op fora da regra e sem linha de contrato.
            await fiscalComplement.ApplyToHeaderAsync(existingEntity);

            // Spec 2026-10-19 parte 3: o vínculo com a fatura de armazenagem só nasce na criação; PUT/PATCH não o move.
            entry.Property(nameof(SalesInvoice.StorageInvoiceKey)).CurrentValue =
                entry.OriginalValues[nameof(SalesInvoice.StorageInvoiceKey)];
            await StorageInvoiceBillingGuardService.EnsureCanBillAsync(db.Context, existingEntity);

            existingEntity.UpdatedAt = DateTime.Now;
            existingEntity.UpdatedBy = userName;
            existingEntity.CardName = (await businessPartnerService.GetByIdAsync(existingEntity.CardCode))?.CardName;
            existingEntity.TruckingCompanyName =
                entity.TruckingCompanyCode != null
                    ? (await businessPartnerService.GetByIdAsync(entity.TruckingCompanyCode))?.CardName
                    : string.Empty;
            existingEntity.DeliveryCardName =
                entity.DeliveryCardCode != null
                    ? (await businessPartnerService.GetByIdAsync(entity.DeliveryCardCode))?.CardName
                    : string.Empty;

            // Data, cliente e filial são entradas do cálculo (vigência do IBS/CBS, UF, regime):
            // mudou um deles, todas as linhas se recalculam. No-op com a regra inativa.
            if (fiscalInputsChanged)
            {
                var items = await db.Context.SalesInvoicesItems
                    .Where(i => i.SalesInvoiceKey == existingEntity.Key)
                    .ToListAsync();

                await taxApply.ApplyAsync(existingEntity, items);
            }

            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            logger.Log(LogLevel.Error, "Failed to update entity.");
            throw new DefaultException("Error updating entity due to concurrency issues.");
        }

        return entity;
    }
    
}