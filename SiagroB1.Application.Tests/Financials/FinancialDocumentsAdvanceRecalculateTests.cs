using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Financials;

/// <summary>
/// Spec 2026-10-14 (compensação de adiantamento): o adiantamento tem dois eixos — PAGO (SettledAmount) e APLICADO
/// (AppliedAmount) —, e o crédito disponível é pago − aplicado. O provisório segue somando tudo no baixado.
/// </summary>
public class FinancialDocumentsAdvanceRecalculateTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private async Task<FinancialDocument> AddDocumentAsync(
        FinancialDocumentNature nature, decimal netAmount,
        FinancialDocumentOrigin originType = FinancialDocumentOrigin.Manual, Guid? originKey = null)
    {
        var document = new FinancialDocument
        {
            Key = Guid.NewGuid(), Code = $"FN-{Guid.NewGuid():N}"[..12], CardCode = "F0001",
            Direction = FinancialDirection.Payable, Nature = nature, Status = FinancialDocumentStatus.Open,
            DueDate = new DateTime(2026, 12, 31), NetAmount = netAmount, Currency = CurrencyType.Brl,
            OriginType = originType, OriginKey = originKey,
        };
        _db.Context.FinancialDocuments.Add(document);
        await _db.Context.SaveChangesAsync();
        return document;
    }

    private static FinancialSettlement Line(
        FinancialDocument document, decimal amount, FinancialSettlementOrigin origin,
        Guid? related = null, Guid? reversed = null) => new()
    {
        Key = Guid.NewGuid(), FinancialDocumentKey = document.Key, SettlementDate = new DateTime(2026, 10, 14),
        Amount = amount, Origin = origin, RelatedDocumentKey = related, ReversedSettlementKey = reversed,
    };

    private async Task SaveLinesAsync(params FinancialSettlement[] lines)
    {
        _db.Context.FinancialSettlements.AddRange(lines);
        await _db.Context.SaveChangesAsync();
    }

    [Fact]
    public async Task Paid_and_applied_axes_are_split()
    {
        var advance = await AddDocumentAsync(FinancialDocumentNature.Advance, 10_000m);
        await SaveLinesAsync(
            Line(advance, 10_000m, FinancialSettlementOrigin.Manual),
            Line(advance, 3_000m, FinancialSettlementOrigin.AdvanceApplication, related: Guid.NewGuid()),
            Line(advance, 2_000m, FinancialSettlementOrigin.AdvanceTransfer, related: Guid.NewGuid()));

        await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(_db.Context, advance.Key);

        Assert.Equal(10_000m, advance.SettledAmount);
        Assert.Equal(5_000m, advance.AppliedAmount);
        Assert.Equal(5_000m, advance.AvailableAdvanceAmount);
        Assert.Equal(FinancialDocumentStatus.Settled, advance.Status);
    }

    [Fact]
    public async Task Reversal_follows_the_axis_of_the_reversed_line()
    {
        var advance = await AddDocumentAsync(FinancialDocumentNature.Advance, 10_000m);
        var application = Line(advance, 3_000m, FinancialSettlementOrigin.AdvanceApplication, related: Guid.NewGuid());
        var secondPayment = Line(advance, 2_000m, FinancialSettlementOrigin.Manual);
        await SaveLinesAsync(
            Line(advance, 8_000m, FinancialSettlementOrigin.Manual),
            secondPayment,
            application,
            Line(advance, -3_000m, FinancialSettlementOrigin.Reversal, reversed: application.Key),
            Line(advance, -2_000m, FinancialSettlementOrigin.Reversal, reversed: secondPayment.Key));

        await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(_db.Context, advance.Key);

        Assert.Equal(8_000m, advance.SettledAmount);
        Assert.Equal(0m, advance.AppliedAmount);
        Assert.Equal(8_000m, advance.AvailableAdvanceAmount);
        Assert.Equal(FinancialDocumentStatus.PartiallySettled, advance.Status);
    }

    [Fact]
    public async Task Pending_lines_count_before_save()
    {
        var advance = await AddDocumentAsync(FinancialDocumentNature.Advance, 10_000m);
        await SaveLinesAsync(Line(advance, 10_000m, FinancialSettlementOrigin.Manual));
        _db.Context.FinancialSettlements.Add(
            Line(advance, 4_000m, FinancialSettlementOrigin.AdvanceApplication, related: Guid.NewGuid()));

        await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(_db.Context, advance.Key);

        Assert.Equal(10_000m, advance.SettledAmount);
        Assert.Equal(4_000m, advance.AppliedAmount);
        Assert.Equal(6_000m, advance.AvailableAdvanceAmount);
    }

    [Fact]
    public async Task Incoming_transfer_counts_as_paid()
    {
        var originalKey = Guid.NewGuid();
        var split = await AddDocumentAsync(FinancialDocumentNature.Advance, 7_000m,
            FinancialDocumentOrigin.FinancialDocument, originalKey);
        await SaveLinesAsync(Line(split, 7_000m, FinancialSettlementOrigin.AdvanceTransfer, related: originalKey));

        await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(_db.Context, split.Key);

        Assert.Equal(7_000m, split.SettledAmount);
        Assert.Equal(0m, split.AppliedAmount);
        Assert.Equal(7_000m, split.AvailableAdvanceAmount);
        Assert.Equal(FinancialDocumentStatus.Settled, split.Status);
    }

    [Fact]
    public async Task Outgoing_transfer_of_a_split_born_advance_counts_as_applied()
    {
        var originalKey = Guid.NewGuid();
        var split = await AddDocumentAsync(FinancialDocumentNature.Advance, 7_000m,
            FinancialDocumentOrigin.FinancialDocument, originalKey);
        await SaveLinesAsync(
            Line(split, 7_000m, FinancialSettlementOrigin.AdvanceTransfer, related: originalKey),
            Line(split, 2_000m, FinancialSettlementOrigin.AdvanceTransfer, related: Guid.NewGuid()));

        await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(_db.Context, split.Key);

        Assert.Equal(7_000m, split.SettledAmount);
        Assert.Equal(2_000m, split.AppliedAmount);
        Assert.Equal(5_000m, split.AvailableAdvanceAmount);
    }

    [Fact]
    public async Task Provisional_counts_the_application_as_settlement()
    {
        var provisional = await AddDocumentAsync(FinancialDocumentNature.Provisional, 1_000m);
        await SaveLinesAsync(Line(provisional, 1_000m, FinancialSettlementOrigin.AdvanceApplication, related: Guid.NewGuid()));

        await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(_db.Context, provisional.Key);

        Assert.Equal(1_000m, provisional.SettledAmount);
        Assert.Equal(0m, provisional.AppliedAmount);
        Assert.Equal(0m, provisional.AvailableAdvanceAmount);
        Assert.Equal(FinancialDocumentStatus.Settled, provisional.Status);
    }

    [Theory]
    [InlineData("10000", "3000", "7000")]
    [InlineData("3000", "5000", "0")]
    [InlineData("100.005", "0", "100.00")]
    public void Available_advance_amount_is_paid_minus_applied_never_negative(string settled, string applied, string expected)
    {
        var advance = new FinancialDocument
        {
            CardCode = "F0001", Nature = FinancialDocumentNature.Advance,
            SettledAmount = decimal.Parse(settled, System.Globalization.CultureInfo.InvariantCulture),
            AppliedAmount = decimal.Parse(applied, System.Globalization.CultureInfo.InvariantCulture),
        };

        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), advance.AvailableAdvanceAmount);
    }

    [Fact]
    public void Non_advance_has_no_credit()
    {
        var provisional = new FinancialDocument
        {
            CardCode = "F0001", Nature = FinancialDocumentNature.Provisional, SettledAmount = 500m,
        };

        Assert.Equal(0m, provisional.AvailableAdvanceAmount);
    }
}
