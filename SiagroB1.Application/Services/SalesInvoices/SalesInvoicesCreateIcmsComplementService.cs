using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.DocNumbers;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Services.Taxes;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.SalesInvoices;

/// <summary>
/// "Complementar ICMS" (spec 2026-10-18 S2/S4/S10): a partir de uma venda Normal, Confirmada, com NF-e autorizada, cria o
/// complemento de ICMS Pendente — uma linha por item com base e ICMS informados, herdando da original produto, descrição,
/// unidade, natureza, CFOP e NCM, com quantidade e valores zerados. Não confirma: quem confirma é a emissão.
/// </summary>
/// <remarks>
/// Não passa pelo <see cref="SalesInvoicesCreateService"/> de propósito: ele aplica o complemento fiscal do CONTRATO,
/// a condição de pagamento padrão do cliente e a resolução ESTRITA do CFOP de saída — e a linha do complemento não tem
/// contrato, não tem pagamento e leva o CFOP da linha original (que o cadastro atual da natureza pode já não dar).
/// <para>
/// Ao contrário do "Devolver", a venda de carga, romaneio ou contrato pode ser complementada: o complemento não leva
/// nenhum desses vínculos, só <see cref="SalesInvoiceItem.ComplementedItemKey"/> — e não usa
/// <c>SalesInvoiceOriginKey</c>/<c>SalesInvoiceItemOriginKey</c>, que significam devolução (spec S3).
/// </para>
/// </remarks>
public class SalesInvoicesCreateIcmsComplementService(
    IUnitOfWork db,
    TaxCalculationGate gate,
    SalesInvoicesTaxApplyService taxApply,
    DocNumberSequenceService numberSequenceService,
    ILogger<SalesInvoicesCreateIcmsComplementService> logger,
    Func<DateTimeOffset>? clock = null,
    TimeZoneInfo? storageZone = null,
    Accounting.AccountingAssignmentDefaultsResolver? dimensionDefaults = null)
{
    public const string OriginNotComplementable =
        "Só uma venda normal, confirmada, com NF-e autorizada pode ser complementada.";

    public const string NoItemInformed = "Informe a base e o valor do ICMS de ao menos um item.";

    public const string BranchWithoutTaxRule = "Complemento de ICMS só existe na filial que emite NF-e pelo Siagro.";

    private readonly Func<DateTimeOffset> _now = clock ?? NfeIssueInputAssembler.BrasiliaNow;

    // Mesmo fuso em que o OData grava o InvoiceDate (o do servidor), como na devolução.
    private readonly TimeZoneInfo _storageZone = storageZone ?? TimeZoneInfo.Local;

    public async Task<SalesInvoice> ExecuteAsync(
        Guid originKey,
        IReadOnlyList<Guid> originItemKeys,
        IReadOnlyList<decimal> icmsBases,
        IReadOnlyList<decimal> icmsValues,
        string userName)
    {
        if (originItemKeys.Count != icmsBases.Count || originItemKeys.Count != icmsValues.Count)
            throw new DefaultException("A lista de itens e as de base e ICMS têm tamanhos diferentes.");

        var origin = await db.Context.SalesInvoices
                         .AsNoTracking()
                         .Include(i => i.Items)
                         .FirstOrDefaultAsync(i => i.Key == originKey)
                     ?? throw new NotFoundException("Documento de saída não encontrado.");

        // TODA a validação antes de qualquer escrita (e antes de consumir número).
        if (!await gate.IsActiveAsync(origin.BranchCode))
            throw new DefaultException(BranchWithoutTaxRule);

        EnsureOriginComplementable(origin);

        var typed = ResolveTyped(origin, originItemKeys, icmsBases, icmsValues);
        var complement = Build(origin, typed, userName);

        // Amarração contábil padrão (produto → parceiro) nas linhas geradas pelo sistema; sem o validador nem a trava de
        // obrigatoriedade: fluxo do sistema não pode ser bloqueado. No-op fora do STANDALONE.
        if (dimensionDefaults is not null)
        {
            foreach (var item in complement.Items)
                await dimensionDefaults.ApplyAsync(item, complement, complement.CardCode, complement.InvoiceDate, item.ItemCode);
        }

        // Tributos (Task 2 / S5): o motor roda com valor 0 e o aplicador repõe a base e o ICMS informados, com CST e CFOP
        // da linha original. Fora do try: a DefaultException tem de chegar à tela como 400.
        await taxApply.ApplyAsync(complement, complement.Items);

        try
        {
            await db.BeginTransactionAsync();

            // Herda a série da nota original; série manual/inativa/inexistente cai na automática padrão.
            // Nunca leva código digitado: o complemento sempre recebe número do contador.
            var assignment = await numberSequenceService.AssignAsync(new DocNumberRequest(
                TransactionCode.SalesInvoice, await numberSequenceService.ResolveInheritedKeyAsync(origin.DocNumberKey),
                null, 9, c => db.Context.SalesInvoices.AnyAsync(x => x.InvoiceNumber == c)));
            complement.DocNumberKey = assignment.DocNumberKey;
            complement.InvoiceNumber = assignment.Code;

            await db.Context.SalesInvoices.AddAsync(complement);
            await db.SaveChangesAsync();
            await db.CommitAsync();

            return complement;
        }
        catch (Exception e)
        {
            await db.RollbackAsync();
            logger.LogError(e, "Erro ao criar o complemento de ICMS do documento de saída {Number}", origin.InvoiceNumber);
            throw;
        }
    }

    /// <summary>V1: venda Normal, Confirmada, NF-e (não "outro documento") autorizada pelo Siagro, com chave.</summary>
    public static void EnsureOriginComplementable(SalesInvoice origin)
    {
        if (origin.InvoiceType != SalesInvoiceType.Normal
            || origin.TaxDocumentKind != TaxDocumentKind.Nfe
            || origin.InvoiceStatus != InvoiceStatus.Confirmed
            || origin.NfeStatus != NfeStatus.Authorized
            || origin.ChaveNFe is not { Length: 44 })
            throw new DefaultException(OriginNotComplementable);
    }

    private static List<(SalesInvoiceItem Original, decimal IcmsBase, decimal IcmsValue)> ResolveTyped(
        SalesInvoice origin,
        IReadOnlyList<Guid> originItemKeys,
        IReadOnlyList<decimal> icmsBases,
        IReadOnlyList<decimal> icmsValues)
    {
        var byKey = new Dictionary<Guid, (decimal Base, decimal Value)>();

        for (var i = 0; i < originItemKeys.Count; i++)
        {
            var key = originItemKeys[i];

            if (origin.Items.All(item => item.Key != key))
                throw new DefaultException("Item informado não pertence ao documento de saída.");

            if (!byKey.TryAdd(key, (Math.Round(icmsBases[i], 2), Math.Round(icmsValues[i], 2))))
                throw new DefaultException("Item informado em duplicidade.");
        }

        // Na ordem da NF-e original: é a ordem em que o operador conferiu os itens.
        var result = new List<(SalesInvoiceItem, decimal, decimal)>();

        foreach (var original in origin.Items.OrderBy(i => i.NfeItemNumber ?? int.MaxValue))
        {
            if (!byKey.TryGetValue(original.Key!.Value, out var typed))
                continue;

            // Item com os dois zerados fica fora (S2).
            if (typed is { Base: 0m, Value: 0m })
                continue;

            if (typed.Base <= 0m || typed.Value <= 0m)
                throw new DefaultException($"Item {original.ItemCode}: informe a base e o valor do ICMS do complemento.");

            if (typed.Value > typed.Base)
                throw new DefaultException($"Item {original.ItemCode}: o ICMS do complemento não pode ser maior que a base.");

            result.Add((original, typed.Base, typed.Value));
        }

        return result.Count == 0 ? throw new DefaultException(NoItemInformed) : result;
    }

    private SalesInvoice Build(
        SalesInvoice origin, IEnumerable<(SalesInvoiceItem Original, decimal IcmsBase, decimal IcmsValue)> lines, string userName)
    {
        // S4: cliente, filial e local de entrega da original; sem pagamento, transporte, pesos, volumes, carga,
        // liberação nem vínculo de devolução.
        var complement = new SalesInvoice
        {
            BranchCode = origin.BranchCode,
            CardCode = origin.CardCode,
            CardName = origin.CardName,
            DeliveryCardCode = origin.DeliveryCardCode,
            DeliveryCardName = origin.DeliveryCardName,
            InvoiceType = SalesInvoiceType.IcmsComplement,
            InvoiceStatus = InvoiceStatus.Pending,
            InvoiceDate = TimeZoneInfo.ConvertTime(_now(), _storageZone).DateTime,
            TaxDocumentKind = TaxDocumentKind.Nfe,
            PaymentConditionCode = null,
            TruckingCompanyCode = null,
            TruckingCompanyName = null,
            TruckCode = null,
            FreightTerms = FreightTerms.None,
            GrossWeight = 0m,
            NetWeight = 0m,
            TaxPayerComments = null,
            TaxComments = null,
            Comments = ReferenceText(origin),
            DeliveryStatus = SalesInvoiceDeliveryStatus.Open,
            CreatedBy = userName,
            CreatedAt = DateTime.Now,
        };

        foreach (var (original, icmsBase, icmsValue) in lines)
        {
            complement.AddItem(new SalesInvoiceItem
            {
                ItemCode = original.ItemCode,
                ItemName = original.ItemName,
                UnitOfMeasureCode = original.UnitOfMeasureCode,
                UsageCode = original.UsageCode,
                UsageName = original.UsageName,
                Cfop = original.Cfop,
                Ncm = original.Ncm,
                GoodsOrigin = original.GoodsOrigin,
                Quantity = 0m,
                UnitPrice = 0m,
                ComplementedItemKey = original.Key,
                // Lidos pelo aplicador do Task 2 antes do motor, que os zera.
                IcmsBase = icmsBase,
                IcmsValue = icmsValue,
            });
        }

        return complement;
    }

    private static string ReferenceText(SalesInvoice origin) =>
        $"Complemento de ICMS da NF-e {NumberText(origin.TaxDocumentNumber)} série {origin.TaxDocumentSeries} " +
        $"(doc.saída {origin.InvoiceNumber}).";

    private static string? NumberText(string? number) =>
        long.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value.ToString(CultureInfo.InvariantCulture)
            : number;
}
