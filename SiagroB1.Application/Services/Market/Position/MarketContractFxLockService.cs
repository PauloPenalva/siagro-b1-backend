using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Interfaces;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Market.Position;

/// <summary>
/// Marca o câmbio de um contrato aprovado como fechado (ou o reabre): um contrato com câmbio fechado sai da
/// exposição em dólar. A gravação é direcionada (só <c>MarketFxLocked</c> e, se informada, <c>MarketFxRate</c>), sem
/// passar pelos serviços de atualização do contrato; o <c>RowVersion</c> do contrato protege contra edição simultânea.
/// </summary>
public class MarketContractFxLockService(
    AppDbContext context, MarketPermissionGuard guard,
    PurchaseContractsChangeLogService purchaseLog, SalesContractsChangeLogService salesLog)
{
    public const string ConcurrencyMessage = "O contrato foi alterado por outro usuário. Atualize e tente de novo.";

    private static readonly CultureInfo PtBr = new("pt-BR");

    public async Task SetAsync(MarketEfpContractSide side, Guid contractKey, bool locked, decimal? fxRate,
        string userName, CancellationToken ct = default)
    {
        await guard.EnsureAsync(userName, PermissionCodes.MarketManage);

        decimal? rate = null;
        if (fxRate.HasValue)
        {
            rate = Math.Round(fxRate.Value, 6, MidpointRounding.AwayFromZero);
            if (rate <= 0)
                throw new BusinessException("A taxa de câmbio deve ser maior que zero.");
        }

        try
        {
            if (side == MarketEfpContractSide.Purchase)
                await SetPurchaseAsync(contractKey, locked, rate, userName, ct);
            else
                await SetSalesAsync(contractKey, locked, rate, userName, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            context.ChangeTracker.Clear();
            throw new BusinessException(ConcurrencyMessage);
        }
    }

    private async Task SetPurchaseAsync(Guid key, bool locked, decimal? rate, string userName, CancellationToken ct)
    {
        var contract = await context.PurchaseContracts.FirstOrDefaultAsync(c => c.Key == key, ct)
                       ?? throw new KeyNotFoundException("Contrato de compra não encontrado.");
        EnsureApproved(contract.Status);
        if (!Apply(contract.MarketFxLocked, contract.MarketFxRate, locked, rate, out var message))
            return;

        contract.MarketFxLocked = locked;
        if (rate.HasValue)
            contract.MarketFxRate = rate;
        purchaseLog.Register(key, "Câmbio", null, message, userName);
        await context.SaveChangesAsync(ct);
    }

    private async Task SetSalesAsync(Guid key, bool locked, decimal? rate, string userName, CancellationToken ct)
    {
        var contract = await context.SalesContracts.FirstOrDefaultAsync(c => c.Key == key, ct)
                       ?? throw new KeyNotFoundException("Contrato de venda não encontrado.");
        EnsureApproved(contract.Status);
        if (!Apply(contract.MarketFxLocked, contract.MarketFxRate, locked, rate, out var message))
            return;

        contract.MarketFxLocked = locked;
        if (rate.HasValue)
            contract.MarketFxRate = rate;
        salesLog.Register(key, "Câmbio", null, message, userName);
        await context.SaveChangesAsync(ct);
    }

    private static void EnsureApproved(ContractStatus? status)
    {
        if (status != ContractStatus.Approved)
            throw new BusinessException("O câmbio só pode ser marcado em contrato aprovado.");
    }

    /// <summary>Diz se algo muda e monta o texto do log; desfazer mantém a taxa gravada.</summary>
    private static bool Apply(bool currentLocked, decimal? currentRate, bool locked, decimal? rate, out string message)
    {
        var rateChanged = rate.HasValue && rate != currentRate;
        if (!locked)
        {
            message = "Câmbio reaberto";
            return currentLocked;
        }

        var shown = rate ?? currentRate;
        message = shown.HasValue
            ? $"Câmbio fechado a {shown.Value.ToString("0.0000", PtBr)}"
            : "Câmbio fechado";
        return !currentLocked || rateChanged;
    }
}
