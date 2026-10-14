using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Services.Notifications;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.PurchaseContracts;

public class PurchaseContractsCloseService(
    AppDbContext context,
    PurchaseContractsFixedVolumeService fixedVolumeService,
    ContractNotificationOutboxService notificationOutbox,
    IConfiguration configuration,
    ILogger<PurchaseContractsCloseService> logger)
{
    /// <summary>D13: data de corte (yyyy-MM-dd) dos contratos anteriores aos títulos firmes. Sem a chave, ninguém é isento.</summary>
    public const string CutoverDateKey = "Financial:FirmPayablesCutoverDate";

    public const string LegacyCancellationReason = "Encerramento do contrato";

    public async Task ExecuteAsync(Guid key, string userName)
    {
        var contract = await context.PurchaseContracts
                           .FirstOrDefaultAsync(x => x.Key == key && x.Status == ContractStatus.Approved)
                       ?? throw new NotFoundException("Contrato não encontrado ou não está aprovado.");

        // ANTES da guarda de fixação: ela desconta o washout lavado ASSUMINDO que só sobrou
        // Approved — se um InApproval ainda pudesse passar, a conta contaria volume que talvez
        // seja rejeitado e volte ao saldo.
        await GuardPendingWashoutsAsync(contract);

        if (contract.Type == ContractType.ToBeDetermined)
            await GuardPriceFixationAsync(contract);

        await GuardNegativeBalanceAsync(contract);

        await GuardFinancialDocumentsAsync(contract, userName);

        contract.Status = ContractStatus.Finished;
        contract.UpdatedAt = DateTime.Now;
        contract.UpdatedBy = userName;

        notificationOutbox.Register(contract, NotificationEventType.Closed, userName);

        // Spec 2026-10-13 (títulos firmes, regra 28 e D8): o encerramento NÃO cancela mais o provisório e não altera
        // valor nenhum — ele só passa com tudo quitado (a entrega a menos se resolve por washout antes).
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Regra 28: nenhum firme/devolução do contrato com saldo, e todo provisório não cancelado QUITADO
    /// (baixa + compensação = valor contratado).
    /// </summary>
    private async Task GuardFinancialDocumentsAsync(PurchaseContract contract, string userName)
    {
        // Firme/devolução da conciliação e firme do complemento de valor (D11); o título a receber do washout não entra.
        var hasOpenFirm = await context.FinancialDocuments.AnyAsync(x =>
            x.PurchaseContractKey == contract.Key &&
            (x.OriginType == FinancialDocumentOrigin.PurchaseFiscalReconciliation ||
             x.OriginType == FinancialDocumentOrigin.PurchaseInvoice) &&
            (x.Nature == FinancialDocumentNature.Firm || x.Nature == FinancialDocumentNature.PurchaseReturn) &&
            x.Status != FinancialDocumentStatus.Canceled &&
            x.NetAmount > x.SettledAmount);

        if (hasOpenFirm)
            throw new ApplicationException(
                "Há títulos firmes com saldo neste contrato: componha, compense ou baixe por dação antes de encerrar.");

        await CancelLegacyUntouchedProvisionalsAsync(contract, userName);

        var unsettled = await context.FinancialDocuments
            .Where(x => x.PurchaseContractKey == contract.Key &&
                        x.Nature == FinancialDocumentNature.Provisional &&
                        x.Status != FinancialDocumentStatus.Canceled &&
                        x.Status != FinancialDocumentStatus.Settled)
            .OrderBy(x => x.Code)
            .Select(x => new { x.Key, x.Code })
            .ToListAsync();
        // O SQL não vê o cancelamento do legado acima, ainda só em memória: reconfere pela instância rastreada.
        var unsettledCode = unsettled
            .Where(x => context.FinancialDocuments.Local.FirstOrDefault(d => d.Key == x.Key) is not { Status: FinancialDocumentStatus.Canceled })
            .Select(x => x.Code)
            .FirstOrDefault();

        if (unsettledCode is not null)
            throw new ApplicationException(
                $"O título {unsettledCode} não está quitado: o contrato só encerra com todos os títulos quitados. Entrega a menos se resolve por washout; faturamento abaixo, por documento de complemento de valor.");
    }

    /// <summary>
    /// D13 (spec 2026-10-13, decisões da revisão final): provisório criado ANTES da data de corte, sem composição e sem
    /// baixa, não trava o encerramento: é cancelado como antes desta fase ("Encerramento do contrato"). Enqueue-only:
    /// grava junto com o contrato, e uma recusa adiante descarta tudo. Sem a chave (ou com valor inválido), nada é isento.
    /// </summary>
    private async Task CancelLegacyUntouchedProvisionalsAsync(PurchaseContract contract, string userName)
    {
        var raw = configuration[CutoverDateKey];
        if (string.IsNullOrWhiteSpace(raw))
            return;

        if (!DateTime.TryParseExact(raw.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var cutover))
        {
            logger.LogWarning("{Key} inválida ({Value}): nenhum provisório é isento no encerramento.", CutoverDateKey, raw);
            return;
        }

        var legacy = await context.FinancialDocuments
            .Where(x => x.PurchaseContractKey == contract.Key &&
                        x.Nature == FinancialDocumentNature.Provisional &&
                        x.Status != FinancialDocumentStatus.Canceled &&
                        x.Status != FinancialDocumentStatus.Settled &&
                        x.CreatedAt != null && x.CreatedAt < cutover &&
                        x.ComposedAmount == 0m && x.SettledAmount == 0m)
            .ToListAsync();

        foreach (var provisional in legacy)
        {
            FinancialDocumentsCancelService.Cancel(provisional, LegacyCancellationReason, userName);
            logger.LogInformation(
                "Encerramento do contrato {Contract}: provisório {Document} anterior à data de corte {Cutover:yyyy-MM-dd} cancelado.",
                contract.Code, provisional.Code, cutover);
        }
    }

    /// <summary>
    /// Um contrato a fixar não pode ser encerrado devendo preço de mercadoria já entregue.
    /// Usa o volume CONFIRMADO — volume apenas em aprovação não define preço — descontado do
    /// volume lavado por washout: aquele volume fixado saiu do contrato e não precisa de preço.
    /// <see cref="GuardPendingWashoutsAsync"/> já rodou antes desta guarda, então os washouts
    /// ATIVOS aqui são só os APROVADOS.
    /// </summary>
    private async Task GuardPriceFixationAsync(PurchaseContract contract)
    {
        var pendingCount = await context.PurchaseContractsPriceFixations
            .CountAsync(f => f.PurchaseContractKey == contract.Key
                             && f.Status == PriceFixationStatus.InApproval);

        if (pendingCount > 0)
            throw new ApplicationException(
                $"Contrato possui {pendingCount} fixação(ões) pendente(s) de aprovação. " +
                "Aprove ou rejeite antes de encerrar.");

        var confirmedVolume = await fixedVolumeService.ConfirmedVolumeAsync(contract.Key);
        var deliveredVolume = await fixedVolumeService.DeliveredVolumeAsync(contract.Key);

        var (washedOutTotal, washedOutUnfixed) = await PurchaseContractsWashedOutVolumeService
            .ActiveVolumesAsync(context, contract.Key);
        var approvedWashedFixed = washedOutTotal - washedOutUnfixed;

        var pricedConfirmed = confirmedVolume - approvedWashedFixed;

        if (pricedConfirmed < deliveredVolume)
            throw new ApplicationException(
                $"Volume entregue sem preço fixado. Entregue: {deliveredVolume:N3}, " +
                $"fixado e confirmado (descontado o washout): {pricedConfirmed:N3}. " +
                "Fixe o preço do volume entregue antes de encerrar o contrato.");
    }

    /// <summary>
    /// Washout em aprovação reserva volume mas ainda pode ser rejeitado. Encerrar com ele pendente
    /// congelaria o contrato com um volume "lavado" que ninguém decidiu.
    /// </summary>
    private async Task GuardPendingWashoutsAsync(PurchaseContract contract)
    {
        var pendingCount = await context.PurchaseContractsWashouts
            .CountAsync(w => w.PurchaseContractKey == contract.Key
                             && w.Status == PurchaseContractWashoutStatus.InApproval);

        if (pendingCount > 0)
            throw new ApplicationException(
                $"Contrato possui {pendingCount} washout(s) pendente(s) de aprovação. " +
                "Aprove ou rejeite antes de encerrar.");
    }

    /// <summary>
    /// Espelha <c>SalesContractsCloseService</c>: contrato consumido ALÉM do volume contratado
    /// não pode ser congelado. Decide sobre o saldo RECALCULADO do ledger e dos washouts ativos,
    /// não sobre os agregados persistidos, que podem estar defasados. Não persiste o recálculo.
    /// </summary>
    private async Task GuardNegativeBalanceAsync(PurchaseContract contract)
    {
        var allocated = await PurchaseContractsRecalculateBalanceService
            .CalculateAllocatedAsync(context, contract.Key);
        var (washedOut, _) = await PurchaseContractsWashedOutVolumeService
            .ActiveVolumesAsync(context, contract.Key);
        var balance = decimal.Round(contract.TotalVolume - allocated - washedOut, 2, MidpointRounding.ToEven);

        if (balance < 0)
            throw new ApplicationException(
                $"Contrato faturado além do volume contratado. Contratado: {contract.TotalVolume:N2}, " +
                $"alocado: {allocated:N2}, washout: {washedOut:N2}, saldo: {balance:N2}. " +
                "Ajuste as alocações antes de encerrar.");
    }
}
