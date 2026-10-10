using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
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

    /// <summary>
    /// Romaneios que exigem a situação de royalty ao confirmar (quando o produto controla):
    /// Recebimento e Compra. Fica isenta a Compra da Conferência de saldo de armazém (movimento
    /// de papel). A isenção da Compra de troca de liberação de embarque anterior ao controle é
    /// outra: ver <see cref="IsLegacyReleaseChangeLeg"/>.
    /// </summary>
    public static bool RequiresRoyalty(StorageTransaction st) =>
        st.TransactionType == StorageTransactionType.Receipt ||
        (st.TransactionType == StorageTransactionType.Purchase &&
         st.TransactionOrigin != TransactionCode.WarehouseReconciliation);

    /// <summary>
    /// Compra gerada por troca de liberação que, mesmo depois dos padrões do armazém, segue sem
    /// situação: veio de um embarque anterior ao controle de royalties. O usuário não tem como
    /// preencher uma compra que o sistema criou, então só a exigência de preenchimento é
    /// dispensada; os padrões do armazém participante continuam valendo. Avaliar DEPOIS de
    /// <see cref="ApplyPurchaseDefaultsAsync"/>.
    /// </summary>
    public static bool IsLegacyReleaseChangeLeg(StorageTransaction st) =>
        st.ShippingReleaseChangeKey != null && st.RoyaltySituation is null;

    /// <summary>
    /// Nome da origem da carga: na Compra a origem é o armazém do carregamento; nos demais, o
    /// parceiro do código (se o cadastro não o achar, mantém o nome atual).
    /// </summary>
    public static async Task<string?> ResolveOriginNameAsync(
        StorageTransaction st, Func<string, Task<string?>> findPartnerName)
    {
        if (st.TransactionType == StorageTransactionType.Purchase)
            return st.WarehouseName;

        if (string.IsNullOrWhiteSpace(st.RoyaltyOriginCode))
            return null;

        return await findPartnerName(st.RoyaltyOriginCode) ?? st.RoyaltyOriginName;
    }

    /// <summary>
    /// Na Compra (carregamento em terceiro) a origem é o armazém do carregamento, já gravado no
    /// romaneio. Sem situação informada, armazém participante assume Armazém Participante.
    /// </summary>
    public static async Task ApplyPurchaseDefaultsAsync(AppDbContext ctx, StorageTransaction st)
    {
        st.RoyaltyOriginCode = st.WarehouseCode;
        st.RoyaltyOriginName = st.WarehouseName;

        if (st.RoyaltySituation is null && !string.IsNullOrWhiteSpace(st.WarehouseCode) &&
            await ctx.WarehouseComplements.AnyAsync(w => w.WarehouseCode == st.WarehouseCode && w.IsParticipant))
            st.RoyaltySituation = RoyaltySituation.ParticipantWarehouse;

        st.RoyaltyTestCode = NormalizeTestCode(st.RoyaltySituation, st.RoyaltyTestCode);
    }

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
