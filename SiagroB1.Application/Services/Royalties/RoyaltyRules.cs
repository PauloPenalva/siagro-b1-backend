using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Royalties;

/// <summary>
/// Regras do controle de royalties (ITS) compartilhadas por ticket de pesagem, romaneio e baixa.
/// </summary>
public static class RoyaltyRules
{
    public static async Task<bool> ControlsRoyaltiesAsync(AppDbContext ctx, string? itemCode) =>
        !string.IsNullOrWhiteSpace(itemCode) &&
        await ctx.ItemComplements.AnyAsync(x => x.ItemCode == itemCode && x.ControlsRoyalties);

    public static void EnsureFilled(RoyaltySituation? situation, string? originCode, string? testCode)
    {
        if (situation is null || string.IsNullOrWhiteSpace(originCode))
            throw new ApplicationException("Informe a situação de royalty e a origem da carga.");

        if (situation == RoyaltySituation.Tested && string.IsNullOrWhiteSpace(testCode))
            throw new ApplicationException("Informe o código do teste para a situação Testada.");
    }

    /// <summary>O código do teste só existe na situação Testada.</summary>
    public static string? NormalizeTestCode(RoyaltySituation? situation, string? testCode) =>
        situation == RoyaltySituation.Tested && !string.IsNullOrWhiteSpace(testCode) ? testCode.Trim() : null;

    public static async Task EnsureNoActiveWriteOffAsync(AppDbContext ctx, Guid storageTransactionKey)
    {
        var itsId = await ctx.RoyaltyWriteOffLines
            .Where(l => l.StorageTransactionKey == storageTransactionKey &&
                        l.RoyaltyWriteOff!.Status == RoyaltyWriteOffStatus.Active)
            .Select(l => l.RoyaltyWriteOff!.ItsId)
            .FirstOrDefaultAsync();

        if (itsId != null)
            throw new ApplicationException($"Romaneio possui baixa ITS ativa ({itsId}); cancele a baixa antes.");
    }
}
