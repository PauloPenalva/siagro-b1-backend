using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Financials;

/// <summary>
/// Revínculo do adiantamento. O guard que importa é o de PARCEIRO: sem ele, mover crédito de um
/// produtor para o contrato de outro é um erro caro e silencioso.
/// </summary>
public class FinancialAdvancesRelinkContractServiceTests
{
    private readonly IUnitOfWork _db = TestDb.CreateUnitOfWork();

    private FinancialAdvancesRelinkContractService Service() =>
        new(_db, new FinancialDocumentChangeLogService(_db.Context));

    private static PurchaseContract NewPurchaseContract(
        string code, string cardCode = "F0001", string branchCode = "03",
        ContractStatus status = ContractStatus.Approved,
        CurrencyType currency = CurrencyType.Brl) => new()
    {
        Key = Guid.NewGuid(),
        Code = code,
        CardCode = cardCode,
        BranchCode = branchCode,
        ItemCode = "SOJA",
        UnitOfMeasureCode = "KG",
        HarvestSeasonCode = "24/25",
        DeliveryLocationCode = "01",
        TotalVolume = 1_000m,
        Type = ContractType.Fixed,
        StandardCashFlowDate = new DateTime(2026, 12, 31),
        StandardCurrency = currency,
        PaymentTerms = "Depósito no Banco X",
        Status = status,
    };

    private static SalesContract NewSalesContract(string code, string cardCode = "C0001") => new()
    {
        Key = Guid.NewGuid(),
        Code = code,
        CardCode = cardCode,
        BranchCode = "03",
        ItemCode = "SOJA",
        UnitOfMeasureCode = "KG",
        HarvestSeasonCode = "24/25",
        TotalVolume = 1_000m,
        StandardCashFlowDate = new DateTime(2026, 12, 31),
        StandardCurrency = CurrencyType.Brl,
        Status = ContractStatus.Approved,
    };

    private async Task<(FinancialDocument advance, PurchaseContract origin)> SeedAsync(
        FinancialDirection direction = FinancialDirection.Payable,
        FinancialDocumentNature nature = FinancialDocumentNature.Advance)
    {
        var origin = NewPurchaseContract("PC-0001");
        _db.Context.PurchaseContracts.Add(origin);

        var advance = new FinancialDocument
        {
            Key = Guid.NewGuid(),
            Code = "FN000217",
            CardCode = "F0001",
            BranchCode = "03",
            Direction = direction,
            Nature = nature,
            Status = FinancialDocumentStatus.Settled,
            DueDate = new DateTime(2026, 12, 31),
            NetAmount = 50_000m,
            SettledAmount = 50_000m,
            Currency = CurrencyType.Brl,
            OriginType = FinancialDocumentOrigin.Manual,
            OriginDocNumber = "PC-0001",
            PurchaseContractKey = origin.Key,
            PaymentTermsText = "Depósito no Banco X",
        };

        _db.Context.FinancialDocuments.Add(advance);
        await _db.Context.SaveChangesAsync();
        return (advance, origin);
    }

    [Fact]
    public async Task Relinking_moves_the_key_the_origin_doc_number_and_logs_one_line()
    {
        var (advance, _) = await SeedAsync();
        var target = NewPurchaseContract("PC-0002");
        _db.Context.PurchaseContracts.Add(target);
        await _db.Context.SaveChangesAsync();

        await Service().ExecuteAsync(advance.Key, "Purchase", target.Key, "tester");

        Assert.Equal(target.Key, advance.PurchaseContractKey);
        Assert.Null(advance.SalesContractKey);
        Assert.Equal("PC-0002", advance.OriginDocNumber);

        var log = await _db.Context.FinancialDocumentChangeLogs
            .SingleAsync(x => x.FinancialDocumentKey == advance.Key);

        Assert.Equal(FinancialDocumentChangeLogFields.Contract, log.Field);
        Assert.Equal("PC-0001", log.OldValue);
        Assert.Equal("PC-0002", log.NewValue);
        Assert.Equal("tester", log.ChangedBy);
    }

    /// <summary>
    /// PaymentTermsText é a cópia de "onde pagar" segundo o contrato de ORIGEM, e para um
    /// adiantamento já pago foi por ali que o dinheiro saiu. Reescrevê-lo apagaria histórico.
    /// </summary>
    [Fact]
    public async Task Relinking_does_not_touch_the_payment_terms_text()
    {
        var (advance, _) = await SeedAsync();
        var target = NewPurchaseContract("PC-0002");
        target.PaymentTerms = "Outro banco totalmente diferente";
        _db.Context.PurchaseContracts.Add(target);
        await _db.Context.SaveChangesAsync();

        await Service().ExecuteAsync(advance.Key, "Purchase", target.Key, "tester");

        Assert.Equal("Depósito no Banco X", advance.PaymentTermsText);
    }

    [Fact]
    public async Task Refuses_a_target_contract_of_a_different_partner()
    {
        var (advance, _) = await SeedAsync();
        var target = NewPurchaseContract("PC-0002", cardCode: "F0002");
        _db.Context.PurchaseContracts.Add(target);
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(advance.Key, "Purchase", target.Key, "tester"));

        Assert.Contains("parceiro", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refuses_a_target_contract_that_is_not_approved()
    {
        var (advance, _) = await SeedAsync();
        var target = NewPurchaseContract("PC-0002", status: ContractStatus.InApproval);
        _db.Context.PurchaseContracts.Add(target);
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(advance.Key, "Purchase", target.Key, "tester"));

        Assert.Contains("aprovado", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Direção cruzada é erro de negócio, não de digitação: a pagar só migra para compra.</summary>
    [Fact]
    public async Task Refuses_a_sales_contract_for_a_payable_advance()
    {
        var (advance, _) = await SeedAsync();
        var target = NewSalesContract("SC-0002", cardCode: "F0001");
        _db.Context.SalesContracts.Add(target);
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(advance.Key, "Sales", target.Key, "tester"));

        Assert.Contains("a pagar", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refuses_a_target_contract_in_a_different_branch()
    {
        var (advance, _) = await SeedAsync();
        var target = NewPurchaseContract("PC-0002", branchCode: "01");
        _db.Context.PurchaseContracts.Add(target);
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(advance.Key, "Purchase", target.Key, "tester"));

        Assert.Contains("filial", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refuses_a_target_contract_in_a_different_currency()
    {
        var (advance, _) = await SeedAsync();
        var target = NewPurchaseContract("PC-0002", currency: CurrencyType.Usd);
        _db.Context.PurchaseContracts.Add(target);
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(advance.Key, "Purchase", target.Key, "tester"));

        Assert.Contains("moeda", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refuses_relinking_to_the_contract_it_is_already_linked_to()
    {
        var (advance, origin) = await SeedAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(advance.Key, "Purchase", origin.Key, "tester"));

        Assert.Contains("já está vinculado", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refuses_a_document_that_is_not_an_advance()
    {
        var (advance, _) = await SeedAsync(nature: FinancialDocumentNature.Provisional);
        var target = NewPurchaseContract("PC-0002");
        _db.Context.PurchaseContracts.Add(target);
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(advance.Key, "Purchase", target.Key, "tester"));

        Assert.Contains("adiantamento", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refuses_an_invalid_contract_type()
    {
        var (advance, _) = await SeedAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(advance.Key, "Barter", Guid.NewGuid(), "tester"));

        Assert.Contains("tipo de contrato", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
