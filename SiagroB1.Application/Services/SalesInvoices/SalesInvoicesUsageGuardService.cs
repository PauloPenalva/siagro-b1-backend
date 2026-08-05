using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Models;

namespace SiagroB1.Application.Services.SalesInvoices;

/// <summary>Natureza de operação já resolvida de uma linha do documento.</summary>
public sealed record SalesInvoiceItemUsage(SalesInvoiceItem Item, UsageModel Usage);

/// <summary>
/// Obrigatoriedade condicional do documento de saída, dirigida pela natureza de operação.
///
/// A natureza é de LINHA, como no SAP: cada item tem a sua, e por isso cada item resolve o
/// próprio CFOP e produz o próprio efeito no contrato. Um documento pode misturar naturezas.
///
/// Validado AQUI, e não na tela: os campos do formulário são todos visíveis, e quem recusa o
/// documento incompleto é o serviço — em qualquer caminho que o crie ou confirme.
///
/// Devolve a natureza de cada linha para o chamador não buscá-la de novo. Indexado pela
/// INSTÂNCIA do item, e não pela chave: na criação os itens ainda não têm chave.
/// </summary>
public class SalesInvoicesUsageGuardService(IUsage usageService)
{
    public async Task<IReadOnlyList<SalesInvoiceItemUsage>> ValidateAsync(SalesInvoice invoice)
    {
        var resolved = new List<SalesInvoiceItemUsage>();
        var cache = new Dictionary<int, UsageModel>();

        foreach (var item in invoice.Items)
        {
            var usage = await ResolveForItemAsync(invoice, item, cache);

            // O caminho de romaneio resolve a natureza padrão aqui; gravar de volta é o que
            // faz a linha nascer com ela (a coluna só é nulável por causa do legado).
            item.UsageCode = usage.Code;

            ValidateItem(item, usage);

            resolved.Add(new SalesInvoiceItemUsage(item, usage));
        }

        ValidateWeight(invoice, resolved);

        return resolved;
    }

    private async Task<UsageModel> ResolveForItemAsync(
        SalesInvoice invoice, SalesInvoiceItem item, Dictionary<int, UsageModel> cache)
    {
        if (item.UsageCode is not { } usageCode)
        {
            return await ResolveDefaultForShipmentBillingAsync(invoice);
        }

        if (cache.TryGetValue(usageCode, out var cached))
        {
            return cached;
        }

        var usage = await usageService.GetByIdAsync(usageCode)
                    ?? throw new DefaultException("Natureza de operação não encontrada.");

        cache[usageCode] = usage;

        return usage;
    }

    private static void ValidateItem(SalesInvoiceItem item, UsageModel usage)
    {
        if (usage.Inactive)
        {
            throw new DefaultException($"Natureza de operação {usage.Name} está inativa.");
        }

        // Natureza sem efeito CONFIGURADO é barrada em vez de valer "não altera nada".
        // Em SAPB1 as naturezas chegam do OUSG sem efeito nenhum: deixá-las passar faria o
        // documento nascer sem tocar no contrato, em silêncio — exatamente o default
        // silencioso que o desenho proíbe.
        if (!usage.HasConfiguredEffects)
        {
            throw new DefaultException(
                $"Natureza de operação {usage.Name} está sem efeito configurado. " +
                "Configure o efeito no cadastro de Naturezas de Operação antes de usá-la.");
        }

        if (usage.RequiresContract && item.SalesContractKey is null)
        {
            throw new DefaultException(
                $"Natureza de operação {usage.Name} exige contrato de venda no item {item.ItemCode}.");
        }

        if (usage.RequiresQuantity && item.Quantity <= 0)
        {
            throw new DefaultException(
                $"Natureza de operação {usage.Name} exige quantidade no item {item.ItemCode}.");
        }
    }

    /// <summary>
    /// Peso é do CABEÇALHO, então basta UMA linha exigir para o documento ter que informá-lo.
    /// </summary>
    private static void ValidateWeight(
        SalesInvoice invoice, IReadOnlyList<SalesInvoiceItemUsage> resolved)
    {
        var demanding = resolved.FirstOrDefault(r => r.Usage.RequiresWeight);

        if (demanding is not null && (invoice.GrossWeight <= 0 || invoice.NetWeight <= 0))
        {
            throw new DefaultException(
                $"Natureza de operação {demanding.Usage.Name} exige peso bruto e peso líquido.");
        }
    }

    /// <summary>
    /// O faturamento de romaneio não escolhe natureza em tela: usa a marcada como padrão.
    /// A regra vale SÓ para o documento nascido de romaneio — no avulso a natureza é sempre
    /// explícita, senão um erro de tela viraria efeito silencioso no contrato.
    /// </summary>
    private async Task<UsageModel> ResolveDefaultForShipmentBillingAsync(SalesInvoice invoice)
    {
        if (invoice.SalesTransactions.Count == 0)
        {
            throw new DefaultException("Natureza de operação é obrigatória.");
        }

        var usages = await usageService.GetAllAsync();

        return usages.FirstOrDefault(u => u is { IsDefault: true, Inactive: false })
               ?? throw new DefaultException(
                   "Nenhuma natureza de operação padrão está cadastrada. " +
                   "Marque uma natureza como padrão para faturar romaneio.");
    }
}
