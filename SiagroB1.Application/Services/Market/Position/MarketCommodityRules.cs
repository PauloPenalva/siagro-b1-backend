using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities.Market;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Market.Position;

/// <summary>
/// Normalização e regras de commodity, produto e mês de vencimento. Compartilhadas pela gravação avulsa (serviços de
/// itens e meses) e pela inclusão em profundidade (commodity + itens + meses), para as vias nunca divergirem.
/// </summary>
public static class MarketCommodityRules
{
    private static readonly char[] ForbiddenCodeChars = ['/', '\\', '\'', '%', '?', '#'];

    /// <summary>Normaliza e valida os campos do cabeçalho (sem consultar o banco).</summary>
    public static void NormalizeHeader(MarketCommodity entity)
    {
        entity.Code = entity.Code?.Trim() ?? string.Empty;
        if (entity.Code.Length == 0)
            throw new BusinessException("Informe o código da commodity.");
        if (entity.Code.Length > 20)
            throw new BusinessException("O código da commodity deve ter no máximo 20 caracteres.");
        // O código vai na URL do OData (chave): caracteres que quebram a rota ficam de fora.
        if (entity.Code.IndexOfAny(ForbiddenCodeChars) >= 0)
            throw new BusinessException("O código da commodity não pode conter / \\ ' % ? #.");

        entity.Name = entity.Name?.Trim() ?? string.Empty;
        if (entity.Name.Length == 0)
            throw new BusinessException("Informe o nome da commodity.");
        if (entity.Name.Length > 60)
            throw new BusinessException("O nome da commodity deve ter no máximo 60 caracteres.");

        if (!Enum.IsDefined(entity.HedgeExchange))
            throw new BusinessException("Informe a bolsa de proteção da commodity.");
    }

    /// <summary>Valida o mês de vencimento: 1–12, deslocamento 0 ou 1 (sem consultar o banco).</summary>
    public static void NormalizeMonth(MarketCommodityMonth month)
    {
        if (month.DeliveryMonth is < 1 or > 12)
            throw new BusinessException("O mês de entrega deve estar entre 1 e 12.");
        if (month.FuturesMonth is < 1 or > 12)
            throw new BusinessException("O mês de bolsa deve estar entre 1 e 12.");
        if (month.YearOffset is not (0 or 1))
            throw new BusinessException("O deslocamento de ano deve ser 0 (mesmo ano) ou 1 (ano seguinte).");
    }

    public static void NormalizeItemCode(MarketCommodityItem item)
    {
        item.ItemCode = item.ItemCode?.Trim() ?? string.Empty;
        if (item.ItemCode.Length == 0)
            throw new BusinessException("Informe o produto.");
        if (item.ItemCode.Length > 10)
            throw new BusinessException("O código do produto deve ter no máximo 10 caracteres.");
    }

    /// <summary>
    /// Confere o produto no cadastro de itens (vale em STANDALONE e SAPB1) e preenche o nome a partir dele: o nome
    /// que o cliente mandar é sempre descartado.
    /// </summary>
    public static async Task FillItemNameAsync(IItemService itemService, MarketCommodityItem item)
    {
        var master = await itemService.GetByIdAsync(item.ItemCode)
                     ?? throw new BusinessException($"Produto {item.ItemCode} não encontrado.");
        var name = master.ItemName?.Trim();
        item.ItemName = string.IsNullOrEmpty(name) ? null : name.Length > 200 ? name[..200] : name;
    }

    /// <summary>Um produto só pode estar em uma commodity: nomeia a outra em vez de deixar vazar o índice único.</summary>
    public static async Task EnsureItemFreeAsync(AppDbContext context, MarketCommodityItem item)
    {
        var other = await context.MarketCommodityItems.AsNoTracking()
            .Where(i => i.Id != item.Id && i.ItemCode == item.ItemCode)
            .Select(i => i.CommodityCode).FirstOrDefaultAsync();
        if (other is not null)
            throw new BusinessException($"O produto {item.ItemCode} já pertence à commodity {other}.");
    }

    public static Task<bool> CommodityExistsAsync(AppDbContext context, string code) =>
        context.MarketCommodities.AsNoTracking().AnyAsync(c => c.Code == code);

}
