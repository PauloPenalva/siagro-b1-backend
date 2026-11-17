using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.SAP;
using SiagroB1.Application.Services.ShipmentBilling;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// "Vincular NF do SAP" (GAC-1226): cria o documento de saída da carga a partir de uma A/R Invoice
/// do SAP. Tudo é relido e revalidado aqui — o que a tela mostrou pode ter mudado no SAP.
/// </summary>
/// <remarks>
/// Não duplica efeito nenhum: monta o <see cref="SalesInvoice"/> e entrega ao
/// <see cref="ShipmentBillingCreateSalesInvoiceService"/> com um <see cref="SapInvoiceLink"/>, então
/// alocação no contrato, saldo da liberação, títulos firmes, status da carga e log de movimento
/// saem exatamente como no faturamento manual.
/// <para>
/// <b>Escala do frete.</b> <c>ShipmentLoad.FreightPrice</c> é R$ por tonelada nesta base — confirmado nos
/// dados de produção (GAC-1226). Por isso é copiado direto para <c>FreightCostStandard</c> ("Frete p/Tonelada"
/// no faturamento manual), sem conversão; nulo vira 0.
/// </para>
/// </remarks>
public class ShipmentLoadsLinkSapInvoiceService(
    IUnitOfWork db,
    SapLinkableInvoicesService finder,
    SalesInvoiceTaxDocumentDuplicateGuard duplicateGuard,
    ShipmentBillingCreateSalesInvoiceService billing,
    IConfiguration? configuration = null)
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    private const string ReleaseUnavailable = "A liberação de entrega informada não está disponível para esta carga.";

    public async Task<SalesInvoice> ExecuteAsync(
        Guid loadKey, int sapDocEntry, Guid salesShipmentReleaseKey, string username)
    {
        if (!ErpMode.IsSapB1(configuration))
            throw new ApplicationException("Disponível somente com integração SAP Business One.");

        var load = await db.Context.ShipmentLoads.AsNoTracking().FirstOrDefaultAsync(x => x.Key == loadKey)
                   ?? throw new NotFoundException($"Shipment load not found key {loadKey}");

        var sapInvoice = await finder.FindAsync(loadKey, sapDocEntry);
        if (sapInvoice is null)
            throw await ExplainRefusalAsync(load, sapDocEntry);

        var release = await db.Context.SalesShipmentReleases.AsNoTracking()
            .Include(r => r.SalesContract)
            .FirstOrDefaultAsync(r => r.Key == salesShipmentReleaseKey);

        if (release is null || release.Status != ReleaseStatus.Actived
                            || release.SalesContract is null || release.SalesContract.ItemCode != load.ItemCode)
            throw new ApplicationException(ReleaseUnavailable);

        if (!string.Equals(release.SalesContract.CardCode?.Trim(), sapInvoice.CardCode?.Trim(),
                StringComparison.OrdinalIgnoreCase))
            throw new ApplicationException(
                $"O cliente da NF do SAP ({sapInvoice.CardCode}) é diferente do cliente do contrato da liberação.");

        await duplicateGuard.EnsureUniqueAsync(
            load.BranchCode ?? string.Empty, sapInvoice.TaxDocumentNumber, sapInvoice.TaxDocumentSeries,
            sapInvoice.AccessKey, null);

        var salesInvoice = Build(load, release, sapInvoice);
        var link = new SapInvoiceLink(
            sapInvoice.DocEntry, sapInvoice.DocNum, sapInvoice.TaxDocumentNumber, sapInvoice.TaxDocumentSeries,
            sapInvoice.AccessKey, sapInvoice.Cfop);

        try
        {
            await billing.ExecuteAsync(salesInvoice, username, link);
        }
        catch (Exception e) when (e is ApplicationException or DbUpdateException)
        {
            // Duas pessoas vinculando a mesma NF: o índice único filtrado em SapDocEntry derruba a
            // segunda. O orquestrador relança só a mensagem (a exceção do EF se perde), então a causa
            // é confirmada relendo o banco — nunca pela mensagem.
            var winner = await FindActiveLinkAsync(sapDocEntry, salesInvoice.Key);
            if (winner is not null)
                throw new ApplicationException(AlreadyLinkedMessage(winner, sapInvoice.DocNum), e);

            throw;
        }

        return salesInvoice;
    }

    private static SalesInvoice Build(ShipmentLoad load, SalesShipmentRelease release, SapLinkableInvoiceDto sap)
    {
        var invoice = new SalesInvoice
        {
            Key = Guid.NewGuid(),
            InvoiceDate = sap.DocDate,
            InvoiceStatus = InvoiceStatus.Pending,
            InvoiceType = SalesInvoiceType.Normal,
            CardCode = sap.CardCode!,
            BranchCode = load.BranchCode ?? string.Empty,
            ShipmentLoadKey = load.Key,
            // Transportadora da CARGA: a divergência com a da invoice é só aviso na lista, e o guard
            // do faturamento exige que o documento tenha a mesma da carga.
            TruckingCompanyCode = load.CarrierCardCode,
            TruckCode = load.TruckCode,
            GrossWeight = sap.Quantity,
            NetWeight = sap.Quantity,
            FreightTerms = sap.FreightTerms,
            FreightCostStandard = load.FreightPrice ?? 0m,
            DeliveryCardCode = release.DeliveryLocationCode,
        };

        invoice.AddItem(new SalesInvoiceItem
        {
            Key = Guid.NewGuid(),
            ItemCode = load.ItemCode,
            Quantity = sap.Quantity,
            UnitPrice = sap.UnitPrice,
            UnitOfMeasureCode = release.SalesContract!.UnitOfMeasureCode,
            SalesContractKey = release.SalesContractKey,
            SalesShipmentReleaseKey = release.Key,
        });

        return invoice;
    }

    /// <summary>Descobre por que <see cref="SapLinkableInvoicesService.FindAsync"/> não achou a invoice.</summary>
    private async Task<Exception> ExplainRefusalAsync(ShipmentLoad load, int sapDocEntry)
    {
        var linked = await FindActiveLinkAsync(sapDocEntry, null);
        if (linked is not null)
            return new ApplicationException(AlreadyLinkedMessage(linked, linked.SapDocNum ?? sapDocEntry));

        var onlyQuantityFails = await finder.FindIgnoringQuantityAsync(load.Key, sapDocEntry);
        if (onlyQuantityFails is not null)
        {
            var available = await new ShipmentLoadsBillingGuardService(db.Context).CalculateAvailableAsync(load.Key);
            return new ApplicationException(
                $"A quantidade da NF do SAP ({onlyQuantityFails.Quantity.ToString("N3", PtBr)}) é diferente do " +
                $"saldo da carga {load.Code} ({available.ToString("N3", PtBr)}). O vínculo exige quantidades iguais.");
        }

        return new ApplicationException("A NF do SAP não está mais disponível para vínculo.");
    }

    private Task<SalesInvoice?> FindActiveLinkAsync(int sapDocEntry, Guid? exceptKey) =>
        db.Context.SalesInvoices.AsNoTracking()
            .Where(s => s.SapDocEntry == sapDocEntry
                        && s.InvoiceStatus != InvoiceStatus.Cancelled
                        && s.InvoiceStatus != InvoiceStatus.Returned
                        && (exceptKey == null || s.Key != exceptKey))
            .FirstOrDefaultAsync();

    private static string AlreadyLinkedMessage(SalesInvoice linked, int fallbackDocNum)
    {
        var number = string.IsNullOrWhiteSpace(linked.TaxDocumentNumber)
            ? fallbackDocNum.ToString()
            : linked.TaxDocumentNumber;

        return $"A NF {number} do SAP já está vinculada ao documento de saída {linked.InvoiceNumber}.";
    }
}
