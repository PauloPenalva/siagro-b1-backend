using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.SalesShipmentReleases;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.ShipmentBilling;

/// <summary>
/// Emite o documento de saída contra uma liberação de entrega de venda. Atende os dois
/// caminhos: o novo, por <b>CARGA</b> (com faturamento parcial), e o legado, por romaneios
/// soltos — discriminados por <see cref="SalesInvoice.ShipmentLoadKey"/>.
/// </summary>
/// <remarks>
/// <b>O faturamento NÃO valida saldo de contrato.</b> O caminhão já saiu e já pesou: um
/// NetWeight maior que o saldo da liberação é fato consumado, não decisão a aprovar. Recusar
/// não desfaz a entrega — só impede registrá-la, e empurra o usuário para contornos (foi o que
/// produziu os contratos "AJUSTE DE SALDO" com TotalVolume = 1 absorvendo milhões de kg). O
/// saldo da liberação e o do contrato podem, portanto, ficar NEGATIVOS aqui.
/// <para>
/// O controle é aplicado na saída, não na entrada: liberação com saldo negativo não finaliza
/// (<c>SalesShipmentReleasesCloseService</c>) nem cancela (<c>SalesShipmentReleasesCancelationService</c>),
/// e contrato negativo não encerra (<c>SalesContractsCloseService</c>). O negativo é um estado
/// visível e temporário que obriga a regularização antes de congelar o registro.
/// </para>
/// Continuam valendo os guards que não são de saldo COMERCIAL: duplicidade de romaneio
/// (<c>ShipmentBillingTransactionGuardService</c>, só no caminho legado), status da liberação
/// (<c>SalesShipmentReleaseMovementGuardService</c>) e saldo FÍSICO da carga
/// (<c>ShipmentLoadsBillingGuardService</c>) — este último não é crédito comercial, é o volume
/// que efetivamente saiu no caminhão e não pode ser faturado duas vezes.
/// </remarks>
public class ShipmentBillingCreateSalesInvoiceService(
    IUnitOfWork db,
    SalesInvoicesCreateService salesInvoicesCreateService,
    ShipmentBillingTransactionGuardService transactionGuard,
    SalesShipmentReleaseMovementGuardService movementGuard,
    SalesShipmentReleasesRecalculateShippedService recalcShipped,
    SalesContractsAllocationCreateService allocationCreate,
    ShipmentLoadsBillingGuardService loadGuard,
    ShipmentLoadsRecalculateInvoicedService loadRecalc,
    ShipmentLoadsMovementLogService movementLog,
    ILogger<ShipmentBillingCreateSalesInvoiceService> logger)
{
    public async Task ExecuteAsync(SalesInvoice salesInvoice, string username)
    {
        Validate(salesInvoice);

        var releaseKey = salesInvoice.Items
            .First(i => i.SalesShipmentReleaseKey != null).SalesShipmentReleaseKey!.Value;

        var loadKey = salesInvoice.ShipmentLoadKey;
        var quantity = decimal.Round(
            salesInvoice.Items.Sum(i => i.Quantity), 3, MidpointRounding.ToEven);

        if (loadKey.HasValue)
        {
            // Saldo FÍSICO da carga, decidido sobre o recalculado. Antes de qualquer escrita:
            // uma tentativa recusada não pode deixar efeito no banco.
            await loadGuard.EnsureCanBillAsync(loadKey.Value, quantity);
        }
        else
        {
            // Bloqueia refaturar romaneio já vinculado a um documento de saída (duplo clique,
            // retentativa após erro, duas abas) — é o que produzia invoice duplicada e saldo
            // de contrato descontado duas vezes. Só o caminho LEGADO manda romaneios no
            // payload; no caminho da carga a invariante equivalente é o guard acima.
            await transactionGuard.EnsureCanBillAsync(
                salesInvoice.SalesTransactions.Select(t => t.Key).ToList());
        }

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

            if (loadKey.HasValue)
            {
                // DEPOIS do SaveChanges acima: a projeção SQL do saldo da carga soma as notas
                // vivas, e leria o estado anterior se a nota ainda não estivesse gravada.
                await loadRecalc.RecalculateAsync(loadKey.Value);

                var load = await db.Context.ShipmentLoads.FirstAsync(x => x.Key == loadKey.Value);

                // Cliente e local de entrega no movimento: é o que faz a narrativa do frete
                // registrar PARA ONDE a carga foi em cada faturamento. Numa carga recusada e
                // refaturada, são estas duas linhas — a de antes e a de depois da recusa — que
                // mostram os dois destinos.
                movementLog.Register(
                    loadKey.Value,
                    ShipmentLoadMovementType.Billed,
                    -quantity,
                    load.AvailableQuantity,
                    $"Documento de saída {salesInvoice.InvoiceNumber} emitido: {quantity:N3}.",
                    username,
                    salesInvoice.Key,
                    salesInvoice.InvoiceNumber,
                    ShipmentLoadMovementContext.FromInvoice(salesInvoice));

                await db.SaveChangesAsync();
            }

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

        // Exigência do caminho LEGADO apenas: o documento de carga não conhece romaneio,
        // chega neles pela carga. Exigir a coleção nos dois casos travaria o fluxo novo.
        if (salesInvoice.ShipmentLoadKey == null &&
            (salesInvoice.SalesTransactions == null || salesInvoice.SalesTransactions.Count == 0))
        {
            throw new ApplicationException("Sales Transactions is empty.");
        }
    }
}
