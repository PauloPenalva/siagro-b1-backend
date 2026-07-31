using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.SalesShipmentReleases;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.ShipmentBilling;

/// <summary>
/// Fatura os romaneios embarcados contra uma liberação de entrega de venda.
/// </summary>
/// <remarks>
/// <b>O faturamento NÃO valida saldo.</b> O caminhão já saiu e já pesou: um NetWeight maior que
/// o saldo da liberação é fato consumado, não decisão a aprovar. Recusar não desfaz a entrega —
/// só impede registrá-la, e empurra o usuário para contornos (foi o que produziu os contratos
/// "AJUSTE DE SALDO" com TotalVolume = 1 absorvendo milhões de kg). O saldo da liberação e o do
/// contrato podem, portanto, ficar NEGATIVOS aqui.
/// <para>
/// O controle é aplicado na saída, não na entrada: liberação com saldo negativo não finaliza
/// (<c>SalesShipmentReleasesCloseService</c>) nem cancela (<c>SalesShipmentReleasesCancelationService</c>),
/// e contrato negativo não encerra (<c>SalesContractsCloseService</c>). O negativo é um estado
/// visível e temporário que obriga a regularização antes de congelar o registro.
/// </para>
/// Continuam valendo os guards que não são de saldo: duplicidade de romaneio
/// (<c>ShipmentBillingTransactionGuardService</c>) e status da liberação
/// (<c>SalesShipmentReleaseMovementGuardService</c>).
/// </remarks>
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
