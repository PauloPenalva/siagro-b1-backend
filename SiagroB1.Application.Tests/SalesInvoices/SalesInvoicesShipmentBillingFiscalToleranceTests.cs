using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.SalesInvoices;

/// <summary>
/// A cadeia fiscal (natureza de operação + CFOP) é BEST-EFFORT no documento nascido de
/// romaneio e ESTRITA no documento avulso.
///
/// O caminho de romaneio não consulta os efeitos da natureza — quem debita o contrato ali é
/// <c>SalesContractsAllocationCreateService</c>, dirigido pela origem do documento. Faturar
/// sem natureza deixa apenas metadado fiscal em branco, e por isso pode ser tolerado. No
/// avulso é o contrário: o efeito no contrato vem da natureza, e faltar natureza ou CFOP é
/// erro de negócio que precisa chegar à tela.
///
/// O que motivou a tolerância: em modo SAPB1 as naturezas vêm do OUSG e o efeito mora em
/// USAGE_EFFECTS, tabela do Siagro que nasce vazia — nenhuma natureza padrão existe, e o
/// faturamento de romaneio ficava travado numa base recém-implantada.
/// </summary>
public class SalesInvoicesShipmentBillingFiscalToleranceTests
{
    private const string BranchCode = "01";
    private const string CardCode = "C0001";

    private static UsageService Usages(UnitOfWork db) =>
        new(db, NullLogger<UsageService>.Instance);

    private static SalesInvoicesCreateService Create(
        UnitOfWork db, IBusinessPartnerService partners)
    {
        var usages = Usages(db);

        return new SalesInvoicesCreateService(
            db,
            partners,
            new FakeItemService(new Dictionary<string, string> { ["SOJA"] = "SOJA EM GRAOS" }),
            new FakeDocNumberSequenceService(),
            new SalesInvoicesUsageGuardService(usages),
            new SalesInvoicesCfopResolveService(db, usages, partners),
            NullLogger<SalesInvoicesCreateService>.Instance);
    }

    private static FakeBusinessPartnerService Partners(string? state = "RS") =>
        new(
            names: new Dictionary<string, string> { [CardCode] = "CLIENTE TESTE" },
            states: state is null
                ? new Dictionary<string, string>()
                : new Dictionary<string, string> { [CardCode] = state });

    private static async Task SeedBranchAsync(UnitOfWork db, string? stateCode)
    {
        db.Context.Branchs.Add(new Branch
        {
            Code = BranchCode,
            BranchName = "MATRIZ",
            StateCode = stateCode,
        });

        await db.SaveChangesAsync();
    }

    /// <summary>Natureza padrão completa — o que a semente cria em STANDALONE.</summary>
    private static async Task<int> SeedDefaultUsageAsync(UnitOfWork db)
    {
        var created = await Usages(db).CreateAsync(new UsageModel
        {
            Name = "Venda de grãos",
            CfopOutgoingInState = "5102",
            CfopOutgoingOutState = "6102",
            ContractBalanceEffect = ContractBalanceEffect.Consume,
            RequiresContract = true,
            RequiresQuantity = true,
            IsDefault = true,
        });

        return created.Code;
    }

    private static SalesInvoice Invoice(int? usageCode = null, Guid? contractKey = null) => new()
    {
        Key = Guid.NewGuid(),
        BranchCode = BranchCode,
        CardCode = CardCode,
        GrossWeight = 1000m,
        NetWeight = 990m,
        Items =
        [
            new SalesInvoiceItem
            {
                Key = Guid.NewGuid(),
                ItemCode = "SOJA",
                UnitOfMeasureCode = "KG",
                Quantity = 990m,
                UnitPrice = 2.5m,
                SalesContractKey = contractKey ?? Guid.NewGuid(),
                UsageCode = usageCode,
            }
        ],
    };

    /// <summary>
    /// Marca o documento como vindo de romaneio. O romaneio precisa existir no banco: o
    /// serviço o relê para gravar o vínculo.
    /// </summary>
    private static async Task<StorageTransaction> AttachTransactionAsync(
        UnitOfWork db, SalesInvoice invoice)
    {
        var transaction = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = "ROM-0001",
            CardCode = CardCode,
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            WarehouseCode = "01",
            GrossWeight = 1000m,
            NetWeight = 990m,
        };

        db.Context.StorageTransactions.Add(transaction);
        await db.SaveChangesAsync();

        invoice.SalesTransactions.Add(transaction);

        return transaction;
    }

    /// <summary>
    /// Base SAPB1 recém-implantada: USAGE_EFFECTS vazia, portanto nenhuma natureza padrão.
    /// O faturamento tem que nascer assim mesmo, sem natureza e sem CFOP.
    /// </summary>
    [Fact]
    public async Task Shipment_billing_without_any_usage_creates_the_invoice()
    {
        var db = TestDb.CreateUnitOfWork();
        await SeedBranchAsync(db, "RS");

        var invoice = Invoice();
        await AttachTransactionAsync(db, invoice);

        await Create(db, Partners()).ExecuteAsync(invoice, "tester");

        var item = invoice.Items.Single();

        Assert.Null(item.UsageCode);
        Assert.Null(item.Cfop);
        Assert.NotNull(invoice.InvoiceNumber);
    }

    /// <summary>
    /// O vínculo do romaneio é o efeito que importa: é dele que o consumo do contrato deriva.
    /// Sem natureza ele tem que sair igual.
    /// </summary>
    [Fact]
    public async Task Shipment_billing_without_any_usage_still_links_the_transaction()
    {
        var db = TestDb.CreateUnitOfWork();
        await SeedBranchAsync(db, "RS");

        var invoice = Invoice();
        var transaction = await AttachTransactionAsync(db, invoice);

        await Create(db, Partners()).ExecuteAsync(invoice, "tester");

        var stored = await db.Context.StorageTransactions
            .FirstAsync(t => t.Key == transaction.Key);

        Assert.Equal(invoice.Key, stored.SalesInvoiceKey);
        Assert.Equal(StorageTransactionsStatus.Invoiced, stored.TransactionStatus);
    }

    /// <summary>
    /// Segunda parede da cadeia: a UF da filial é coluna nova e nula nas bases já implantadas.
    /// Com natureza padrão resolvida, o CFOP é o que falha — e o romaneio segue faturando.
    /// </summary>
    [Fact]
    public async Task Shipment_billing_without_branch_state_creates_the_invoice_without_cfop()
    {
        var db = TestDb.CreateUnitOfWork();
        await SeedBranchAsync(db, stateCode: null);
        var defaultCode = await SeedDefaultUsageAsync(db);

        var invoice = Invoice();
        await AttachTransactionAsync(db, invoice);

        await Create(db, Partners()).ExecuteAsync(invoice, "tester");

        var item = invoice.Items.Single();

        Assert.Equal(defaultCode, item.UsageCode);
        Assert.Null(item.Cfop);
    }

    /// <summary>
    /// Contraprova da tolerância: com o cadastro completo, o CFOP continua sendo resolvido e
    /// congelado na linha. É o comportamento de hoje em STANDALONE, que não pode regredir.
    /// </summary>
    [Fact]
    public async Task Shipment_billing_with_complete_setup_still_resolves_the_cfop()
    {
        var db = TestDb.CreateUnitOfWork();
        await SeedBranchAsync(db, "RS");
        var defaultCode = await SeedDefaultUsageAsync(db);

        var invoice = Invoice();
        await AttachTransactionAsync(db, invoice);

        await Create(db, Partners(state: "RS")).ExecuteAsync(invoice, "tester");

        var item = invoice.Items.Single();

        Assert.Equal(defaultCode, item.UsageCode);
        Assert.Equal("5102", item.Cfop);
        Assert.Equal("Venda de grãos", item.UsageName);
    }

    /// <summary>
    /// Fora do estado o CFOP muda — prova que a tolerância não achatou a resolução.
    /// </summary>
    [Fact]
    public async Task Shipment_billing_out_of_state_resolves_the_interstate_cfop()
    {
        var db = TestDb.CreateUnitOfWork();
        await SeedBranchAsync(db, "RS");
        await SeedDefaultUsageAsync(db);

        var invoice = Invoice();
        await AttachTransactionAsync(db, invoice);

        await Create(db, Partners(state: "SP")).ExecuteAsync(invoice, "tester");

        Assert.Equal("6102", invoice.Items.Single().Cfop);
    }

    /// <summary>
    /// O avulso não herda a tolerância: sem natureza explícita continua recusado na tela.
    /// </summary>
    [Fact]
    public async Task Standalone_invoice_without_usage_still_fails()
    {
        var db = TestDb.CreateUnitOfWork();
        await SeedBranchAsync(db, "RS");
        await SeedDefaultUsageAsync(db);

        await Assert.ThrowsAsync<DefaultException>(() =>
            Create(db, Partners()).ExecuteAsync(Invoice(), "tester"));
    }

    /// <summary>
    /// E o avulso com natureza explícita continua recusando o cadastro incompleto — aqui a
    /// UF da filial. Sem isso, a tolerância teria vazado para o caminho errado.
    /// </summary>
    [Fact]
    public async Task Standalone_invoice_without_branch_state_still_fails()
    {
        var db = TestDb.CreateUnitOfWork();
        await SeedBranchAsync(db, stateCode: null);
        var usageCode = await SeedDefaultUsageAsync(db);

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            Create(db, Partners()).ExecuteAsync(Invoice(usageCode), "tester"));

        Assert.Contains("sem UF cadastrada", ex.Message);
    }
}
