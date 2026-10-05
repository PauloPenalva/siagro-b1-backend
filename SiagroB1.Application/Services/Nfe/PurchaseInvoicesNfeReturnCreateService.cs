using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Services.Taxes;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Nfe;

public sealed record PurchaseInvoiceNfeReturnItem(Guid OriginItemKey, decimal Quantity, int? ItemNumber = null);

public sealed record PurchaseInvoiceNfeReturnRequest(
    Guid PurchaseInvoiceKey, IReadOnlyList<PurchaseInvoiceNfeReturnItem> Items, string Reason);

/// <summary>
/// "Devolver" do Documento de Entrada (spec §9.2): a partir de uma entrada própria autorizada pelo Siagro ou de uma
/// entrada de terceiro confirmada (NF-e do fornecedor, spec terceiro §8), cria a devolução de compra Pendente que sai
/// com NF-e PRÓPRIA de saída (finalidade 4). Não confirma (quem confirma é a emissão). A única escrita na entrada de
/// origem é o número do item do fornecedor digitado no diálogo (spec terceiro D6); o saldo devolvível é calculado das
/// devoluções (D12).
/// </summary>
public class PurchaseInvoicesNfeReturnCreateService(
    IUnitOfWork db,
    TaxCalculationGate gate,
    PurchaseInvoicesCreateService createService,
    Func<DateTimeOffset>? clock = null,
    TimeZoneInfo? storageZone = null)
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    private readonly Func<DateTimeOffset> _now = clock ?? NfeIssueInputAssembler.BrasiliaNow;

    // Mesmo fuso em que o OData grava as datas (o do servidor): a emissão o converte de volta para Brasília.
    private readonly TimeZoneInfo _storageZone = storageZone ?? TimeZoneInfo.Local;

    public async Task<PurchaseInvoice> ExecuteAsync(PurchaseInvoiceNfeReturnRequest request, string userName)
    {
        var origin = await db.Context.PurchaseInvoices.AsNoTracking().Include(i => i.Items)
                         .FirstOrDefaultAsync(i => i.Key == request.PurchaseInvoiceKey)
                     ?? throw new NotFoundException("Documento de entrada não encontrado.");

        // TODA a validação antes de qualquer escrita.
        var thirdParty = await ValidateOriginAsync(origin);

        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new DefaultException("Informe o motivo da devolução.");

        var quantities = await ResolveQuantitiesAsync(origin, request.Items);
        var returnUsages = thirdParty
            ? await ResolveThirdPartyUsageAsync(origin, quantities.Keys)
            : await ResolveReturnUsagesAsync(origin, quantities.Keys);
        var typedNumbers = thirdParty
            ? await ResolveThirdPartyItemNumbersAsync(origin, request.Items, quantities.Keys)
            : new Dictionary<Guid, int>();

        if (!thirdParty)
            EnsureEntryItemNumbers(origin, quantities.Keys);

        // O nItem digitado é identidade da linha na nota do fornecedor: fica na linha de origem para as próximas
        // devoluções (D6). Única escrita na origem; gravada no mesmo SaveChanges do create.
        foreach (var (key, number) in typedNumbers)
            (await db.Context.PurchaseInvoicesItems.FirstAsync(i => i.Key == key)).NfeItemNumber = number;

        var today = TimeZoneInfo.ConvertTime(_now(), _storageZone).DateTime;
        var share = quantities.Values.Sum() / origin.Items.Sum(i => i.Quantity);

        var returnInvoice = new PurchaseInvoice
        {
            Key = Guid.NewGuid(),
            BranchCode = origin.BranchCode,
            CardCode = origin.CardCode,
            CardName = origin.CardName,
            IssuerType = DocumentIssuerType.Own,
            InvoiceType = PurchaseInvoiceType.Return,
            IssueDate = today,
            PostingDate = today,
            PurchaseInvoiceOriginKey = origin.Key,
            // Pesos na proporção do que volta: a unidade da linha pode não ser quilo (editáveis enquanto Pendente).
            NetWeight = decimal.Round(origin.NetWeight * share, 3, MidpointRounding.AwayFromZero),
            GrossWeight = decimal.Round(origin.GrossWeight * share, 3, MidpointRounding.AwayFromZero),
            TruckCode = origin.TruckCode,
            TruckingCompanyCode = origin.TruckingCompanyCode,
            TruckingCompanyName = origin.TruckingCompanyName,
            FreightTerms = origin.FreightTerms,
            // Volume como na devolução de venda: copiado da entrada e editável enquanto Pendente.
            VolumeQuantity = origin.VolumeQuantity,
            VolumeSpecies = origin.VolumeSpecies,
            VolumeBrand = origin.VolumeBrand,
            VolumeNumbering = origin.VolumeNumbering,
            Comments = $"Devolução da NF-e {NumberText(origin.TaxDocumentNumber)} série {origin.TaxDocumentSeries}. " +
                       $"Motivo: {request.Reason.Trim()}",
        };

        foreach (var bought in NfeItemNumbering.Ordered(origin.Items).Where(i => quantities.ContainsKey(i.Key!.Value)))
        {
            var quantity = quantities[bought.Key!.Value];

            var returnItem = new PurchaseInvoiceItem
            {
                Key = Guid.NewGuid(),
                ItemCode = bought.ItemCode,
                ItemName = bought.ItemName,
                UnitOfMeasureCode = bought.UnitOfMeasureCode,
                Quantity = quantity,
                UnitPrice = bought.UnitPrice,
                UsageCode = returnUsages[bought.Key!.Value],
                PurchaseInvoiceItemOriginKey = bought.Key,
            };

            // Frete, seguro, desconto e outras despesas na proporção do que volta (spec 2026-10-05 D4); editáveis
            // enquanto a devolução está Pendente.
            InvoiceLineChargeRules.ApplyProportional(returnItem, bought);
            returnInvoice.AddItem(returnItem);
        }

        // O create calcula e confere os tributos (spec §6.2) e grava num SaveChanges só.
        await createService.ExecuteAsync(returnInvoice, userName, nfeReturn: true);

        return returnInvoice;
    }

    private async Task<bool> ValidateOriginAsync(PurchaseInvoice origin)
    {
        if (!await gate.IsActiveAsync(origin.BranchCode))
            throw new DefaultException($"A filial {origin.BranchCode} não emite NF-e pelo Siagro.");

        if (origin.IssuerType == DocumentIssuerType.ThirdParty)
        {
            if (origin.InvoiceType != PurchaseInvoiceType.Normal || origin.InvoiceStatus != InvoiceStatus.Confirmed ||
                origin.TaxDocumentKind != TaxDocumentKind.Nfe || origin.ChaveNFe is not { Length: 44 } key || !key.All(char.IsAsciiDigit))
                throw new DefaultException(
                    "A devolução de entrada de terceiro parte de um documento Normal, confirmado e com a chave da NF-e do fornecedor (44 dígitos).");

            return true;
        }

        if (origin.IssuerType != DocumentIssuerType.Own || origin.InvoiceType != PurchaseInvoiceType.Normal ||
            origin.InvoiceStatus != InvoiceStatus.Confirmed || origin.NfeStatus != NfeStatus.Authorized ||
            origin.ChaveNFe is not { Length: 44 })
            throw new DefaultException(
                "O documento de entrada não tem NF-e própria autorizada pelo Siagro: " +
                "a devolução de compra parte de uma entrada própria autorizada.");

        return false;
    }

    private async Task<Dictionary<Guid, decimal>> ResolveQuantitiesAsync(
        PurchaseInvoice origin, IReadOnlyList<PurchaseInvoiceNfeReturnItem> items)
    {
        if (items.Any(i => i.Quantity < 0))
            throw new DefaultException("A quantidade a devolver não pode ser negativa.");

        var returned = await PurchaseInvoiceNfeReturnBalance.ReturnedByOriginItemAsync(db.Context, origin.Key, null);
        var result = new Dictionary<Guid, decimal>();

        foreach (var requested in items.Where(i => i.Quantity > 0))
        {
            var bought = origin.Items.FirstOrDefault(i => i.Key == requested.OriginItemKey)
                         ?? throw new DefaultException("Item informado não pertence ao documento de entrada.");
            var available = bought.Quantity - returned.GetValueOrDefault(requested.OriginItemKey);

            if (requested.Quantity > available)
                throw new DefaultException(
                    $"Item {bought.ItemCode}: a quantidade a devolver ({requested.Quantity.ToString("N3", PtBr)}) " +
                    $"passa do saldo devolvível ({available.ToString("N3", PtBr)}).");

            result[requested.OriginItemKey] = requested.Quantity;
        }

        return result.Count == 0
            ? throw new DefaultException("Informe a quantidade a devolver de ao menos um item.")
            : result;
    }

    private async Task<Dictionary<Guid, int>> ResolveReturnUsagesAsync(PurchaseInvoice origin, IEnumerable<Guid> originItemKeys)
    {
        var result = new Dictionary<Guid, int>();

        foreach (var key in originItemKeys)
        {
            var bought = origin.Items.First(i => i.Key == key);
            var usage = bought.UsageCode is { } code
                ? await db.Context.Usages.AsNoTracking().FirstOrDefaultAsync(u => u.Code == code)
                : null;

            if (usage?.ReturnUsageCode is not { } returnCode)
                throw new DefaultException(usage is null
                    ? $"O item {bought.ItemCode} da compra está sem natureza de operação."
                    : $"A natureza {usage.Code} {usage.Name} da compra não tem natureza de devolução cadastrada.");

            result[key] = returnCode;
        }

        return result;
    }

    /// <summary>Natureza padrão da filial (spec terceiro D3), a mesma para todas as linhas.</summary>
    private async Task<Dictionary<Guid, int>> ResolveThirdPartyUsageAsync(PurchaseInvoice origin, IEnumerable<Guid> originItemKeys)
    {
        var code = await db.Context.Branchs.AsNoTracking()
            .Where(b => b.Code == origin.BranchCode)
            .Select(b => b.ThirdPartyPurchaseReturnUsageCode)
            .FirstOrDefaultAsync();
        var usage = code is { } value
            ? await db.Context.Usages.AsNoTracking().FirstOrDefaultAsync(u => u.Code == value)
            : null;

        if (usage is null || usage.Inactive || usage.Direction != UsageDirection.Outgoing)
            throw new DefaultException($"Configure a natureza de devolução de compra de terceiro na filial {origin.BranchCode}.");

        return originItemKeys.ToDictionary(key => key, _ => usage.Code);
    }

    /// <summary>
    /// nItem de cada linha devolvida (spec terceiro §8.3): o da linha de origem, ou o digitado quando falta (1–990, único no
    /// documento); recusa item com IPI/ICMS-ST na nota do fornecedor e produto fora do cadastro. Devolve só os digitados.
    /// </summary>
    private async Task<Dictionary<Guid, int>> ResolveThirdPartyItemNumbersAsync(
        PurchaseInvoice origin, IReadOnlyList<PurchaseInvoiceNfeReturnItem> requested, IEnumerable<Guid> originItemKeys)
    {
        var supplierNfe = origin.XmlData is { Length: > 0 } ? SupplierNfeXmlReader.Read(origin.XmlData) : null;
        var used = origin.Items.Where(i => i.NfeItemNumber != null).Select(i => i.NfeItemNumber!.Value).ToHashSet();
        var typed = new Dictionary<Guid, int>();

        foreach (var key in originItemKeys)
        {
            var bought = origin.Items.First(i => i.Key == key);
            var number = bought.NfeItemNumber;

            if (number is null)
            {
                var informed = requested.First(i => i.OriginItemKey == key).ItemNumber;

                if (informed is not (>= 1 and <= 990))
                    throw new DefaultException($"Item {bought.ItemCode}: informe o número do item na NF-e do fornecedor.");

                if (!used.Add(informed.Value))
                    throw new DefaultException($"Item {bought.ItemCode}: o número {informed} já é de outro item desta NF-e do fornecedor.");

                typed[key] = informed.Value;
                number = informed;
            }

            var det = supplierNfe?.Items.FirstOrDefault(d => d.ItemNumber == number);
            if (supplierNfe is not null && det is null)
                throw new DefaultException($"Item {bought.ItemCode}: o item {number} não existe na NF-e do fornecedor.");

            if (det is not null && (det.IpiValue > 0m || det.IcmsStValue > 0m))
                throw new DefaultException($"Item {bought.ItemCode}: a nota do fornecedor tem IPI/ICMS-ST neste item, que o Siagro ainda não devolve.");

            if (!await db.Context.Items.AsNoTracking().AnyAsync(i => i.ItemCode == bought.ItemCode))
                throw new DefaultException($"Item {bought.ItemCode}: o produto não está cadastrado; ajuste o produto na entrada antes de devolver.");
        }

        return typed;
    }

    private static void EnsureEntryItemNumbers(PurchaseInvoice origin, IEnumerable<Guid> originItemKeys)
    {
        foreach (var key in originItemKeys)
        {
            if (NfeItemNumbering.OriginNumber(origin.Items.First(i => i.Key == key), origin.Items.Count) is null)
                throw new DefaultException(
                    "A NF-e de entrada foi emitida sem a numeração dos itens; a devolução com NF-e não está disponível para ela.");
        }
    }

    private static string? NumberText(string? number) =>
        long.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value.ToString(CultureInfo.InvariantCulture)
            : number;
}
