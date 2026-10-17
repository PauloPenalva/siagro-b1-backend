using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Support;

/// <summary>Cenário do complemento de valor próprio (spec 2026-10-14) sobre o da entrada própria (PurchaseNfeTestSeed).</summary>
public sealed record ValueComplementScenario(
    PurchaseNfeScenario Purchase, PurchaseContract Contract, int PurchaseUsage, int PaymentConditionCode,
    Guid OriginalKey, Guid OriginalItemKey)
{
    public UnitOfWork Db => Purchase.Db;

    /// <summary>
    /// Complemento próprio Pendente, ainda não gravado: cada linha só com o item original e o valor — produto, unidade,
    /// natureza e contrato o servidor herda. Data = a do relógio dos testes de emissão (02/10/2026).
    /// </summary>
    public PurchaseInvoice Complement(params (Guid? OriginalItemKey, decimal Amount)[] lines)
    {
        var invoice = new PurchaseInvoice
        {
            Key = Guid.NewGuid(), BranchCode = "01", CardCode = PurchaseNfeTestSeed.Supplier, CardName = "PRODUTOR RURAL TESTE",
            IssuerType = DocumentIssuerType.Own, InvoiceType = PurchaseInvoiceType.ValueComplement,
            IssueDate = new DateTime(2026, 10, 2), PostingDate = new DateTime(2026, 10, 2), FreightTerms = FreightTerms.None,
            PaymentConditionCode = PaymentConditionCode,
        };

        foreach (var (originalItemKey, amount) in lines)
            invoice.AddItem(new PurchaseInvoiceItem
            {
                Key = ValueComplementNfeSeed.NextLineKey(), ComplementedItemKey = originalItemKey, ComplementAmount = amount,
            });

        return invoice;
    }
}

/// <summary>
/// Produtor F-SP com a natureza "COMPRA DE MERCADORIA" gerando financeiro, contrato aprovado de TRIGO e a entrada própria
/// DE000005 (série 9 nº 5) AUTORIZADA — a nota original que o complemento referencia.
/// </summary>
public static class ValueComplementNfeSeed
{
    public const string OriginalNumber = "DE000005";
    public const string SecondNumber = "DE000007";

    private static long _lineSequence;

    /// <summary>Chave de 44 dígitos da NF-e de entrada série 9 nº <paramref name="number"/> da filial semeada.</summary>
    public static string AccessKey(int number) => $"3526101234567800019555009{number:000000000}1481516234";

    /// <summary>Chaves crescentes: a ordem do nItem das linhas novas é a da Key (NfeItemNumbering), e o teste precisa prevê-la.</summary>
    public static Guid NextLineKey() =>
        Guid.Parse($"00000000-0000-0000-0000-{Interlocked.Increment(ref _lineSequence):x12}");

    public static async Task<ValueComplementScenario> SeedAsync()
    {
        var purchase = await PurchaseNfeTestSeed.SeedAsync();
        var context = purchase.Db.Context;

        // STANDALONE: a linha do complemento exige natureza com "Gera financeiro" (spec 2026-10-13 D10).
        var usage = await context.Usages.SingleAsync(u => u.Name == "COMPRA DE MERCADORIA");
        usage.CreatesFinancialDocument = true;
        // Compra de grão: a natureza exige o contrato (spec 2026-10-17 D3 — no complemento, quem manda no contrato é ela).
        context.UsageEffects.Add(new UsageEffect { UsageCode = usage.Code, RequiresContract = true });

        var contract = await AddContractAsync(purchase.Db, "PC-TRIGO");

        var condition = (await context.PaymentConditions.FirstAsync()).Code;
        var (originalKey, itemKeys) = await AddOriginalAsync(purchase.Db, contract, usage.Code, OriginalNumber, 5);

        return new ValueComplementScenario(purchase, contract, usage.Code, condition, originalKey, itemKeys[0]);
    }

    /// <summary>Contrato de compra de TRIGO (aprovado por padrão) — o do operador quando a original não tem (D5).</summary>
    public static async Task<PurchaseContract> AddContractAsync(
        UnitOfWork db, string code, string cardCode = PurchaseNfeTestSeed.Supplier, ContractStatus status = ContractStatus.Approved)
    {
        var contract = new PurchaseContract
        {
            Key = Guid.NewGuid(), Code = code, CardCode = cardCode, ItemCode = "TRIGO", UnitOfMeasureCode = "KG",
            HarvestSeasonCode = "25/26", Status = status, TotalVolume = 1_000_000m, FreightTerms = FreightTerms.Cif,
            DeliveryLocationCode = "01",
        };
        db.Context.PurchaseContracts.Add(contract);
        await db.SaveChangesAsync();
        return contract;
    }

    /// <summary>Entrada própria Normal, confirmada e AUTORIZADA, com <paramref name="items"/> linhas de TRIGO no contrato.</summary>
    public static async Task<(Guid InvoiceKey, IReadOnlyList<Guid> ItemKeys)> AddOriginalAsync(
        UnitOfWork db, PurchaseContract contract, int usageCode, string invoiceNumber, int nfeNumber, int items = 1)
    {
        var invoice = new PurchaseInvoice
        {
            Key = Guid.NewGuid(), InvoiceNumber = invoiceNumber, BranchCode = "01", CardCode = PurchaseNfeTestSeed.Supplier,
            CardName = "PRODUTOR RURAL TESTE", IssuerType = DocumentIssuerType.Own, InvoiceType = PurchaseInvoiceType.Normal,
            InvoiceStatus = InvoiceStatus.Confirmed, NfeStatus = NfeStatus.Authorized, NfeEnvironment = NfeEnvironment.Homologation,
            ChaveNFe = AccessKey(nfeNumber), NfeProtocol = "135260000000005", TaxDocumentSeries = "9",
            TaxDocumentNumber = nfeNumber.ToString("000000000"), IssueDate = new DateTime(2026, 10, 1),
            PostingDate = new DateTime(2026, 10, 1), GrossWeight = 1000m * items, NetWeight = 1000m * items,
            FreightTerms = FreightTerms.None,
        };

        for (var number = 1; number <= items; number++)
            invoice.AddItem(new PurchaseInvoiceItem
            {
                Key = Guid.NewGuid(), ItemCode = "TRIGO", ItemName = "TRIGO EM GRAOS", UnitOfMeasureCode = "KG",
                Quantity = 1000m, UnitPrice = 1.5m, UsageCode = usageCode, UsageName = "COMPRA DE MERCADORIA",
                PurchaseContractKey = contract.Key, NfeItemNumber = number,
            });

        db.Context.PurchaseInvoices.Add(invoice);
        await db.SaveChangesAsync();

        return (invoice.Key, invoice.Items.OrderBy(i => i.NfeItemNumber).Select(i => i.Key!.Value).ToList());
    }

    /// <summary>Altera um documento no contexto do cenário — é ele que os serviços leem.</summary>
    public static async Task ChangeAsync(UnitOfWork db, Guid invoiceKey, Action<PurchaseInvoice> change)
    {
        var invoice = await db.Context.PurchaseInvoices.Include(i => i.Items).SingleAsync(i => i.Key == invoiceKey);
        change(invoice);
        await db.SaveChangesAsync();
    }

    /// <summary>Inclusão com a regra ativa (STANDALONE) e o cadastro do F-SP; a numeração interna começa em ST-0001.</summary>
    public static PurchaseInvoicesCreateService Create(UnitOfWork db) =>
        new(db, PurchaseNfeTestSeed.Partners(), new FakeItemService(),
            TaxTestServices.PurchaseApply(db, PurchaseNfeTestSeed.Partners()), new FakeDocNumberSequenceService());
}
