using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Tests.SalesContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.SalesInvoices;

/// <summary>
/// Spec 2026-10-07 §4.2: na filial que emite NF-e pelo Siagro, o faturamento aplica o complemento fiscal do contrato —
/// natureza e condição SOBRESCREVEM o corpo (D6) — e recusa o contrato sem complemento completo. Fora da regra (SAPB1,
/// STANDALONE sem a chave, devolução, linha sem contrato) nada muda.
/// </summary>
public class SalesInvoicesFiscalComplementApplierTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private SalesInvoicesFiscalComplementApplier Applier(string erp = "STANDALONE") =>
        new(_db, TaxTestServices.Gate(_db, erp));

    private async Task<SalesContract> SeedAsync(
        bool issuesNfe = true, string code = "CT0001", SalesContractFiscalComplement? complement = null)
    {
        if (!_db.Context.Branchs.Any())
            _db.Context.Branchs.Add(new Branch
            {
                Code = "01", BranchName = "CEAGUI", StateCode = "SP", TaxRegime = TaxRegime.Normal, IssuesNfe = issuesNfe,
            });

        var contract = SalesContractsAllocationTestSupport.NewContract(totalVolume: 1_000m);
        contract.Code = code;
        _db.Context.SalesContracts.Add(contract);

        if (complement is not null)
        {
            complement.SalesContractKey = contract.Key;
            _db.Context.SalesContractFiscalComplements.Add(complement);
        }

        await _db.SaveChangesAsync();
        return contract;
    }

    private static SalesContractFiscalComplement Complete(int usage = 3, int condition = 11, string? info = null) => new()
    {
        UsageCode = usage, PaymentConditionCode = condition, AdditionalInfo = info,
        CustomerOrderNumber = "PED-123", CustomerOrderItem = "10",
    };

    private static (SalesInvoice Invoice, SalesInvoiceItem Item) Invoice(
        Guid? contractKey, SalesInvoiceType type = SalesInvoiceType.Normal, bool nfeReturn = false)
    {
        var invoice = SalesContractsAllocationTestSupport.NewInvoice(InvoiceStatus.Pending, type);
        invoice.BranchCode = "01";
        invoice.IsNfeReturn = nfeReturn;
        invoice.PaymentConditionCode = 5;
        var item = SalesContractsAllocationTestSupport.NewItem(invoice, contractKey, releaseKey: null, 100m);
        item.UsageCode = 2;
        return (invoice, item);
    }

    [Fact]
    public async Task Gate_active_without_complement_is_refused()
    {
        var contract = await SeedAsync(code: "CT0042");
        var (invoice, _) = Invoice(contract.Key);

        var e = await Assert.ThrowsAsync<DefaultException>(() => Applier().ApplyToDocumentAsync(invoice));

        Assert.Equal(
            "O contrato CT0042 não tem complemento fiscal com natureza de operação e condição de pagamento. Peça ao fiscal para completá-lo.",
            e.Message);
    }

    [Fact]
    public async Task Gate_active_with_incomplete_complement_is_refused()
    {
        var contract = await SeedAsync(complement: new SalesContractFiscalComplement { UsageCode = 3 });
        var (invoice, _) = Invoice(contract.Key);

        var e = await Assert.ThrowsAsync<DefaultException>(() => Applier().ApplyToDocumentAsync(invoice));

        Assert.Contains("CT0001", e.Message);
    }

    [Fact]
    public async Task Gate_active_overwrites_usage_and_payment_condition()
    {
        var contract = await SeedAsync(complement: Complete());
        var (invoice, item) = Invoice(contract.Key);

        await Applier().ApplyToDocumentAsync(invoice);

        Assert.Equal(3, item.UsageCode);
        Assert.Equal(11, invoice.PaymentConditionCode);
    }

    [Fact]
    public async Task Order_fields_are_copied_to_the_line()
    {
        var contract = await SeedAsync(complement: Complete());
        var (invoice, item) = Invoice(contract.Key);

        await Applier().ApplyToDocumentAsync(invoice);

        Assert.Equal("PED-123", item.CustomerOrderNumber);
        Assert.Equal("10", item.CustomerOrderItem);
    }

    [Fact]
    public async Task Contract_text_goes_before_the_operator_text_and_is_not_duplicated()
    {
        var contract = await SeedAsync(complement: Complete(info: "Pedido 77"));
        var (invoice, _) = Invoice(contract.Key);
        invoice.TaxPayerComments = "Placa ABC";

        await Applier().ApplyToDocumentAsync(invoice);
        Assert.Equal("Pedido 77 | Placa ABC", invoice.TaxPayerComments);

        await Applier().ApplyToDocumentAsync(invoice);
        Assert.Equal("Pedido 77 | Placa ABC", invoice.TaxPayerComments);
    }

    [Fact]
    public async Task Contract_text_alone_when_the_operator_wrote_nothing()
    {
        var contract = await SeedAsync(complement: Complete(info: "Pedido 77"));
        var (invoice, _) = Invoice(contract.Key);

        await Applier().ApplyToDocumentAsync(invoice);

        Assert.Equal("Pedido 77", invoice.TaxPayerComments);
    }

    /// <summary>Vários contratos: os textos na ordem em que os contratos aparecem nas linhas, depois o do operador.</summary>
    [Fact]
    public async Task Contract_texts_keep_the_line_order_before_the_operator_text()
    {
        var first = await SeedAsync(complement: Complete(info: "A"));
        var second = await SeedAsync(code: "CT0002", complement: Complete(info: "B"));
        var (invoice, _) = Invoice(first.Key);
        SalesContractsAllocationTestSupport.NewItem(invoice, second.Key, releaseKey: null, 50m);
        invoice.TaxPayerComments = "operador";

        await Applier().ApplyToDocumentAsync(invoice);
        Assert.Equal("A | B | operador", invoice.TaxPayerComments);

        await Applier().ApplyToDocumentAsync(invoice);
        Assert.Equal("A | B | operador", invoice.TaxPayerComments);
    }

    /// <summary>Spec §4.2 item 3: a regra reaplicada na alteração/inclusão de linha também leva o texto, sem repetir.</summary>
    [Fact]
    public async Task Line_path_prepends_the_contract_text_once()
    {
        var contract = await SeedAsync(complement: Complete(info: "Pedido 77"));
        var (invoice, item) = Invoice(contract.Key);
        invoice.PaymentConditionCode = null;
        invoice.TaxPayerComments = "Placa ABC";

        await Applier().ApplyToLineAsync(invoice, item);
        Assert.Equal("Pedido 77 | Placa ABC", invoice.TaxPayerComments);

        await Applier().ApplyToLineAsync(invoice, item);
        Assert.Equal("Pedido 77 | Placa ABC", invoice.TaxPayerComments);
    }

    [Fact]
    public async Task Two_contracts_with_different_conditions_are_refused()
    {
        var first = await SeedAsync(complement: Complete(condition: 11));
        var second = await SeedAsync(code: "CT0002", complement: Complete(condition: 12));
        var (invoice, _) = Invoice(first.Key);
        SalesContractsAllocationTestSupport.NewItem(invoice, second.Key, releaseKey: null, 50m);

        var e = await Assert.ThrowsAsync<DefaultException>(() => Applier().ApplyToDocumentAsync(invoice));

        Assert.Equal("Os contratos deste documento têm condições de pagamento diferentes no complemento fiscal.", e.Message);
    }

    [Fact]
    public async Task Two_contracts_with_the_same_condition_are_applied_line_by_line()
    {
        var first = await SeedAsync(complement: Complete(usage: 3, condition: 11));
        var second = await SeedAsync(code: "CT0002", complement: Complete(usage: 4, condition: 11));
        var (invoice, firstItem) = Invoice(first.Key);
        var secondItem = SalesContractsAllocationTestSupport.NewItem(invoice, second.Key, releaseKey: null, 50m);

        await Applier().ApplyToDocumentAsync(invoice);

        Assert.Equal(3, firstItem.UsageCode);
        Assert.Equal(4, secondItem.UsageCode);
        Assert.Equal(11, invoice.PaymentConditionCode);
    }

    [Theory]
    [InlineData("SAPB1", true)]
    [InlineData("STANDALONE", false)]
    public async Task Gate_inactive_changes_nothing(string erp, bool issuesNfe)
    {
        var contract = await SeedAsync(issuesNfe: issuesNfe); // sem complemento: nem assim recusa
        var (invoice, item) = Invoice(contract.Key);

        await Applier(erp).ApplyToDocumentAsync(invoice);
        await Applier(erp).ApplyToLineAsync(invoice, item);

        Assert.Equal(2, item.UsageCode);
        Assert.Equal(5, invoice.PaymentConditionCode);
        Assert.Null(item.CustomerOrderNumber);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Return_and_own_return_change_nothing(bool nfeReturn)
    {
        var contract = await SeedAsync(); // sem complemento: a devolução não pede
        var (invoice, item) = Invoice(contract.Key, SalesInvoiceType.Return, nfeReturn);

        await Applier().ApplyToDocumentAsync(invoice);
        await Applier().ApplyToLineAsync(invoice, item);

        Assert.Equal(2, item.UsageCode);
        Assert.Equal(5, invoice.PaymentConditionCode);
    }

    [Fact]
    public async Task Line_without_contract_changes_nothing()
    {
        await SeedAsync();
        var (invoice, item) = Invoice(contractKey: null);

        await Applier().ApplyToDocumentAsync(invoice);
        await Applier().ApplyToLineAsync(invoice, item);

        Assert.Equal(2, item.UsageCode);
        Assert.Equal(5, invoice.PaymentConditionCode);
    }

    [Fact]
    public async Task Line_path_sets_condition_only_when_header_is_empty_and_refuses_a_different_one()
    {
        var contract = await SeedAsync(complement: Complete(condition: 11));

        // Cabeçalho vazio: recebe a condição do complemento.
        var (empty, emptyItem) = Invoice(contract.Key);
        empty.PaymentConditionCode = null;
        await Applier().ApplyToLineAsync(empty, emptyItem);
        Assert.Equal(11, empty.PaymentConditionCode);
        Assert.Equal(3, emptyItem.UsageCode);
        Assert.Equal("PED-123", emptyItem.CustomerOrderNumber);

        // Mesma condição: segue.
        var (same, sameItem) = Invoice(contract.Key);
        same.PaymentConditionCode = 11;
        await Applier().ApplyToLineAsync(same, sameItem);
        Assert.Equal(11, same.PaymentConditionCode);

        // Condição diferente no cabeçalho: recusa.
        var (other, otherItem) = Invoice(contract.Key);
        other.PaymentConditionCode = 5;
        var e = await Assert.ThrowsAsync<DefaultException>(() => Applier().ApplyToLineAsync(other, otherItem));
        Assert.Equal("Os contratos deste documento têm condições de pagamento diferentes no complemento fiscal.", e.Message);
    }

    [Fact]
    public async Task Line_path_refuses_a_contract_without_complement()
    {
        var contract = await SeedAsync(code: "CT0099");
        var (invoice, item) = Invoice(contract.Key);

        var e = await Assert.ThrowsAsync<DefaultException>(() => Applier().ApplyToLineAsync(invoice, item));

        Assert.Contains("CT0099", e.Message);
    }
}
