using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.PurchaseInvoices;

/// <summary>
/// Regras compartilhadas pelos serviços de LINHA do documento de entrada.
///
/// Existem porque a grade da tela grava por POST/PATCH/DELETE direto em
/// <c>/PurchaseInvoicesItems</c>, sem passar pelo <c>PurchaseInvoicesUpdateService</c>. Deixar a
/// guarda só no cabeçalho abriria a porta dos fundos para alterar documento confirmado.
/// </summary>
internal static class PurchaseInvoiceLineGuard
{
    public static async Task EnsureParentIsPendingAsync(IUnitOfWork db, Guid? purchaseInvoiceKey)
    {
        if (purchaseInvoiceKey is null)
            throw new DefaultException("Linha sem documento de entrada informado.");

        var status = await db.Context.PurchaseInvoices
            .Where(x => x.Key == purchaseInvoiceKey)
            .Select(x => (InvoiceStatus?)x.InvoiceStatus)
            .FirstOrDefaultAsync();

        if (status is null)
            throw new NotFoundException("Documento de entrada não encontrado.");

        if (status != InvoiceStatus.Pending)
            throw new DefaultException(
                "Somente documento pendente pode ter as linhas alteradas. " +
                "Estorne a confirmação antes.");
    }

    /// <summary>
    /// Resolve a descrição do produto SÓ quando ela não veio — ou quando o produto MUDOU.
    ///
    /// A descrição lida do XML é a que CONSTA NA NOTA e vale mais que a do cadastro; e o código do
    /// emitente pode nem existir no cadastro local, caso em que sobrescrever apagaria a descrição
    /// em vez de melhorá-la.
    ///
    /// "Não veio" inclui VAZIO, não só null: o <c>create()</c> do UI5 é obrigado a declarar toda
    /// propriedade editável para a primeira digitação não abrir "Must not change a property before
    /// it has been read", então a linha chega com <c>""</c>. E ela chega assim mesmo quando o
    /// operador escolheu o produto no value help: a descrição é copiada para a tela com group ID
    /// <c>null</c> de propósito (campo desnormalizado é do servidor) e NÃO entra no POST/PATCH.
    /// Sem esta resolução no servidor, a descrição aparecia na tela e gravava em branco.
    /// </summary>
    /// <param name="itemCodeChanged">
    /// Verdadeiro quando o produto da linha foi TROCADO. Nesse caso a descrição que chegou é a do
    /// produto ANTERIOR, pelo mesmo motivo acima — copiá-la deixaria a linha com o código de um
    /// produto e a descrição de outro.
    /// </param>
    public static async Task<string?> ResolveItemNameAsync(
        IItemService itemService, string? itemCode, string? itemName, bool itemCodeChanged = false)
    {
        if (string.IsNullOrWhiteSpace(itemCode))
            return itemName;

        if (!itemCodeChanged)
            return string.IsNullOrWhiteSpace(itemName)
                ? (await itemService.GetByIdAsync(itemCode))?.ItemName
                : itemName;

        // Produto trocado por um código fora do cadastro (o value help filtra por grupo, o XML
        // não): manter a descrição que veio é melhor que esvaziar a linha.
        return (await itemService.GetByIdAsync(itemCode))?.ItemName ?? itemName;
    }

    /// <summary>
    /// A unidade de medida é obrigatória na linha (B7): sem ela a devolução copiava a unidade vazia e a NF-e recusava.
    /// Vale sobre a lista FINAL de linhas do documento, na ordem em que a grade as mostra (nº = posição na carga, a partir de 1; linha nova no fim). Complemento
    /// de valor fica fora: a linha só tem valor (quantidade 0), então não há unidade a informar.
    /// </summary>
    public static void EnsureUnitOfMeasure(PurchaseInvoice invoice, IReadOnlyList<PurchaseInvoiceItem> lines)
    {
        if (invoice.InvoiceType == PurchaseInvoiceType.ValueComplement)
            return;

        // Devolução com NF-e e contra-nota: a unidade da linha vem da origem e a tela a trava (RestoreReturnLine /
        // RestoreCounterNoteLine) — exigir aqui prenderia o documento sem o usuário poder corrigir. A origem sem
        // unidade é recusada ANTES de criar (Devolver / Gerar contra-nota).
        if (invoice.IsNfeReturn || invoice.ProducerInvoiceKey is not null)
            return;

        for (var i = 0; i < lines.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i].UnitOfMeasureCode))
                throw new DefaultException($"Informe a unidade de medida da linha {i + 1} ({lines[i].ItemCode}).");
        }
    }

    /// <summary>
    /// "Outra compra" (spec 2026-10-17 §1): natureza de entrada que NÃO exige contrato e gera financeiro — serviço, uso e
    /// consumo, insumo, ativo. O caminho do grão (contrato) nunca cai aqui.
    /// </summary>
    public static bool IsOtherPurchase(UsageModel usage) => !usage.RequiresContract && usage.CreatesFinancialDocument;

    /// <summary>
    /// O documento inteiro é de outra compra: STANDALONE, tipo Normal, não é devolução e TODA linha com natureza é
    /// outra compra (há ao menos uma). Devolução e complemento de valor seguem o caminho de hoje.
    /// </summary>
    public static Task<bool> IsOtherPurchaseDocumentAsync(
        IUsage usages, IConfiguration cfg, PurchaseInvoice invoice, IReadOnlyList<PurchaseInvoiceItem> lines) =>
        IsOtherPurchaseDocumentAsync(usages, ErpMode.IsStandalone(cfg), invoice, lines);

    internal static async Task<bool> IsOtherPurchaseDocumentAsync(
        IUsage usages, bool standalone, PurchaseInvoice invoice, IReadOnlyList<PurchaseInvoiceItem> lines)
    {
        if (!standalone || invoice.InvoiceType != PurchaseInvoiceType.Normal || invoice.IsNfeReturn)
            return false;

        var byCode = await LoadUsagesAsync(usages, lines);
        var withUsage = lines.Where(l => l.UsageCode is not null && byCode.ContainsKey(l.UsageCode.Value)).ToList();

        return withUsage.Count > 0 && withUsage.All(l => IsOtherPurchase(byCode[l.UsageCode!.Value]));
    }

    /// <summary>
    /// A natureza da linha decide se o contrato é exigido (spec 2026-10-17 §1): exige → contrato obrigatório; não exige →
    /// contrato recusado; e o documento não mistura linhas de contrato com linhas de outras compras.
    ///
    /// Só STANDALONE e tipo Normal: em SAPB1 a natureza (OUSG) não carrega a flag, o complemento de valor já exige contrato
    /// (<c>PurchaseInvoiceValueComplement</c>) e a devolução mantém o contrato opcional. Linha sem natureza passa sem regra.
    /// Recebe a lista FINAL de linhas do documento, porque a mistura é uma regra do conjunto.
    /// </summary>
    public static Task EnsureUsageContractRuleAsync(
        IUnitOfWork db, IUsage usages, IConfiguration cfg, PurchaseInvoice invoice, IReadOnlyList<PurchaseInvoiceItem> lines) =>
        EnsureUsageContractRuleAsync(usages, ErpMode.IsStandalone(cfg), invoice, lines);

    internal static async Task EnsureUsageContractRuleAsync(
        IUsage usages, bool standalone, PurchaseInvoice invoice, IReadOnlyList<PurchaseInvoiceItem> lines)
    {
        if (!standalone || invoice.InvoiceType != PurchaseInvoiceType.Normal || invoice.IsNfeReturn)
            return;

        // Valor de complemento em documento Normal é recusado pela regra do complemento (EnsureValueComplementAsync), com
        // a mensagem certa; esta regra não se antecipa a ela.
        if (lines.Any(l => l.ComplementAmount != 0m))
            return;

        var byCode = await LoadUsagesAsync(usages, lines);

        foreach (var line in lines)
        {
            if (line.UsageCode is null || !byCode.TryGetValue(line.UsageCode.Value, out var usage))
                continue;

            if (usage.RequiresContract && line.PurchaseContractKey is null)
                throw new DefaultException(
                    $"A natureza {usage.Name} exige contrato de compra na linha do produto {line.ItemCode}.");

            if (!usage.RequiresContract && line.PurchaseContractKey is not null)
                throw new DefaultException(
                    $"A natureza {usage.Name} não aceita contrato de compra: retire o contrato da linha do produto {line.ItemCode}.");
        }

        var hasContractLine = lines.Any(l => l.PurchaseContractKey is not null);
        var hasOtherPurchaseLine = lines.Any(l =>
            l.UsageCode is not null && byCode.TryGetValue(l.UsageCode.Value, out var u) && IsOtherPurchase(u));

        if (hasContractLine && hasOtherPurchaseLine)
            throw new DefaultException("Um documento não mistura linhas de contrato com linhas de outras compras.");
    }

    private static async Task<Dictionary<int, UsageModel>> LoadUsagesAsync(
        IUsage usages, IReadOnlyList<PurchaseInvoiceItem> lines)
    {
        var codes = lines.Where(l => l.UsageCode is not null).Select(l => l.UsageCode!.Value).ToHashSet();
        var result = new Dictionary<int, UsageModel>();
        foreach (var code in codes)
        {
            var usage = await usages.GetByIdAsync(code);
            if (usage is not null)
                result[code] = usage;
        }
        return result;
    }

    /// <summary>
    /// Valida a amarração da linha com o contrato de compra.
    ///
    /// Mora aqui, e não em um serviço, porque são TRÊS os caminhos que gravam linha — deep-insert
    /// do documento, POST de linha e PATCH de linha —, mais o SyncItems do cabeçalho, que cobre
    /// as linhas que chegam no payload do PATCH do cabeçalho. A grade da tela alcança cada um por
    /// uma ação diferente. Deixar a regra em um só deles é o erro previsível: já aconteceu com a
    /// re-resolução da descrição do produto, que ficou só no SyncItems e não valia para a troca de
    /// produto pela grade do Edit.
    ///
    /// Linha SEM contrato passa: o campo é opcional por decisão de projeto.
    /// </summary>
    public static Task EnsureContractIsCompatibleAsync(
        IUnitOfWork db, Guid? purchaseContractKey, string? itemCode, string cardCode) =>
        EnsureContractIsCompatibleAsync(db.Context, purchaseContractKey, itemCode, cardCode);

    /// <summary>
    /// Mesma regra, para quem só tem o contexto: a linha do complemento próprio (spec 2026-10-14, D5) roda a guarda de
    /// dentro de <c>PurchaseInvoiceValueComplement</c>, depois de herdar o produto e de C3.
    /// </summary>
    public static async Task EnsureContractIsCompatibleAsync(
        AppDbContext context, Guid? purchaseContractKey, string? itemCode, string cardCode)
    {
        if (purchaseContractKey is null)
            return;

        var contract = await context.PurchaseContracts
            .AsNoTracking()
            .Where(x => x.Key == purchaseContractKey)
            .Select(x => new { x.Code, x.CardCode, x.ItemCode, x.Status })
            .FirstOrDefaultAsync();

        if (contract is null)
            throw new DefaultException("Contrato de compra informado na linha não foi encontrado.");

        // Encerrado ENTRA de propósito: a NF chega com frequência depois de o contrato fechar, e
        // recusá-la deixaria essa nota sem como ser conciliada.
        if (contract.Status != ContractStatus.Approved && contract.Status != ContractStatus.Finished)
            throw new DefaultException(
                $"O contrato {contract.Code}, amarrado na linha do produto {itemCode}, não está " +
                "aprovado nem encerrado e não pode ser amarrado a um documento de entrada.");

        if (contract.CardCode != cardCode)
            throw new DefaultException(
                $"O contrato {contract.Code}, amarrado na linha do produto {itemCode}, é de " +
                "outro fornecedor e não pode ser amarrado a este documento.");

        if (contract.ItemCode != itemCode)
            throw new DefaultException(
                $"O contrato {contract.Code}, amarrado na linha do produto {itemCode}, é de " +
                "outro produto e não pode ser amarrado a esta linha.");
    }
}
