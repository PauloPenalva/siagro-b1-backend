using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.SalesInvoices.Factories;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>
/// Monta a devolução com NF-e PRÓPRIA de entrada (finalidade 4) a partir de uma venda autorizada. Um lugar só
/// para o "Devolver" (<see cref="SalesInvoicesNfeReturnCreateService"/>) e a recusa de carga em dois tempos
/// (<c>ShipmentLoadsRefuseService</c>, spec 2026-10-09) gerarem a mesma nota. Não grava nada.
/// </summary>
/// <remarks>
/// A regra "documento com carga não" NÃO mora aqui: ela é do "Devolver", que não sabe destravar a carga.
/// </remarks>
public class SalesInvoiceNfeReturnBuilder(
    IUnitOfWork db,
    Func<DateTimeOffset>? clock = null,
    TimeZoneInfo? storageZone = null)
{
    private readonly Func<DateTimeOffset> _now = clock ?? NfeIssueInputAssembler.BrasiliaNow;

    // Mesmo fuso em que o OData grava o InvoiceDate (o do servidor): a emissão o converte de volta
    // para Brasília antes de exigir "hoje".
    private readonly TimeZoneInfo _storageZone = storageZone ?? TimeZoneInfo.Local;

    public static void EnsureOriginAuthorized(SalesInvoice origin)
    {
        if (origin.InvoiceStatus == InvoiceStatus.Returned)
            throw new DefaultException($"O documento {origin.InvoiceNumber} já foi devolvido por inteiro.");

        if (origin.InvoiceStatus != InvoiceStatus.Confirmed || origin.NfeStatus != NfeStatus.Authorized ||
            origin.ChaveNFe is not { Length: 44 })
            throw new DefaultException(
                $"O documento {origin.InvoiceNumber} não tem NF-e autorizada pelo Siagro: " +
                "a devolução com NF-e parte de uma venda autorizada.");
    }

    public async Task<SalesInvoice> BuildAsync(
        SalesInvoice origin,
        IReadOnlyDictionary<Guid, decimal> quantitiesByOriginItemKey,
        string comments,
        string userName)
    {
        var returnUsages = await ResolveReturnUsagesAsync(origin, quantitiesByOriginItemKey.Keys);
        EnsureSaleItemNumbers(origin, quantitiesByOriginItemKey.Keys);

        var returnInvoice = SalesInvoiceReturnFactory.CreateFrom(origin, userName, quantitiesByOriginItemKey);
        returnInvoice.InvoiceDate = TimeZoneInfo.ConvertTime(_now(), _storageZone).DateTime;
        returnInvoice.PaymentConditionCode = null;
        // A devolução volta ao remetente: não leva o local de entrega da venda (sem <entrega> no XML).
        returnInvoice.DeliveryCardCode = null;
        returnInvoice.DeliveryCardName = null;
        returnInvoice.TaxPayerComments = null;
        returnInvoice.TaxComments = null;
        returnInvoice.VolumeQuantity = origin.VolumeQuantity;
        returnInvoice.VolumeSpecies = origin.VolumeSpecies;
        returnInvoice.VolumeBrand = origin.VolumeBrand;
        returnInvoice.VolumeNumbering = origin.VolumeNumbering;
        returnInvoice.Comments = comments;

        foreach (var item in returnInvoice.Items)
            item.UsageCode = returnUsages[item.SalesInvoiceItemOriginKey!.Value];

        return returnInvoice;
    }

    public static string ReferenceText(SalesInvoice origin) =>
        $"Devolução da NF-e {NumberText(origin.TaxDocumentNumber)} série {origin.TaxDocumentSeries} " +
        $"(doc.saída {origin.InvoiceNumber}).";

    private async Task<Dictionary<Guid, int>> ResolveReturnUsagesAsync(SalesInvoice origin, IEnumerable<Guid> originItemKeys)
    {
        var result = new Dictionary<Guid, int>();

        foreach (var key in originItemKeys)
        {
            var sold = origin.Items.First(i => i.Key == key);
            var usage = sold.UsageCode is { } code
                ? await db.Context.Usages.AsNoTracking().FirstOrDefaultAsync(u => u.Code == code)
                : null;

            if (usage?.ReturnUsageCode is not { } returnCode)
                throw new DefaultException(usage is null
                    ? $"O item {sold.ItemCode} da venda está sem natureza de operação."
                    : $"A natureza {usage.Code} {usage.Name} da venda não tem natureza de devolução cadastrada.");

            result[key] = returnCode;
        }

        return result;
    }

    private static void EnsureSaleItemNumbers(SalesInvoice origin, IEnumerable<Guid> originItemKeys)
    {
        foreach (var key in originItemKeys)
        {
            if (NfeItemNumbering.OriginNumber(origin.Items.First(i => i.Key == key), origin.Items.Count) is null)
                throw new DefaultException(
                    "A NF-e de venda foi emitida antes da numeração dos itens; a devolução com NF-e não está disponível para ela.");
        }
    }

    private static string? NumberText(string? number) =>
        long.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value.ToString(CultureInfo.InvariantCulture)
            : number;
}
