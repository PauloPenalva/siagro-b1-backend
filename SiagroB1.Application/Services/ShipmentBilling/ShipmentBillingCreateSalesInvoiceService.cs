using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.SalesShipmentReleases;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.ShipmentBilling;

public class ShipmentBillingCreateSalesInvoiceService(
    IUnitOfWork db,
    SalesInvoicesCreateService salesInvoicesCreateService,
    ShipmentBillingTransactionGuardService transactionGuard,
    SalesShipmentReleaseMovementGuardService movementGuard,
    SalesShipmentReleasesRecalculateShippedService recalcShipped,
    SalesContractsAllocationCreateService allocationCreate,
    ILogger<ShipmentBillingCreateSalesInvoiceService> logger)
{
    public async Task ExecuteAsync(SalesInvoice salesInvoice, string username)
    {
        Validate(salesInvoice);

        var releaseKey = salesInvoice.Items
            .First(i => i.SalesShipmentReleaseKey != null).SalesShipmentReleaseKey!.Value;

        // Bloqueia refaturar romaneio já vinculado a um documento de saída (duplo clique,
        // retentativa após erro, duas abas) — é o que produzia invoice duplicada e saldo
        // de contrato descontado duas vezes.
        await transactionGuard.EnsureCanBillAsync(
            salesInvoice.SalesTransactions.Select(t => t.Key).ToList());

        // Bloqueia faturar contra liberação finalizada/cancelada/pausada.
        await movementGuard.EnsureCanBillAsync(releaseKey);

        await EnsureReleaseHasBalanceAsync(salesInvoice, releaseKey);

        try
        {
            // Tudo numa transação só: sem ela o RollbackAsync abaixo era no-op e uma falha
            // depois do primeiro SaveChanges deixava invoice e vínculo gravados, mas com
            // erro na tela — convidando o usuário a refaturar.
            await db.BeginTransactionAsync();

            await salesInvoicesCreateService.ExecuteAsync(salesInvoice, username);

            salesInvoice.InvoiceStatus = InvoiceStatus.Confirmed;
            await db.SaveChangesAsync();

            // Alocação padrão no ledger (item → contrato original, consumindo a liberação)
            // — precisa estar gravada ANTES do recálculo, que agora lê do ledger.
            await allocationCreate.ExecuteForInvoiceAsync(salesInvoice, username);

            // Ledger gravado → baixa o saldo liberado a partir das alocações.
            await recalcShipped.RecalculateAsync(releaseKey);

            await db.CommitAsync();
        }
        catch (Exception e)
        {
            await db.RollbackAsync();
            logger.LogError(e, e.Message);
            throw new ApplicationException(e.Message);
        }
    }

    /// <summary>
    /// Recusa o faturamento que exceda o saldo disponível da liberação. Mede pelo NetWeight
    /// dos romaneios selecionados (mesmo eixo do consumo em
    /// <see cref="SalesShipmentReleasesRecalculateShippedService"/>).
    /// </summary>
    private async Task EnsureReleaseHasBalanceAsync(SalesInvoice salesInvoice, Guid releaseKey)
    {
        var transactionKeys = salesInvoice.SalesTransactions.Select(t => t.Key).ToList();

        var shippingVolume = await db.Context.StorageTransactions
            .Where(t => transactionKeys.Contains(t.Key))
            .SumAsync(t => t.NetWeight);

        var release = await db.Context.SalesShipmentReleases
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Key == releaseKey)
            ?? throw new ApplicationException("Liberação de entrega não encontrada.");

        if (shippingVolume > release.AvailableQuantity)
            throw new ApplicationException(
                $"Volume a faturar ({shippingVolume:N3}) excede o saldo da liberação " +
                $"({release.AvailableQuantity:N3}).");
    }

    private static void Validate(SalesInvoice salesInvoice)
    {
        if (salesInvoice.Items.Any(i => i.SalesContractKey == null))
        {
            throw new ApplicationException("Sales Contract Key is empty.");
        }

        if (salesInvoice.Items.All(i => i.SalesShipmentReleaseKey == null))
        {
            throw new ApplicationException("Sales Shipment Release Key is empty.");
        }

        if (salesInvoice.SalesTransactions == null || salesInvoice.SalesTransactions.Count == 0)
        {
            throw new ApplicationException("Sales Transactions is empty.");
        }
    }
}
