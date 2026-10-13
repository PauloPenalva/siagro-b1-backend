using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using R = SiagroB1.Infra.Queries.PurchaseFiscalReconciliationRules;

namespace SiagroB1.Application.Tests.PurchaseFiscalReconciliations;

public class PurchaseFiscalReconciliationRulesTests
{
    [Theory]
    [InlineData(StorageTransactionType.Purchase, true)]
    [InlineData(StorageTransactionType.PurchaseReturn, true)]
    [InlineData(StorageTransactionType.PurchaseQtyComplement, true)]
    [InlineData(StorageTransactionType.PurchaseDischargeLoss, true)]
    [InlineData(StorageTransactionType.PurchaseDischargeGain, true)]
    [InlineData(StorageTransactionType.Receipt, false)]
    [InlineData(StorageTransactionType.SalesShipment, false)]
    [InlineData(StorageTransactionType.PurchasePriceComplement, false)]
    public void Only_purchase_side_transactions_are_linkable(StorageTransactionType type, bool expected) =>
        Assert.Equal(expected, R.IsLinkable(type));

    [Theory]
    [InlineData(PurchaseInvoiceType.Normal, StorageTransactionType.Purchase, true)]
    [InlineData(PurchaseInvoiceType.Normal, StorageTransactionType.PurchaseQtyComplement, true)]
    [InlineData(PurchaseInvoiceType.Normal, StorageTransactionType.PurchaseDischargeGain, true)]
    [InlineData(PurchaseInvoiceType.Normal, StorageTransactionType.PurchaseReturn, false)]
    [InlineData(PurchaseInvoiceType.Normal, StorageTransactionType.PurchaseDischargeLoss, false)]
    [InlineData(PurchaseInvoiceType.Return, StorageTransactionType.PurchaseReturn, true)]
    [InlineData(PurchaseInvoiceType.Return, StorageTransactionType.PurchaseDischargeLoss, true)]
    [InlineData(PurchaseInvoiceType.Return, StorageTransactionType.Purchase, false)]
    public void Invoice_sense_must_match_transaction(PurchaseInvoiceType invoice, StorageTransactionType type, bool expected) =>
        Assert.Equal(expected, R.SenseMatches(invoice, type));

    [Theory]
    [InlineData("12345678000199", "12345678000270", true)]
    [InlineData("12.345.678/0001-99", "12345678000199", true)]
    [InlineData("12345678901", "12345678901", true)]
    [InlineData("12345678901", "12345678902", false)]
    [InlineData("12345678000199", "87654321000199", false)]
    [InlineData(null, "12345678901", false)]
    [InlineData("", "", false)]
    public void Same_owner_compares_cpf_or_cnpj_root(string? a, string? b, bool expected) =>
        Assert.Equal(expected, R.SameOwner(a, b));

    [Fact]
    public void Kg_factor_is_one_for_kg_or_blank_unit()
    {
        Assert.Equal(1m, R.KgFactor("KG", null));
        Assert.Equal(1m, R.KgFactor("kg", null));
        Assert.Equal(1m, R.KgFactor(null, null));
    }

    [Fact]
    public void Kg_factor_uses_commercial_unit_of_the_item()
    {
        var complement = new ItemComplement { ItemCode = "SOJA", CommercialUnitOfMeasureCode = "SC", CommercialFactor = 60m };

        Assert.Equal(60m, R.KgFactor("SC", complement));
        Assert.Null(R.KgFactor("TON", complement));
        Assert.Null(R.KgFactor("SC", null));
        Assert.Null(R.KgFactor("SC", new ItemComplement { ItemCode = "SOJA", CommercialUnitOfMeasureCode = "SC" }));
    }

    [Theory]
    [InlineData(100, 0, false, false, FiscalReconciliationStatus.WithoutInvoice)]
    [InlineData(100, 100, true, false, FiscalReconciliationStatus.Reconciled)]
    [InlineData(100, 101, true, false, FiscalReconciliationStatus.Partial)]
    [InlineData(100, 101, true, true, FiscalReconciliationStatus.Reconciled)]
    [InlineData(100, 40, true, false, FiscalReconciliationStatus.Partial)]
    public void Status_follows_links_difference_and_confirmation(
        decimal physical, decimal fiscal, bool hasLinks, bool confirmed, FiscalReconciliationStatus expected) =>
        Assert.Equal(expected, R.Status(physical, fiscal, hasLinks, confirmed));

    [Fact]
    public void Tax_document_is_number_dash_series_or_internal_number()
    {
        Assert.Equal("000123-001", R.TaxDocument("000123", "001", "DE000007"));
        Assert.Equal("000123", R.TaxDocument("000123", null, "DE000007"));
        Assert.Equal("DE000007", R.TaxDocument(null, "001", "DE000007"));
    }
}
