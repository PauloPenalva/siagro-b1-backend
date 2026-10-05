using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Services.Taxes;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Nfe;

public sealed record PurchaseInvoiceNfeReturnItem(Guid OriginItemKey, decimal Quantity);

public sealed record PurchaseInvoiceNfeReturnRequest(
    Guid PurchaseInvoiceKey, IReadOnlyList<PurchaseInvoiceNfeReturnItem> Items, string Reason);

/// <summary>
/// "Devolver" do Documento de Entrada (spec §9.2): a partir de uma entrada própria autorizada pelo Siagro, cria a
/// devolução de compra Pendente que sai com NF-e PRÓPRIA de saída (finalidade 4). Não confirma (quem confirma é a
/// emissão) e não mexe na entrada de origem: o saldo devolvível é calculado das devoluções (D12).
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
        await ValidateOriginAsync(origin);

        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new DefaultException("Informe o motivo da devolução.");

        var quantities = await ResolveQuantitiesAsync(origin, request.Items);
        var returnUsages = await ResolveReturnUsagesAsync(origin, quantities.Keys);
        EnsureEntryItemNumbers(origin, quantities.Keys);

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
            Comments = $"Devolução da NF-e {NumberText(origin.TaxDocumentNumber)} série {origin.TaxDocumentSeries}. " +
                       $"Motivo: {request.Reason.Trim()}",
        };

        foreach (var bought in NfeItemNumbering.Ordered(origin.Items).Where(i => quantities.ContainsKey(i.Key!.Value)))
        {
            returnInvoice.AddItem(new PurchaseInvoiceItem
            {
                Key = Guid.NewGuid(),
                ItemCode = bought.ItemCode,
                ItemName = bought.ItemName,
                UnitOfMeasureCode = bought.UnitOfMeasureCode,
                Quantity = quantities[bought.Key!.Value],
                UnitPrice = bought.UnitPrice,
                UsageCode = returnUsages[bought.Key!.Value],
                PurchaseInvoiceItemOriginKey = bought.Key,
            });
        }

        // O create calcula e confere os tributos (spec §6.2) e grava num SaveChanges só.
        await createService.ExecuteAsync(returnInvoice, userName, nfeReturn: true);

        return returnInvoice;
    }

    private async Task ValidateOriginAsync(PurchaseInvoice origin)
    {
        if (!await gate.IsActiveAsync(origin.BranchCode))
            throw new DefaultException($"A filial {origin.BranchCode} não emite NF-e pelo Siagro.");

        if (origin.IssuerType != DocumentIssuerType.Own || origin.InvoiceType != PurchaseInvoiceType.Normal ||
            origin.InvoiceStatus != InvoiceStatus.Confirmed || origin.NfeStatus != NfeStatus.Authorized ||
            origin.ChaveNFe is not { Length: 44 })
            throw new DefaultException(
                "O documento de entrada não tem NF-e própria autorizada pelo Siagro: " +
                "a devolução de compra parte de uma entrada própria autorizada.");
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
