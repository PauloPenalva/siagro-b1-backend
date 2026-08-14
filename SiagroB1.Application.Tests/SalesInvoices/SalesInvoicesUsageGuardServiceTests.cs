using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.SalesInvoices;

/// <summary>
/// Obrigatoriedade condicional dirigida pela natureza de operação. A tela some com o campo;
/// quem garante é o serviço.
/// </summary>
public class SalesInvoicesUsageGuardServiceTests
{
    private static UsageService Usages(UnitOfWork db) =>
        new(db, NullLogger<UsageService>.Instance);

    private static async Task<int> SeedUsageAsync(
        UnitOfWork db,
        bool requiresContract = false,
        bool requiresQuantity = true,
        bool requiresWeight = false,
        bool inactive = false,
        ContractValueEffect value = ContractValueEffect.None)
    {
        var created = await Usages(db).CreateAsync(new UsageModel
        {
            Name = "Natureza de teste",
            CfopOutgoingInState = "5102",
            CfopOutgoingOutState = "6102",
            RequiresContract = requiresContract,
            RequiresQuantity = requiresQuantity,
            RequiresWeight = requiresWeight,
            ContractValueEffect = value,
            Inactive = inactive,
        });

        return created.Code;
    }

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
            RequiresWeight = false,
            IsDefault = true,
        });

        return created.Code;
    }

    private static SalesInvoice Invoice(
        int? usageCode,
        decimal quantity = 100m,
        Guid? contractKey = null,
        decimal grossWeight = 0m,
        decimal netWeight = 0m) => new()
        {
            Key = Guid.NewGuid(),
            CardCode = "C0001",
            GrossWeight = grossWeight,
            NetWeight = netWeight,
            Items =
            [
                new SalesInvoiceItem
                {
                    Key = Guid.NewGuid(),
                    ItemCode = "SOJA",
                    UnitOfMeasureCode = "KG",
                    Quantity = quantity,
                    UnitPrice = 2.5m,
                    SalesContractKey = contractKey,
                    // A natureza é da LINHA.
                    UsageCode = usageCode,
                }
            ],
        };

    /// <summary>Romaneio mínimo: só existe para marcar o documento como vindo de romaneio.</summary>
    private static StorageTransaction Transaction() => new()
    {
        Key = Guid.NewGuid(),
        CardCode = "C0001",
        ItemCode = "SOJA",
        UnitOfMeasureCode = "KG",
        WarehouseCode = "01",
    };

    private static SalesInvoicesUsageGuardService Service(UnitOfWork db) =>
        new(Usages(db));

    [Fact]
    public async Task Invoice_without_usage_fails()
    {
        var db = TestDb.CreateUnitOfWork();

        await Assert.ThrowsAsync<DefaultException>(() =>
            Service(db).ValidateAsync(Invoice(usageCode: null)));
    }

    [Fact]
    public async Task Inactive_usage_fails()
    {
        var db = TestDb.CreateUnitOfWork();
        var usageCode = await SeedUsageAsync(db, inactive: true);

        await Assert.ThrowsAsync<DefaultException>(() =>
            Service(db).ValidateAsync(Invoice(usageCode)));
    }

    [Fact]
    public async Task Price_complement_without_weight_and_without_quantity_passes()
    {
        var db = TestDb.CreateUnitOfWork();
        var usageCode = await SeedUsageAsync(db,
            requiresContract: true,
            requiresQuantity: false,
            requiresWeight: false,
            value: ContractValueEffect.Add);

        var usage = (await Service(db).ValidateAsync(
            Invoice(usageCode, quantity: 0m, contractKey: Guid.NewGuid()))).Single().Usage;

        Assert.Equal(ContractValueEffect.Add, usage.ContractValueEffect);
    }

    [Fact]
    public async Task Loss_adjustment_without_quantity_fails()
    {
        var db = TestDb.CreateUnitOfWork();
        var usageCode = await SeedUsageAsync(db, requiresQuantity: true);

        await Assert.ThrowsAsync<DefaultException>(() =>
            Service(db).ValidateAsync(Invoice(usageCode, quantity: 0m)));
    }

    [Fact]
    public async Task Invoice_without_contract_passes_when_not_required()
    {
        var db = TestDb.CreateUnitOfWork();
        var usageCode = await SeedUsageAsync(db, requiresContract: false);

        var usage = (await Service(db).ValidateAsync(Invoice(usageCode))).Single().Usage;

        Assert.False(usage.RequiresContract);
    }

    [Fact]
    public async Task Invoice_without_contract_fails_when_required()
    {
        var db = TestDb.CreateUnitOfWork();
        var usageCode = await SeedUsageAsync(db, requiresContract: true);

        await Assert.ThrowsAsync<DefaultException>(() =>
            Service(db).ValidateAsync(Invoice(usageCode)));
    }

    [Fact]
    public async Task Invoice_without_weight_fails_when_required()
    {
        var db = TestDb.CreateUnitOfWork();
        var usageCode = await SeedUsageAsync(db, requiresWeight: true);

        await Assert.ThrowsAsync<DefaultException>(() =>
            Service(db).ValidateAsync(Invoice(usageCode)));
    }

    [Fact]
    public async Task Shipment_billing_without_usage_falls_back_to_the_default_usage()
    {
        var db = TestDb.CreateUnitOfWork();
        await SeedUsageAsync(db);
        var defaultCode = await SeedDefaultUsageAsync(db);

        // A natureza padrão exige contrato, como o faturamento de romaneio já exige hoje.
        var invoice = Invoice(usageCode: null, contractKey: Guid.NewGuid());
        invoice.SalesTransactions.Add(Transaction());

        var usage = (await Service(db).ValidateAsync(invoice)).Single().Usage;

        Assert.Equal(defaultCode, usage.Code);
        Assert.Equal(defaultCode, invoice.Items.Single().UsageCode);
    }

    /// <summary>
    /// Sem natureza padrão, o faturamento de romaneio segue — e a linha nasce SEM natureza.
    ///
    /// É o retrato do modo SAPB1: as naturezas vêm do OUSG e o efeito mora em USAGE_EFFECTS,
    /// tabela do Siagro que nasce vazia, então nenhuma é padrão. Recusar aqui travava a tela
    /// de Faturamento de Expedição inteira numa base recém-implantada. Pode ser tolerado
    /// porque o consumo do contrato nesse caminho vem da ORIGEM do documento, não do efeito
    /// da natureza — o que fica em branco é só metadado fiscal.
    /// </summary>
    [Fact]
    public async Task Shipment_billing_without_default_usage_resolves_no_usage()
    {
        var db = TestDb.CreateUnitOfWork();
        await SeedUsageAsync(db);

        var invoice = Invoice(usageCode: null);
        invoice.SalesTransactions.Add(Transaction());

        var resolved = await Service(db).ValidateAsync(invoice);

        Assert.Empty(resolved);
        Assert.Null(invoice.Items.Single().UsageCode);
    }

    /// <summary>
    /// A tolerância é da linha SEM natureza, não do documento de romaneio inteiro: linha com
    /// natureza explícita continua validada, mesmo vindo de romaneio.
    /// </summary>
    [Fact]
    public async Task Shipment_billing_still_validates_an_explicit_usage()
    {
        var db = TestDb.CreateUnitOfWork();
        var usageCode = await SeedUsageAsync(db, requiresContract: true);

        var invoice = Invoice(usageCode);
        invoice.SalesTransactions.Add(Transaction());

        await Assert.ThrowsAsync<DefaultException>(() =>
            Service(db).ValidateAsync(invoice));
    }

    [Fact]
    public async Task Standalone_invoice_never_falls_back_to_the_default_usage()
    {
        var db = TestDb.CreateUnitOfWork();
        await SeedDefaultUsageAsync(db);

        // Sem romaneio: a natureza tem que ser explícita.
        await Assert.ThrowsAsync<DefaultException>(() =>
            Service(db).ValidateAsync(Invoice(usageCode: null)));
    }

    [Fact]
    public async Task Marking_a_usage_as_default_clears_the_previous_one()
    {
        var db = TestDb.CreateUnitOfWork();
        var first = await SeedDefaultUsageAsync(db);
        var second = await SeedDefaultUsageAsync(db);

        var all = (await Usages(db).GetAllAsync()).ToList();

        Assert.Single(all, u => u.IsDefault);
        Assert.Equal(second, all.Single(u => u.IsDefault).Code);
        Assert.False(all.Single(u => u.Code == first).IsDefault);
    }

    [Fact]
    public async Task Invoice_with_weight_passes_when_required()
    {
        var db = TestDb.CreateUnitOfWork();
        var usageCode = await SeedUsageAsync(db, requiresWeight: true);

        var usage = (await Service(db).ValidateAsync(
            Invoice(usageCode, grossWeight: 1000m, netWeight: 990m))).Single().Usage;

        Assert.True(usage.RequiresWeight);
    }

    /// <summary>
    /// Natureza sem linha de efeito é BARRADA, e não tratada como "não altera nada". É o caso
    /// de toda natureza recém-chegada do OUSG em modo SAPB1.
    /// </summary>
    [Fact]
    public async Task Usage_without_configured_effects_is_rejected()
    {
        var db = TestDb.CreateUnitOfWork();
        var usageCode = await SeedUsageAsync(db);

        // Some com a linha de efeito, deixando só a identidade — o que o OUSG entrega.
        db.Context.UsageEffects.RemoveRange(db.Context.UsageEffects);
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            Service(db).ValidateAsync(Invoice(usageCode)));

        Assert.Contains("sem efeito configurado", ex.Message);
    }

    /// <summary>
    /// A natureza é de LINHA: um documento pode misturar. Cada item é validado contra a SUA
    /// natureza, e o peso — que é do cabeçalho — é exigido se QUALQUER linha exigir.
    /// </summary>
    [Fact]
    public async Task Each_line_is_validated_against_its_own_usage()
    {
        var db = TestDb.CreateUnitOfWork();
        var lenient = await SeedUsageAsync(db, requiresContract: false);
        var strict = await SeedUsageAsync(db, requiresContract: true);

        var invoice = Invoice(lenient);
        invoice.Items.Add(new SalesInvoiceItem
        {
            Key = Guid.NewGuid(),
            ItemCode = "MILHO",
            UnitOfMeasureCode = "KG",
            Quantity = 50m,
            UnitPrice = 1m,
            SalesContractKey = null,
            UsageCode = strict,
        });

        // A primeira linha passaria sozinha; a segunda exige contrato e não tem.
        await Assert.ThrowsAsync<DefaultException>(() =>
            Service(db).ValidateAsync(invoice));
    }
}
