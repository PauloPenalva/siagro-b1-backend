using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.SalesContracts;
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
/// Invoices do SAP (um documento por NF, tudo ou nada). Tudo é relido e revalidado aqui — o que a tela mostrou
/// pode ter mudado no SAP.
/// </summary>
/// <remarks>
/// Não duplica efeito nenhum: monta o <see cref="SalesInvoice"/> e entrega ao
/// <see cref="ShipmentBillingCreateSalesInvoiceService"/> com um <see cref="SapInvoiceLink"/>, então
/// alocação no contrato, saldo da liberação, títulos firmes, status da carga e log de movimento
/// saem exatamente como no faturamento manual.
/// <para>
/// <b>Divergência.</b> A soma das NFs não precisa bater com o saldo da carga: o vínculo é classificado por
/// <see cref="SapLinkClassifier"/> contra a quantidade efetiva (QE = saldo da carga no momento do vínculo) e o
/// preço de referência do contrato. Com divergência, o usuário confirma o alerta; o documento continua
/// espelhando a NF (o contrato é baixado pela quantidade da NF) e a diferença QE − QNF fica num
/// <see cref="SalesInvoiceDivergence"/> por documento — o sexto termo do saldo, que fecha a carga.
/// </para>
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
    SalesContractsFixedVolumeService fixedVolume,
    IConfiguration? configuration = null,
    ILogger<ShipmentLoadsLinkSapInvoiceService>? logger = null)
{
    private const string PersistenceFailure =
        "Não foi possível vincular as notas fiscais do SAP. Nenhuma alteração foi gravada. Tente novamente.";

    private const string ReleaseUnavailable = "A liberação de entrega informada não está disponível para esta carga.";

    /// <summary>Tolerância de um milésimo no saldo da carga.</summary>
    private const decimal QuantityTolerance = 0.001m;

    /// <summary>Carga, liberação e NFs já revalidadas no servidor.</summary>
    private sealed record LinkRequest(
        ShipmentLoad Load, SalesShipmentRelease Release, IReadOnlyList<SapLinkableInvoiceDto> Invoices);

    /// <summary>
    /// Vincula uma ou mais NFs do SAP à carga: cada NF vira um documento de saída próprio, todos contra a
    /// mesma liberação, numa transação só (tudo ou nada). Devolve os documentos na ordem de
    /// <paramref name="sapDocEntries"/>.
    /// </summary>
    /// <param name="confirmDivergence">
    /// O usuário já viu e aceitou o alerta de divergência. Sem ele, um vínculo divergente (tipo 1 ou 2) é
    /// recusado com <see cref="SapLinkDivergenceException"/> e nada é gravado.
    /// </param>
    public async Task<IReadOnlyList<SalesInvoice>> ExecuteAsync(
        Guid loadKey, IReadOnlyList<int> sapDocEntries, Guid salesShipmentReleaseKey, string username,
        bool confirmDivergence = false)
    {
        var request = await ValidateAsync(loadKey, sapDocEntries, salesShipmentReleaseKey);
        var load = request.Load;
        var release = request.Release;
        var sapInvoices = request.Invoices;

        var created = new List<SalesInvoice>(sapInvoices.Count);

        // Tudo ou nada: uma transação para o lote. O orquestrador roda em CommitMode.Deferred (não abre nem
        // comita a dele) e grava cada documento dentro desta — qualquer falha desfaz todos. O orquestrador não
        // tem trabalho pós-commit: todos os efeitos de cada documento ficam gravados dentro desta transação.
        await db.BeginTransactionAsync();
        try
        {
            // A QE é relida DENTRO da transação: é ela que vale para a classificação e para os registros.
            var assessment = await AssessAsync(request);

            if (assessment.Type != SapLinkType.Normal && !confirmDivergence)
                throw new SapLinkDivergenceException(assessment);

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
                    sapInvoice.UsageCode, sapInvoice.UsageName, assessment.Type);

                created.Add(salesInvoice);

                // allowLoadExcess: a NF pode passar do saldo — a diferença vira o termo de divergência abaixo.
                await billing.ExecuteAsync(salesInvoice, username, link, CommitMode.Deferred, allowLoadExcess: true);
            }

            if (assessment.Type != SapLinkType.Normal)
            {
                // Um registro por documento, com os números da parte (rateio da QE) daquela NF.
                for (var i = 0; i < created.Count; i++)
                {
                    var share = assessment.Shares[i];
                    db.Context.SalesInvoiceDivergences.Add(new SalesInvoiceDivergence
                    {
                        SalesInvoiceKey = created[i].Key,
                        ShipmentLoadKey = load.Key,
                        LinkType = assessment.Type,
                        EffectiveQuantity = share.EffectiveQuantity,
                        InvoiceQuantity = share.InvoiceQuantity,
                        QuantityDifference = share.QuantityDifference,
                        EffectiveValue = share.EffectiveValue,
                        InvoiceValue = share.InvoiceValue,
                        ValueDifference = share.ValueDifference,
                        Status = SalesInvoiceDivergenceStatus.Open,
                        CreatedAt = DateTime.Now,
                        CreatedBy = username,
                    });
                }

                // Os registros nascem DEPOIS do recálculo que o faturamento já fez, e o termo de divergência é
                // somado no servidor: grava primeiro, recalcula a carga e grava de novo.
                await db.SaveChangesAsync();
                await ShipmentLoadsRecalculateInvoicedService.RecalculateAsync(db.Context, load.Key, null);
                await db.SaveChangesAsync();
            }

            await db.CommitAsync();
        }
        catch (SapLinkDivergenceException)
        {
            // Nada foi gravado: só desfaz a transação e devolve o alerta como veio.
            await db.RollbackAsync();
            throw;
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
    /// Prévia do vínculo: as mesmas validações e a mesma classificação do <see cref="ExecuteAsync"/>, sem
    /// transação e sem gravar nada. É o que a tela usa para decidir se mostra o alerta de divergência.
    /// </summary>
    public async Task<SapLinkAssessment> PreviewAsync(
        Guid loadKey, IReadOnlyList<int> sapDocEntries, Guid salesShipmentReleaseKey)
    {
        var request = await ValidateAsync(loadKey, sapDocEntries, salesShipmentReleaseKey);
        return await AssessAsync(request);
    }

    /// <summary>Validações comuns ao vínculo e à prévia; nada é gravado.</summary>
    private async Task<LinkRequest> ValidateAsync(
        Guid loadKey, IReadOnlyList<int> sapDocEntries, Guid salesShipmentReleaseKey)
    {
        if (!ErpMode.IsSapB1(configuration))
            throw new ApplicationException("Disponível somente com integração SAP Business One.");

        var load = await db.Context.ShipmentLoads.AsNoTracking().FirstOrDefaultAsync(x => x.Key == loadKey)
                   ?? throw new NotFoundException($"Shipment load not found key {loadKey}");

        if (sapDocEntries is null || sapDocEntries.Count == 0)
            throw new ApplicationException("Selecione ao menos uma nota fiscal do SAP.");

        if (sapDocEntries.Distinct().Count() != sapDocEntries.Count)
            throw new ApplicationException("A mesma nota fiscal do SAP foi informada mais de uma vez.");

        // ANTES da busca das NFs: carga sem saldo não lista NF nenhuma, e a recusa sairia como
        // "não está mais disponível", mandando o usuário procurar o problema na NF.
        await EnsureHasBalanceAsync(load);

        // Cada NF revalidada no servidor com as regras da listagem (cliente, transportadora e placa da carga)
        // — o que a tela mostrou pode ter mudado.
        var sapInvoices = new List<SapLinkableInvoiceDto>(sapDocEntries.Count);
        foreach (var docEntry in sapDocEntries)
            sapInvoices.Add(await finder.FindAsync(loadKey, docEntry) ?? throw await ExplainRefusalAsync(docEntry));

        var release = await db.Context.SalesShipmentReleases.AsNoTracking()
            .Include(r => r.SalesContract)
            .FirstOrDefaultAsync(r => r.Key == salesShipmentReleaseKey);

        if (release is null || release.Status != ReleaseStatus.Actived
                            || release.SalesContract is null || release.SalesContract.ItemCode != load.ItemCode)
            throw new ApplicationException(ReleaseUnavailable);

        // O cliente da NF NÃO precisa ser o do contrato da liberação: por instrução da trading o contrato é feito
        // num cliente e o faturamento sai em outro. O documento de saída obedece a NF (Build usa sap.CardCode);
        // o saldo é baixado no contrato da liberação escolhida.

        return new LinkRequest(load, release, sapInvoices);
    }

    /// <summary>
    /// Relê a QE (saldo da carga) e classifica o vínculo contra o preço de referência do contrato da liberação.
    /// </summary>
    private async Task<SapLinkAssessment> AssessAsync(LinkRequest request)
    {
        var effectiveQuantity = await EnsureHasBalanceAsync(request.Load);
        var referencePrice = await SapLinkReferencePrice.ResolveAsync(fixedVolume, request.Release.SalesContract!);

        return SapLinkClassifier.Assess(
            effectiveQuantity,
            referencePrice,
            request.Invoices.Select(i => new SapLinkInvoice(i.DocEntry, i.Quantity, i.LineTotal)).ToList());
    }

    private async Task<decimal> EnsureHasBalanceAsync(ShipmentLoad load)
    {
        var available = await new ShipmentLoadsBillingGuardService(db.Context).CalculateAvailableAsync(load.Key);
        if (available <= QuantityTolerance)
            throw new ApplicationException($"A carga {load.Code} não tem saldo a faturar.");

        return available;
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
            // Transportadora da CARGA: a lista já só traz invoices da mesma transportadora, e o guard
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
