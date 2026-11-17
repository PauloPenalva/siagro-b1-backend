using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.SAP;
using SiagroB1.Application.Services.ShipmentBilling;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.Enums;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// "Vincular NF do SAP" (GAC-1226): cria os documentos de saída da carga a partir de uma ou mais A/R
/// Invoices do SAP (rodada 2: "peso de balança + complemento", um documento por NF, tudo ou nada). Tudo é relido e revalidado aqui — o que a tela mostrou pode ter mudado no SAP.
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
    IConfiguration? configuration = null,
    ILogger<ShipmentLoadsLinkSapInvoiceService>? logger = null)
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    private const string PersistenceFailure =
        "Não foi possível vincular as notas fiscais do SAP. Nenhuma alteração foi gravada. Tente novamente.";

    private const string ReleaseUnavailable = "A liberação de entrega informada não está disponível para esta carga.";

    /// <summary>Tolerância de um milésimo na comparação da soma das NFs com o saldo da carga.</summary>
    private const decimal QuantityTolerance = 0.001m;

    /// <summary>
    /// Regra da soma (GAC-1226 rodada 2, confirmada pelo dono do produto): a soma das NFs tem de ser IGUAL ao
    /// saldo da carga. Único ponto da regra — trocar por "não passar do saldo" é mudar esta linha.
    /// </summary>
    private static bool SumMatchesAvailable(decimal sum, decimal available) =>
        Math.Abs(sum - available) <= QuantityTolerance;

    /// <summary>
    /// Vincula uma ou mais NFs do SAP à carga: cada NF vira um documento de saída próprio, todos contra a
    /// mesma liberação, numa transação só (tudo ou nada). Devolve os documentos na ordem de
    /// <paramref name="sapDocEntries"/>.
    /// </summary>
    public async Task<IReadOnlyList<SalesInvoice>> ExecuteAsync(
        Guid loadKey, IReadOnlyList<int> sapDocEntries, Guid salesShipmentReleaseKey, string username)
    {
        if (!ErpMode.IsSapB1(configuration))
            throw new ApplicationException("Disponível somente com integração SAP Business One.");

        var load = await db.Context.ShipmentLoads.AsNoTracking().FirstOrDefaultAsync(x => x.Key == loadKey)
                   ?? throw new NotFoundException($"Shipment load not found key {loadKey}");

        if (sapDocEntries is null || sapDocEntries.Count == 0)
            throw new ApplicationException("Selecione ao menos uma nota fiscal do SAP.");

        if (sapDocEntries.Distinct().Count() != sapDocEntries.Count)
            throw new ApplicationException("A mesma nota fiscal do SAP foi informada mais de uma vez.");

        // Cada NF revalidada no servidor com as regras da listagem — o que a tela mostrou pode ter mudado.
        var sapInvoices = new List<SapLinkableInvoiceDto>(sapDocEntries.Count);
        foreach (var docEntry in sapDocEntries)
            sapInvoices.Add(await finder.FindAsync(loadKey, docEntry) ?? throw await ExplainRefusalAsync(docEntry));

        var cardCode = sapInvoices[0].CardCode?.Trim();
        if (sapInvoices.Any(i => !string.Equals(i.CardCode?.Trim(), cardCode, StringComparison.OrdinalIgnoreCase)))
            throw new ApplicationException("As notas fiscais selecionadas devem ser do mesmo cliente.");

        var release = await db.Context.SalesShipmentReleases.AsNoTracking()
            .Include(r => r.SalesContract)
            .FirstOrDefaultAsync(r => r.Key == salesShipmentReleaseKey);

        if (release is null || release.Status != ReleaseStatus.Actived
                            || release.SalesContract is null || release.SalesContract.ItemCode != load.ItemCode)
            throw new ApplicationException(ReleaseUnavailable);

        if (!string.Equals(release.SalesContract.CardCode?.Trim(), cardCode, StringComparison.OrdinalIgnoreCase))
            throw new ApplicationException(
                $"O cliente da NF do SAP ({sapInvoices[0].CardCode}) é diferente do cliente do contrato da liberação.");

        // O saldo é lido ANTES da transação, então esta comparação sozinha não protege de um faturamento
        // concorrente. Ela só é segura porque, dentro da transação, o guard de saldo da carga (chamado pelo
        // orquestrador para cada documento) relê a quantidade faturada do banco e recusa qualquer excesso.
        // Não remova essa chamada do guard por parecer redundante.
        var sum = sapInvoices.Sum(i => i.Quantity);
        var available = await new ShipmentLoadsBillingGuardService(db.Context).CalculateAvailableAsync(load.Key);
        if (!SumMatchesAvailable(sum, available))
            throw new ApplicationException(
                $"A soma das notas fiscais do SAP ({sum.ToString("N3", PtBr)}) é diferente do saldo da carga " +
                $"{load.Code} ({available.ToString("N3", PtBr)}). O vínculo exige que a soma seja igual ao saldo.");

        var created = new List<SalesInvoice>(sapInvoices.Count);

        // Tudo ou nada: uma transação para o lote. O orquestrador roda em CommitMode.Deferred (não abre nem
        // comita a dele) e grava cada documento dentro desta — então o guard de saldo da carga do 2º documento
        // já enxerga o 1º, e qualquer falha desfaz todos. O orquestrador não tem trabalho pós-commit: todos
        // os efeitos de cada documento ficam gravados dentro desta transação.
        await db.BeginTransactionAsync();
        try
        {
            foreach (var sapInvoice in sapInvoices)
            {
                // Dentro da transação: pega também duas NFs do lote com o mesmo número/série/chave.
                await duplicateGuard.EnsureUniqueAsync(
                    load.BranchCode ?? string.Empty, sapInvoice.TaxDocumentNumber, sapInvoice.TaxDocumentSeries,
                    sapInvoice.AccessKey, null);

                var salesInvoice = Build(load, release, sapInvoice);
                var link = new SapInvoiceLink(
                    sapInvoice.DocEntry, sapInvoice.DocNum, sapInvoice.TaxDocumentNumber,
                    sapInvoice.TaxDocumentSeries, sapInvoice.AccessKey, sapInvoice.Cfop,
                    sapInvoice.UsageCode, sapInvoice.UsageName);

                created.Add(salesInvoice);
                await billing.ExecuteAsync(salesInvoice, username, link, CommitMode.Deferred);
            }

            await db.CommitAsync();
        }
        catch (Exception e)
        {
            await db.RollbackAsync();

            if (e is ApplicationException or DbUpdateException)
            {
                // Dois vínculos concorrentes com NF em comum: o índice único filtrado em SapDocEntry derruba
                // o perdedor. O orquestrador relança só a mensagem, então a causa é confirmada relendo o
                // banco — nunca pela mensagem. Os documentos deste lote ficam de fora da busca.
                var mine = created.Select(c => c.Key).ToList();
                foreach (var sapInvoice in sapInvoices)
                {
                    var winner = await FindActiveLinkAsync(sapInvoice.DocEntry, mine);
                    if (winner is not null)
                        throw new ApplicationException(AlreadyLinkedMessage(winner, sapInvoice.DocNum), e);
                }
            }

            // Falha inesperada de gravação (inclusive embrulhada pelo orquestrador): o usuário não deve ver
            // a mensagem crua do EF. A original vai para o log; erros de negócio seguem com a própria mensagem.
            if (HasDbUpdateException(e))
            {
                logger?.LogError(e, "Falha ao gravar o vínculo de NFs do SAP da carga {LoadKey}", loadKey);
                throw new ApplicationException(PersistenceFailure, e);
            }

            throw;
        }

        return created;
    }

    /// <summary>
    /// Converte o parâmetro OData <c>SapDocEntries</c>: tem de ser uma coleção cujos elementos são todos inteiros
    /// maiores que zero (elemento nulo, zero, negativo ou de outro tipo invalida o conjunto).
    /// </summary>
    public static bool TryParseDocEntries(object? value, out List<int> entries)
    {
        entries = [];
        if (value is null or string || value is not System.Collections.IEnumerable items)
            return false;

        foreach (var item in items)
        {
            if (item is not (int or long or short or byte)) { entries = []; return false; }
            var number = Convert.ToInt64(item);
            if (number is <= 0 or > int.MaxValue) { entries = []; return false; }
            entries.Add((int)number);
        }

        return true;
    }

    private static bool HasDbUpdateException(Exception e)
    {
        for (var inner = e; inner != null; inner = inner.InnerException)
            if (inner is DbUpdateException)
                return true;
        return false;
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
    private async Task<Exception> ExplainRefusalAsync(int sapDocEntry)
    {
        var linked = await FindActiveLinkAsync(sapDocEntry, []);
        if (linked is not null)
            return new ApplicationException(AlreadyLinkedMessage(linked, linked.SapDocNum ?? sapDocEntry));

        var number = await finder.FindTaxDocumentNumberAsync(sapDocEntry) ?? sapDocEntry.ToString();
        return new ApplicationException($"A NF do SAP {number} não está mais disponível para vínculo.");
    }

    private Task<SalesInvoice?> FindActiveLinkAsync(int sapDocEntry, IReadOnlyCollection<Guid> exceptKeys) =>
        db.Context.SalesInvoices.AsNoTracking()
            .Where(s => s.SapDocEntry == sapDocEntry
                        && s.InvoiceStatus != InvoiceStatus.Cancelled
                        && s.InvoiceStatus != InvoiceStatus.Returned
                        && !exceptKeys.Contains(s.Key))
            .FirstOrDefaultAsync();

    private static string AlreadyLinkedMessage(SalesInvoice linked, int fallbackDocNum)
    {
        var number = string.IsNullOrWhiteSpace(linked.TaxDocumentNumber)
            ? fallbackDocNum.ToString()
            : linked.TaxDocumentNumber;

        return $"A NF {number} do SAP já está vinculada ao documento de saída {linked.InvoiceNumber}.";
    }
}
