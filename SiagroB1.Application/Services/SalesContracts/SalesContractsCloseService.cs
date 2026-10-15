using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Services.Notifications;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SalesContracts;

public class SalesContractsCloseService(
    AppDbContext context,
    SalesContractsFixedVolumeService fixedVolumeService,
    ContractNotificationOutboxService notificationOutbox,
    IConfiguration configuration,
    ILogger<SalesContractsCloseService> logger)
{
    /// <summary>Spec 2026-10-15 (regra 16): data de corte PRÓPRIA da venda (yyyy-MM-dd) — o deploy da venda tem outra data.</summary>
    public const string CutoverDateKey = "Financial:SalesFirmReceivablesCutoverDate";

    public async Task ExecuteAsync(Guid key, string userName)
    {
        var contract = await context.SalesContracts
                           .FirstOrDefaultAsync(x => x.Key == key && x.Status == ContractStatus.Approved)
                       ?? throw new NotFoundException("Contrato não encontrado ou não está aprovado.");

        if (contract.Type == ContractType.ToBeDetermined)
            await GuardPriceFixationAsync(contract);

        await GuardNegativeBalanceAsync(contract);

        await GuardFinancialDocumentsAsync(contract, userName);

        await GuardAdvanceCreditAsync(contract);

        contract.Status = ContractStatus.Finished;
        contract.UpdatedAt = DateTime.Now;
        contract.UpdatedBy = userName;

        notificationOutbox.Register(contract, NotificationEventType.Closed, userName);

        // Spec 2026-10-15 (regra 15): o encerramento NÃO cancela mais o provisório — ele só passa com tudo quitado.
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Um contrato a fixar não pode ser encerrado devendo preço de mercadoria já entregue.
    /// Usa o volume CONFIRMADO — volume apenas em aprovação não define preço.
    /// </summary>
    private async Task GuardPriceFixationAsync(SalesContract contract)
    {
        var pendingCount = await context.SalesContractsPriceFixations
            .CountAsync(f => f.SalesContractKey == contract.Key
                             && f.Status == PriceFixationStatus.InApproval);

        if (pendingCount > 0)
            throw new ApplicationException(
                $"Contrato possui {pendingCount} fixação(ões) pendente(s) de aprovação. " +
                "Aprove ou rejeite antes de encerrar.");

        var confirmedVolume = await fixedVolumeService.ConfirmedVolumeAsync(contract.Key);
        var deliveredVolume = await fixedVolumeService.DeliveredVolumeAsync(contract.Key);

        if (confirmedVolume < deliveredVolume)
            throw new ApplicationException(
                $"Volume entregue sem preço fixado. Entregue: {deliveredVolume:N3}, " +
                $"fixado e confirmado: {confirmedVolume:N3}. " +
                "Fixe o preço do volume entregue antes de encerrar o contrato.");
    }

    /// <summary>
    /// Contrato faturado ALÉM do volume contratado não pode ser congelado: encerrado, ele sai
    /// das listas de alocação e do recálculo em lote, e o volume excedente fica órfão.
    /// Decide sobre o saldo RECALCULADO do ledger, não sobre <see cref="SalesContract.AllocatedVolume"/>:
    /// o agregado é persistido-derivado e pode estar defasado — usar o valor persistido barraria
    /// contratos corretos por drift. Não persiste o recálculo: encerrar não tem efeito colateral
    /// de saldo (para isso existe o botão Recalcular Saldo, ao lado na tela).
    /// </summary>
    private async Task GuardNegativeBalanceAsync(SalesContract contract)
    {
        var allocated = await SalesContractsRecalculateBalanceService
            .CalculateAllocatedAsync(context, contract.Key);
        var balance = decimal.Round(contract.TotalVolume - allocated, 3, MidpointRounding.ToEven);

        if (balance < 0)
            throw new ApplicationException(
                $"Contrato faturado além do volume contratado. Contratado: {contract.TotalVolume:N3}, " +
                $"alocado: {allocated:N3}, saldo: {balance:N3}. " +
                "Ajuste as alocações na tela de conciliação de saldos antes de encerrar.");
    }

    /// <summary>Regra 15: nenhum firme/devolução de venda com saldo, e todo provisório não cancelado QUITADO.</summary>
    private async Task GuardFinancialDocumentsAsync(SalesContract contract, string userName)
    {
        var hasOpenFirm = await context.FinancialDocuments.AnyAsync(x =>
            x.SalesContractKey == contract.Key &&
            x.OriginType == FinancialDocumentOrigin.SalesInvoice &&
            (x.Nature == FinancialDocumentNature.Firm || x.Nature == FinancialDocumentNature.SalesReturn) &&
            x.Status != FinancialDocumentStatus.Canceled &&
            x.NetAmount > x.SettledAmount);

        if (hasOpenFirm)
            throw new ApplicationException(
                "Há títulos firmes com saldo neste contrato: componha, compense ou baixe por dação antes de encerrar.");

        await CancelLegacyUntouchedProvisionalsAsync(contract, userName);

        var unsettled = await context.FinancialDocuments
            .Where(x => x.SalesContractKey == contract.Key &&
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
                $"O título {unsettledCode} não está quitado: o contrato só encerra com todos os títulos quitados.");
    }

    /// <summary>Regra 15: adiantamento de venda com crédito não aplicado barra o encerramento (mesmo texto da compra).</summary>
    private async Task GuardAdvanceCreditAsync(SalesContract contract)
    {
        var advance = await context.FinancialDocuments.AsNoTracking()
            .Where(x => x.SalesContractKey == contract.Key &&
                        x.Nature == FinancialDocumentNature.Advance &&
                        x.Status != FinancialDocumentStatus.Canceled &&
                        x.SettledAmount - x.AppliedAmount > 0m)
            .OrderBy(x => x.Code)
            .Select(x => new { x.Code, x.SettledAmount, x.AppliedAmount })
            .FirstOrDefaultAsync();

        if (advance is null)
            return;

        var credit = decimal.Round(advance.SettledAmount - advance.AppliedAmount, 2, MidpointRounding.ToEven);
        throw new ApplicationException(
            $"O adiantamento {advance.Code} tem {credit.ToString("N2", CultureInfo.GetCultureInfo("pt-BR"))} de crédito não aplicado: compense, devolva ou vincule a outro contrato antes de encerrar.");
    }

    /// <summary>
    /// Regra 16: provisório criado ANTES da data de corte, sem composição e sem baixa, é cancelado no encerramento como
    /// antes desta fase. Sem a chave (ou inválida), nada é isento.
    /// </summary>
    private async Task CancelLegacyUntouchedProvisionalsAsync(SalesContract contract, string userName)
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
            .Where(x => x.SalesContractKey == contract.Key &&
                        x.Nature == FinancialDocumentNature.Provisional &&
                        x.Status != FinancialDocumentStatus.Canceled &&
                        x.Status != FinancialDocumentStatus.Settled &&
                        x.CreatedAt != null && x.CreatedAt < cutover &&
                        x.ComposedAmount == 0m && x.SettledAmount == 0m)
            .ToListAsync();

        foreach (var provisional in legacy)
        {
            FinancialDocumentsCancelService.Cancel(provisional, PurchaseContractsCloseService.LegacyCancellationReason, userName);
            logger.LogInformation(
                "Encerramento do contrato de venda {Contract}: provisório {Document} anterior à data de corte {Cutover:yyyy-MM-dd} cancelado.",
                contract.Code, provisional.Code, cutover);
        }
    }
}
