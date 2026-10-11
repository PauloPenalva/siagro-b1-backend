using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;
using SiagroB1.Infra.Enums;

namespace SiagroB1.Application.Services.StorageDeposits;

public sealed record StorageRemittanceRequest(Guid ShipmentReleaseKey, int UsageCode, decimal UnitPrice);

/// <summary>
/// "Emitir remessa" (spec 2026-10-10 §3.1): cria o Documento de Saída Pendente que remete ao armazém de terceiro a
/// quantidade liberada. Não confirma nem transmite — daí em diante é o fluxo comum do Documento de Saída.
/// </summary>
public class ShipmentReleasesCreateStorageRemittanceService(
    IUnitOfWork db,
    IConfiguration configuration,
    IBusinessPartnerService businessPartners,
    IUsage usages,
    SalesInvoicesCreateService createService,
    ILogger<ShipmentReleasesCreateStorageRemittanceService> logger)
{
    public async Task<SalesInvoice> ExecuteAsync(StorageRemittanceRequest request, string userName)
    {
        // TODA a validação antes de qualquer escrita.
        if (!ErpMode.IsStandalone(configuration))
            throw new DefaultException(StorageDepositRules.StandaloneOnlyMessage);

        var release = await db.Context.ShipmentReleases
                          .Include(r => r.PurchaseContract)
                          .FirstOrDefaultAsync(r => r.Key == request.ShipmentReleaseKey)
                      ?? throw new NotFoundException("Liberação não encontrada.");

        if (release.Status == ReleaseStatus.Cancelled)
            throw new DefaultException("Liberação cancelada não pode ter remessa para depósito.");

        var active = await StorageDepositRules.FindActiveRemittanceAsync(db.Context, release.Key);
        if (active != null)
            throw new DefaultException(
                $"A liberação já tem a remessa para depósito nº {StorageDepositRules.DocumentNumber(active.TaxDocumentNumber, active.InvoiceNumber)} ativa.");

        if (await businessPartners.GetByIdAsync(release.DeliveryLocationCode) is null)
            throw new DefaultException(
                $"O armazém {release.DeliveryLocationCode} não tem parceiro de negócio cadastrado: a remessa precisa de destinatário.");

        var usage = await usages.GetByIdAsync(request.UsageCode);
        if (usage is null || usage.StorageDepositRole != StorageDepositRole.Remittance)
            throw new DefaultException("Escolha uma natureza de remessa para depósito.");

        if (request.UnitPrice <= 0)
            throw new DefaultException("Informe o preço unitário da remessa.");

        var contract = release.PurchaseContract!;
        var invoice = new SalesInvoice
        {
            Key = Guid.NewGuid(),
            BranchCode = release.BranchCode ?? contract.BranchCode,
            CardCode = release.DeliveryLocationCode,
            InvoiceType = SalesInvoiceType.Normal,
            InvoiceDate = DateTime.Now.Date,
            ShipmentReleaseKey = release.Key,
            Comments = $"Remessa para depósito da liberação do contrato {contract.Code}.",
            Items =
            [
                new SalesInvoiceItem
                {
                    ItemCode = contract.ItemCode,
                    UnitOfMeasureCode = contract.UnitOfMeasureCode,
                    Quantity = release.ReleasedQuantity,
                    UnitPrice = request.UnitPrice,
                    UsageCode = usage.Code,
                },
            ],
        };

        try
        {
            await db.BeginTransactionAsync();
            await createService.ExecuteAsync(invoice, userName, CommitMode.Deferred);
            await db.SaveChangesAsync();
            await db.CommitAsync();
            return invoice;
        }
        catch (Exception e)
        {
            await db.RollbackAsync();
            logger.LogError(e, "Erro ao criar a remessa para depósito da liberação {Key}", release.Key);
            throw;
        }
    }
}
