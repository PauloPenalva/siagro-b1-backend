using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.StorageDeposits;

/// <summary>
/// Linha de retorno de depósito no Documento de Entrada (spec 2026-10-10 §3.3, D7). Recebe TODAS as linhas que o
/// documento terá depois da operação e exclui o próprio documento do retornado no banco — assim a mesma conta vale
/// para o deep-insert, o SyncItems do cabeçalho e os POST/PATCH de linha, e editar uma linha não conta a versão antiga.
/// </summary>
public static class StorageDepositReturnGuard
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static async Task EnsureAsync(
        AppDbContext ctx, string cardCode, Guid? purchaseInvoiceKey, IReadOnlyList<PurchaseInvoiceItem> documentLines)
    {
        var usageCodes = documentLines.Where(l => l.UsageCode != null).Select(l => l.UsageCode!.Value).Distinct().ToList();
        if (usageCodes.Count == 0)
            return;

        var roles = await ctx.UsageEffects.AsNoTracking()
            .Where(e => usageCodes.Contains(e.UsageCode) && e.StorageDepositRole != StorageDepositRole.None)
            .ToDictionaryAsync(e => e.UsageCode, e => e.StorageDepositRole);

        if (roles.Count == 0)
            return;

        var returnLines = new List<PurchaseInvoiceItem>();
        foreach (var line in documentLines.Where(l => l.UsageCode != null && roles.ContainsKey(l.UsageCode.Value)))
        {
            if (roles[line.UsageCode!.Value] == StorageDepositRole.Remittance)
                throw new DefaultException("Natureza de remessa para depósito não pode ser usada no documento de entrada.");

            if (line.SalesInvoiceItemKey is null)
                throw new DefaultException(
                    $"Linha {line.ItemCode} com natureza de retorno de depósito precisa indicar a remessa.");

            returnLines.Add(line);
        }

        var remittanceKeys = returnLines.Select(l => l.SalesInvoiceItemKey!.Value).Distinct().ToList();
        var remittances = await ctx.SalesInvoicesItems.AsNoTracking()
            .Where(i => i.Key != null && remittanceKeys.Contains(i.Key.Value)
                        && i.SalesInvoice!.ShipmentReleaseKey != null
                        && i.SalesInvoice.InvoiceStatus != InvoiceStatus.Cancelled)
            .Select(i => new
            {
                Key = i.Key!.Value, i.ItemCode, i.Quantity, i.SalesInvoice!.CardCode,
                Number = i.SalesInvoice.TaxDocumentNumber, i.SalesInvoice.InvoiceNumber,
            })
            .ToDictionaryAsync(x => x.Key);

        var returned = await StorageDepositRules.ReturnedByRemittanceItemAsync(ctx, remittanceKeys, purchaseInvoiceKey);

        foreach (var group in returnLines.GroupBy(l => l.SalesInvoiceItemKey!.Value))
        {
            var first = group.First();
            if (!remittances.TryGetValue(group.Key, out var remittance))
                throw new DefaultException(
                    $"A linha {first.ItemCode} aponta um documento que não é remessa para depósito ativa.");

            var number = StorageDepositRules.DocumentNumber(remittance.Number, remittance.InvoiceNumber);

            if (remittance.CardCode != cardCode)
                throw new DefaultException(
                    $"O retorno precisa ser do armazém {remittance.CardCode}, destinatário da remessa nº {number}.");

            foreach (var line in group.Where(l => l.ItemCode != remittance.ItemCode))
                throw new DefaultException(
                    $"A linha {line.ItemCode} é de outro produto que a remessa nº {number} ({remittance.ItemCode}).");

            var available = remittance.Quantity - returned.GetValueOrDefault(group.Key);
            var requested = group.Sum(l => l.Quantity);

            if (requested > available)
                throw new DefaultException(
                    $"Retorno de depósito: a quantidade ({requested.ToString("N3", PtBr)}) passa do saldo a retornar " +
                    $"da remessa nº {number} ({available.ToString("N3", PtBr)}).");
        }
    }
}
