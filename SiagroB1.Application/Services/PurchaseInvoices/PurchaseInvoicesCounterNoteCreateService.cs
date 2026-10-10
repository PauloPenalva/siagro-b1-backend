using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Services.Taxes;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.PurchaseInvoices;

/// <summary>
/// "Gerar contra-nota" (spec 2026-10-10 §5): a partir da escrituração confirmada da NF-e do produtor, cria a entrada
/// PRÓPRIA Pendente com a mesma natureza e a mesma fotografia fiscal de cada linha — copiada, não recalculada (D5) — e
/// sem Funrural (D7). Quem emite é o "Emitir NF-e" da contra-nota; a escrituração não é alterada.
/// </summary>
public class PurchaseInvoicesCounterNoteCreateService(
    IUnitOfWork db,
    TaxCalculationGate gate,
    PurchaseInvoicesCreateService createService,
    Func<DateTimeOffset>? clock = null,
    TimeZoneInfo? storageZone = null)
{
    private readonly Func<DateTimeOffset> _now = clock ?? NfeIssueInputAssembler.BrasiliaNow;

    // Mesmo fuso em que o OData grava as datas (o do servidor): a emissão o converte de volta para Brasília.
    private readonly TimeZoneInfo _storageZone = storageZone ?? TimeZoneInfo.Local;

    public async Task<PurchaseInvoice> ExecuteAsync(Guid producerInvoiceKey, string userName)
    {
        var producer = await db.Context.PurchaseInvoices.AsNoTracking().Include(i => i.Items)
                           .FirstOrDefaultAsync(i => i.Key == producerInvoiceKey)
                       ?? throw new NotFoundException("Documento de entrada não encontrado.");

        // TODA a validação antes de qualquer escrita.
        if (!await gate.IsActiveAsync(producer.BranchCode))
            throw new DefaultException($"A filial {producer.BranchCode} não emite NF-e pelo Siagro.");

        if (producer.IssuerType != DocumentIssuerType.ThirdParty || producer.InvoiceType != PurchaseInvoiceType.Normal ||
            producer.InvoiceStatus != InvoiceStatus.Confirmed || producer.TaxDocumentKind != TaxDocumentKind.Nfe ||
            producer.ChaveNFe is not { Length: 44 } key || !key.All(char.IsAsciiDigit))
            throw new DefaultException(
                "A contra-nota parte da escrituração de uma NF-e de produtor: documento de terceiro, Normal, confirmado e com a chave de 44 dígitos.");

        await EnsureUsagesAllowAsync(producer);

        if (await PurchaseInvoiceCounterNote.FindValidAsync(db.Context, producer.Key) is { } existing)
            throw new DefaultException($"A escrituração já tem a contra-nota {existing.InvoiceNumber}.");

        var today = TimeZoneInfo.ConvertTime(_now(), _storageZone).DateTime;

        var note = new PurchaseInvoice
        {
            Key = Guid.NewGuid(),
            BranchCode = producer.BranchCode,
            CardCode = producer.CardCode,
            CardName = producer.CardName,
            IssuerType = DocumentIssuerType.Own,
            InvoiceType = PurchaseInvoiceType.Normal,
            TaxDocumentKind = TaxDocumentKind.Nfe,
            IssueDate = today,
            PostingDate = today,
            ProducerInvoiceKey = producer.Key,
            PaymentConditionCode = producer.PaymentConditionCode,
            // Transporte, pesos e volume copiados e editáveis enquanto Pendente (a importação do XML não lê os pesos).
            NetWeight = producer.NetWeight,
            GrossWeight = producer.GrossWeight,
            TruckCode = producer.TruckCode,
            TruckingCompanyCode = producer.TruckingCompanyCode,
            TruckingCompanyName = producer.TruckingCompanyName,
            FreightTerms = producer.FreightTerms,
            VolumeQuantity = producer.VolumeQuantity,
            VolumeSpecies = producer.VolumeSpecies,
            VolumeBrand = producer.VolumeBrand,
            VolumeNumbering = producer.VolumeNumbering,
            Comments = $"Contra-nota da NF-e {NumberText(producer.TaxDocumentNumber)} série {producer.TaxDocumentSeries} do produtor.",
        };

        foreach (var bought in NfeItemNumbering.Ordered(producer.Items))
        {
            var line = new PurchaseInvoiceItem
            {
                Key = Guid.NewGuid(),
                ItemCode = bought.ItemCode,
                ItemName = bought.ItemName,
                UnitOfMeasureCode = bought.UnitOfMeasureCode,
                Quantity = bought.Quantity,
                UnitPrice = bought.UnitPrice,
                PurchaseContractKey = bought.PurchaseContractKey,
                FreightValue = bought.FreightValue,
                InsuranceValue = bought.InsuranceValue,
                DiscountValue = bought.DiscountValue,
                OtherExpensesValue = bought.OtherExpensesValue,
                ProducerInvoiceItemKey = bought.Key,
            };

            TaxSnapshot.Copy(bought, line);
            note.AddItem(line);
        }

        // A contra-nota não tem Funrural (D7): o TaxSnapshot.Copy não copia os campos de Funrural e o motor de impostos
        // pula a contra-nota (IsActiveForAsync), então nada é recalculado. O create só dá o número interno e grava tudo
        // num SaveChanges só.
        await createService.ExecuteAsync(note, userName, counterNote: true);

        return note;
    }

    private async Task EnsureUsagesAllowAsync(PurchaseInvoice producer)
    {
        foreach (var line in NfeItemNumbering.Ordered(producer.Items))
        {
            if (line.UsageCode is not { } code)
                throw new DefaultException($"O item {line.ItemCode} da escrituração está sem natureza de operação.");

            var usage = await db.Context.Usages.AsNoTracking().FirstOrDefaultAsync(u => u.Code == code);

            if (usage is null || !usage.AllowsCounterNote)
                throw new DefaultException(
                    $"A natureza {usage?.Name ?? code.ToString(CultureInfo.InvariantCulture)} do item {line.ItemCode} não permite contra-nota.");

            if (string.IsNullOrWhiteSpace(line.Cfop))
                throw new DefaultException($"O item {line.ItemCode} da escrituração está sem os tributos calculados.");
        }
    }

    private static string? NumberText(string? number) =>
        long.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value.ToString(CultureInfo.InvariantCulture)
            : number;
}
