using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Services.Taxes;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.PurchaseInvoices;

/// <summary>
/// Documentos de entrada "Complemento de valor" (spec 2026-10-13 D11: o faturado ABAIXO do contrato) e "Complemento de
/// ICMS" (spec 2026-10-17: NF só de imposto, base e valor do ICMS informados). Regra única das linhas, chamada pelos
/// quatro caminhos que gravam linha (deep-insert, PATCH do cabeçalho, POST e PATCH de linha — ver
/// PurchaseInvoiceLineGuard) e de novo na confirmação e na emissão.
///
/// Emissão própria (spec 2026-10-14, estendida ao ICMS pela D2 de 2026-10-17): cada linha aponta o item de uma entrada
/// própria autorizada do mesmo produtor e filial (ComplementedItemKey) e herda dela produto, unidade e natureza — e o
/// contrato, quando o original tem (D5).
///
/// Contrato (D3 de 2026-10-17): em STANDALONE quem manda é a natureza
/// (<c>PurchaseInvoiceLineGuard.EnsureUsageContractRuleAsync</c>); em SAPB1 (natureza sem a flag) o complemento de valor
/// continua exigindo o contrato aqui.
/// </summary>
public static class PurchaseInvoiceComplement
{
    /// <summary>K3: o complemento de ICMS só existe em STANDALONE.</summary>
    internal const string IcmsOnlyInStandaloneMessage = "Complemento de ICMS só existe no modo STANDALONE.";

    /// <summary>C7 generalizada: o vínculo com a nota original só existe no complemento próprio.</summary>
    internal const string OnlyOwnComplementMessage = "O item da nota original só existe no complemento de emissão própria.";

    /// <summary>Complemento próprio fora do modo NF-e (SAPB1, ou filial STANDALONE que não emite pelo Siagro).</summary>
    internal static string OutsideNfeModeMessage(PurchaseInvoiceType type) =>
        $"{Capitalized(PurchaseInvoiceComplements.Label(type))} de emissão própria só existe na filial que emite NF-e pelo Siagro.";

    /// <param name="gate">
    /// O modo: STANDALONE exige natureza com "Gera financeiro" no complemento de valor; em SAPB1 a natureza é o OUSG, sem
    /// a flag (D10), e basta existir. E a filial que emite NF-e pelo Siagro, única onde o complemento PRÓPRIO existe.
    /// </param>
    /// <param name="context">Lê os itens originais do complemento próprio.</param>
    public static async Task EnsureAsync(
        PurchaseInvoice invoice, IReadOnlyList<PurchaseInvoiceItem> lines, IUsage usageService, TaxCalculationGate gate,
        AppDbContext context)
    {
        var type = invoice.InvoiceType;
        var standalone = gate.IsStandalone;

        if (!PurchaseInvoiceComplements.IsComplement(type))
        {
            // C7: o vínculo com a nota original só existe no complemento próprio.
            if (lines.Any(l => l.ComplementedItemKey is not null))
                throw new DefaultException(OnlyOwnComplementMessage);

            var carrying = lines.FirstOrDefault(l => l.ComplementAmount != 0m);
            if (carrying is not null)
                throw new DefaultException(
                    $"Item {carrying.ItemCode}: valor de complemento só existe no documento do tipo Complemento de valor.");
            return;
        }

        // K3 antes de tudo: fora de STANDALONE o tipo nem existe.
        if (type == PurchaseInvoiceType.IcmsComplement && !standalone)
            throw new DefaultException(IcmsOnlyInStandaloneMessage);

        var label = PurchaseInvoiceComplements.Label(type);
        var ownComplement = invoice.IssuerType == DocumentIssuerType.Own;

        // C7: o de terceiro é escrituração da nota dele, sem vínculo com a original.
        if (!ownComplement && lines.Any(l => l.ComplementedItemKey is not null))
            throw new DefaultException(OnlyOwnComplementMessage);

        if (ownComplement)
        {
            // O complemento próprio sai como NF-e complementar (finNFe=2): fora do modo NF-e não há a nota original
            // autorizada que ele referencia, e C1 falaria de um item que a tela nem oferece. Antes de C1.
            if (!await gate.IsActiveAsync(invoice.BranchCode))
                throw new DefaultException(OutsideNfeModeMessage(type));

            await EnsureOwnLinesAsync(invoice, lines, context);

            // D3 sobre a natureza e o contrato JÁ herdados: nos caminhos que gravam linha a regra da natureza roda antes
            // desta herança, quando a linha nova do complemento próprio ainda chega sem natureza, e por isso pula o próprio.
            await PurchaseInvoiceLineGuard.EnsureUsageContractRuleAsync(
                usageService, standalone, invoice, lines, ownComplementInherited: true);
        }

        foreach (var line in lines)
        {
            if (type == PurchaseInvoiceType.IcmsComplement)
                EnsureIcmsLine(line);
            else
                EnsureValueLine(line, standalone);

            if (line.UsageCode is not { } usageCode)
                throw new DefaultException($"Item {line.ItemCode}: o {label} exige a natureza de operação.");

            // D5: o complemento de ICMS não gera título; só o de valor precisa de natureza que gere (STANDALONE).
            if (type == PurchaseInvoiceType.ValueComplement && standalone &&
                (await usageService.GetByIdAsync(usageCode))?.CreatesFinancialDocument != true)
                throw new DefaultException(
                    $"Item {line.ItemCode}: a natureza {usageCode} não gera financeiro; o complemento de valor precisa de uma natureza que gere.");
        }
    }

    /// <summary>Linha do complemento de valor: só o valor do complemento; em SAPB1 o contrato é obrigatório (D3).</summary>
    private static void EnsureValueLine(PurchaseInvoiceItem line, bool standalone)
    {
        if (line.Quantity != 0m || line.UnitPrice != 0m)
            throw new DefaultException(
                $"Item {line.ItemCode}: no complemento de valor a linha não tem quantidade nem preço unitário; informe só o valor do complemento.");

        if (line.ComplementAmount <= 0m)
            throw new DefaultException($"Item {line.ItemCode}: informe o valor do complemento.");

        if (!standalone && line.PurchaseContractKey is null)
            throw new DefaultException($"Item {line.ItemCode}: o complemento de valor exige o contrato de compra na linha.");
    }

    /// <summary>
    /// Linha do complemento de ICMS (D4): sem quantidade, preço nem valor (K1); base e ICMS informados, ambos positivos
    /// (K2); o ICMS não passa da base (K4).
    /// </summary>
    private static void EnsureIcmsLine(PurchaseInvoiceItem line)
    {
        if (line.Quantity != 0m || line.UnitPrice != 0m || line.ComplementAmount != 0m)
            throw new DefaultException(
                $"Item {line.ItemCode}: no complemento de ICMS a linha não tem quantidade, preço nem valor; informe só a base e o valor do ICMS.");

        if (line.IcmsBase <= 0m || line.IcmsValue <= 0m)
            throw new DefaultException($"Item {line.ItemCode}: informe a base e o valor do ICMS do complemento.");

        if (line.IcmsValue > line.IcmsBase)
            throw new DefaultException($"Item {line.ItemCode}: o ICMS do complemento não pode ser maior que a base.");
    }

    private static string Capitalized(string text) => char.ToUpperInvariant(text[0]) + text[1..];

    /// <summary>
    /// Regra 2 / C2: a nota que se complementa — própria, Normal (a contra-nota é uma entrada própria Normal: D1), com a
    /// NF-e autorizada e o documento não cancelado. Só dados do próprio documento. A nota de custódia (entrada para
    /// beneficiamento ou armazenagem, 1.901/1.905) também é própria, Normal e autorizada, mas não é compra: fica de fora.
    /// </summary>
    internal static bool IsComplementableOrigin(PurchaseInvoice origin) =>
        origin.IssuerType == DocumentIssuerType.Own &&
        origin.InvoiceType == PurchaseInvoiceType.Normal &&
        origin.CustodyRole == StorageCustodyRole.None &&
        origin.NfeStatus == NfeStatus.Authorized &&
        origin.InvoiceStatus != InvoiceStatus.Cancelled;

    /// <summary>
    /// "Emitir NF-e" do complemento próprio, antes de reservar o número: a regra das linhas de novo, porque o cadastro
    /// pode ter mudado desde a gravação do rascunho (contrato herdado ou escolhido fora de Aprovado/Encerrado, natureza
    /// sem "Gera financeiro"). Sem isso a SEFAZ autorizaria e a confirmação, que roda depois, falharia: NF-e autorizada
    /// com o documento Pendente e sem título firme.
    ///
    /// Sobre uma cópia SEM rastreamento, como a confirmação: as linhas rastreadas da emissão cairiam no ramo "recusa
    /// divergência", e a cópia não grava nada. A original que deixou de estar autorizada fala a língua da emissão (C6),
    /// não a do rascunho (C2).
    /// </summary>
    internal static async Task EnsureIssuableAsync(
        Guid invoiceKey, IUsage usageService, TaxCalculationGate gate, AppDbContext context)
    {
        var copy = await context.PurchaseInvoices.AsNoTracking().Include(i => i.Items)
            .SingleAsync(i => i.Key == invoiceKey);
        var lines = copy.Items.ToList();

        var originals = await LoadOriginalsAsync(context, lines);
        var lapsed = originals.Values.Select(o => o.PurchaseInvoice)
            .FirstOrDefault(origin => origin is not null && !IsComplementableOrigin(origin));
        if (lapsed is not null)
            throw new DefaultException(NoLongerAuthorizedMessage(lapsed.InvoiceNumber));

        await EnsureAsync(copy, lines, usageService, gate, context);
    }

    /// <summary>C6: a original deixou de estar autorizada entre o rascunho e a emissão.</summary>
    internal static string NoLongerAuthorizedMessage(string? originalNumber) =>
        $"A nota original {originalNumber} não está mais autorizada: o complemento não pode ser emitido.";

    /// <summary>C1: a linha do complemento próprio não aponta um item de nota original.</summary>
    internal static string MissingOriginalMessage(string? itemCode, PurchaseInvoiceType type) =>
        $"Item {(string.IsNullOrWhiteSpace(itemCode) ? "(sem produto)" : itemCode)}: o {PurchaseInvoiceComplements.Label(type)} de emissão própria exige o item da nota original.";

    /// <summary>
    /// D4 / C8: a original não é cancelada (documento nem NF-e) enquanto houver linha de complemento apontando um item
    /// dela num documento não cancelado — o NFref do complemento ficaria referenciando uma NF-e cancelada. Complemento
    /// cancelado não trava.
    /// </summary>
    public static async Task EnsureNoActiveComplementAsync(AppDbContext context, Guid originalKey, string? originalNumber)
    {
        var complement = await context.PurchaseInvoicesItems.AsNoTracking()
            .Where(l => l.ComplementedItemKey != null &&
                        l.ComplementedItem!.PurchaseInvoiceKey == originalKey &&
                        l.PurchaseInvoice!.InvoiceStatus != InvoiceStatus.Cancelled)
            .Select(l => new { l.PurchaseInvoice!.InvoiceNumber })
            .FirstOrDefaultAsync();

        if (complement is not null)
            throw new DefaultException(
                $"A nota {originalNumber} tem o complemento {complement.InvoiceNumber} ativo: cancele o complemento antes.");
    }

    /// <summary>
    /// Os itens originais apontados pelas linhas (ComplementedItemKey), com o documento de cada um e os itens dele (para o
    /// nItem da original), sem rastreamento e indexados pela Key do item original. Linha sem vínculo é ignorada; chave que
    /// não existe simplesmente não aparece no dicionário. Compartilhado com a prontidão da emissão (Task 5).
    /// </summary>
    internal static async Task<IReadOnlyDictionary<Guid, PurchaseInvoiceItem>> LoadOriginalsAsync(
        AppDbContext context, IEnumerable<PurchaseInvoiceItem> lines)
    {
        var keys = lines.Where(l => l.ComplementedItemKey is not null)
            .Select(l => l.ComplementedItemKey!.Value).Distinct().ToList();
        if (keys.Count == 0)
            return new Dictionary<Guid, PurchaseInvoiceItem>();

        // Com resolução de identidade: o Include item → documento → itens é um ciclo, que o AsNoTracking simples recusa.
        return await context.PurchaseInvoicesItems.AsNoTrackingWithIdentityResolution()
            .Include(i => i.PurchaseInvoice!).ThenInclude(p => p.Items)
            .Where(i => i.Key != null && keys.Contains(i.Key.Value))
            .ToDictionaryAsync(i => i.Key!.Value);
    }

    /// <summary>Regras 1–5a (C1–C5, C10). Linha nova herda do original; linha já gravada não pode divergir dele.</summary>
    private static async Task EnsureOwnLinesAsync(
        PurchaseInvoice invoice, IReadOnlyList<PurchaseInvoiceItem> lines, AppDbContext context)
    {
        var missing = lines.FirstOrDefault(l => l.ComplementedItemKey is null);
        if (missing is not null)
            throw new DefaultException(MissingOriginalMessage(missing.ItemCode, invoice.InvoiceType));

        var originals = await LoadOriginalsAsync(context, lines);

        var seen = new HashSet<Guid>();
        foreach (var line in lines)
        {
            var key = line.ComplementedItemKey!.Value;

            // Chave que não existe: a linha não aponta item nenhum (C1) — C2 precisa de um documento para citar.
            if (!originals.TryGetValue(key, out var original) || original.PurchaseInvoice is not { } origin)
                throw new DefaultException(MissingOriginalMessage(line.ItemCode, invoice.InvoiceType));

            if (!IsComplementableOrigin(origin))
                throw new DefaultException(
                    $"Item {original.ItemCode}: a nota original {origin.InvoiceNumber} precisa ser uma entrada própria normal, com NF-e autorizada e não cancelada.");

            // C3 roda também no PATCH do cabeçalho: trocar o produtor ou a filial depois de incluir linhas é recusado.
            if (origin.CardCode != invoice.CardCode || origin.BranchCode != invoice.BranchCode)
                throw new DefaultException(
                    $"Item {original.ItemCode}: a nota original {origin.InvoiceNumber} é de outro produtor ou de outra filial.");

            // Complementos sucessivos do mesmo item são permitidos; o mesmo item duas vezes na MESMA nota, não (C4).
            if (!seen.Add(key))
                throw new DefaultException(
                    $"O item {origin.InvoiceNumber}/{NfeItemNumbering.OriginNumber(original, origin.Items.Count)?.ToString() ?? "?"} já está complementado nesta nota.");

            await InheritOrRefuseAsync(invoice, line, original, context);
        }
    }

    /// <summary>
    /// Regras 5 e 5a. Linha NOVA — Detached (deep-insert, POST de linha, confirmação com AsNoTracking) ou Added (linha
    /// incluída pelo PATCH do cabeçalho) — herda do item original produto, unidade e natureza, e o contrato quando o
    /// original tem. Linha já gravada (rastreada) recusa divergência: C5 nos três campos, C10 no contrato herdado (a tela
    /// trava esses campos, então a divergência só chega pela API).
    ///
    /// Original SEM contrato (D5): o contrato é o que o operador informou, e passa AQUI pelas regras de contrato da linha
    /// de entrada — os quatro caminhos que gravam linha deixam de chamar a guarda para a linha do complemento, para que
    /// ela rode com o produto já herdado e DEPOIS de C3 (trocar o produtor do cabeçalho recusa por C3, não pelo
    /// contrato). Sem contrato, decide adiante a natureza (D3, em STANDALONE) ou, em SAPB1, a regra do complemento de valor. O contrato HERDADO passa
    /// pela mesma guarda: fornecedor e produto já batem (C3 e herança), mas a situação pode ter mudado depois da nota
    /// original (cancelado, por exemplo).
    /// </summary>
    private static async Task InheritOrRefuseAsync(
        PurchaseInvoice invoice, PurchaseInvoiceItem line, PurchaseInvoiceItem original, AppDbContext context)
    {
        if (context.Entry(line).State is EntityState.Detached or EntityState.Added)
        {
            line.ItemCode = original.ItemCode;
            line.ItemName = original.ItemName;
            line.UnitOfMeasureCode = original.UnitOfMeasureCode;
            line.UsageCode = original.UsageCode;
            if (original.PurchaseContractKey is not null)
                line.PurchaseContractKey = original.PurchaseContractKey;
        }
        else
        {
            if (line.ItemCode != original.ItemCode || line.UnitOfMeasureCode != original.UnitOfMeasureCode ||
                line.UsageCode != original.UsageCode)
                throw new DefaultException(
                    $"Item {original.ItemCode}: produto, unidade e natureza vêm do item da nota original e não podem ser alterados.");

            if (original.PurchaseContractKey is not null && line.PurchaseContractKey != original.PurchaseContractKey)
                throw new DefaultException(
                    $"Item {original.ItemCode}: o contrato vem do item da nota original e não pode ser alterado.");
        }

        if (line.PurchaseContractKey is not null)
            await PurchaseInvoiceLineGuard.EnsureContractIsCompatibleAsync(
                context, line.PurchaseContractKey, line.ItemCode, invoice.CardCode);
    }
}
