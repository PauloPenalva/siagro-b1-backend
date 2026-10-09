using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Helpers;

namespace SiagroB1.Application.Tests.Reports;

public class InvoiceReportTextTests
{
    private sealed class Request : InvoiceReportRequest;

    [Theory]
    [InlineData("123", "1", "123/1")]
    [InlineData("123", null, "123")]
    [InlineData("123", " ", "123")]
    [InlineData(null, "1", "")]
    [InlineData("", "", "")]
    public void DocumentNumber_NeverLeavesALooseSlash(string? number, string? series, string expected) =>
        Assert.Equal(expected, InvoiceReportText.DocumentNumber(number, series));

    [Fact]
    public void EffectiveStatuses_EmptyMeansEverythingButCancelled() =>
        Assert.Equal(
            new[] { InvoiceStatus.Pending, InvoiceStatus.Confirmed, InvoiceStatus.Returned },
            InvoiceReportText.EffectiveStatuses(null));

    [Fact]
    public void EffectiveStatuses_KeepsAnExplicitChoice() =>
        Assert.Equal(
            new[] { InvoiceStatus.Cancelled },
            InvoiceReportText.EffectiveStatuses([InvoiceStatus.Cancelled]));

    [Theory]
    [InlineData(null, "Pendente")]
    [InlineData(InvoiceStatus.Pending, "Pendente")]
    [InlineData(InvoiceStatus.Confirmed, "Confirmado")]
    [InlineData(InvoiceStatus.Cancelled, "Cancelado")]
    [InlineData(InvoiceStatus.Returned, "Retornado")]
    public void Status_TranslatesToPtBr(InvoiceStatus? status, string expected) =>
        Assert.Equal(expected, InvoiceReportText.Status(status));

    [Theory]
    [InlineData(NfeStatus.None, "")]
    [InlineData(NfeStatus.Processing, "Em processamento")]
    [InlineData(NfeStatus.Authorized, "Autorizada")]
    [InlineData(NfeStatus.Rejected, "Rejeitada")]
    [InlineData(NfeStatus.Denied, "Denegada")]
    [InlineData(NfeStatus.Cancelled, "Cancelada")]
    [InlineData(NfeStatus.Voided, "Inutilizada")]
    public void Nfe_TranslatesToPtBr(NfeStatus status, string expected) =>
        Assert.Equal(expected, InvoiceReportText.Nfe(status));

    [Theory]
    [InlineData("C001", "COOPERATIVA", "(C001) COOPERATIVA")]
    [InlineData("C001", null, "C001")]
    [InlineData(null, "COOPERATIVA", "COOPERATIVA")]
    [InlineData(null, null, "")]
    public void Partner_PrefixesTheCode(string? code, string? name, string expected) =>
        Assert.Equal(expected, InvoiceReportText.Partner(code, name));

    [Theory]
    [InlineData("10001", "SOJA", "SOJA (10001)")]
    [InlineData("10001", null, "10001")]
    [InlineData(null, "SOJA DO XML", "SOJA DO XML")]
    [InlineData(null, null, "Sem produto vinculado")]
    public void Product_FallsBack(string? code, string? name, string expected) =>
        Assert.Equal(expected, InvoiceReportText.Product(code, name));

    [Fact]
    public void BranchName_PrefersShortNameThenNameThenCode()
    {
        Assert.Equal("MATRIZ", InvoiceReportText.BranchName(new Branch { Code = "01", BranchName = "MATRIZ LTDA", ShortName = "MATRIZ" }, "01"));
        Assert.Equal("MATRIZ LTDA", InvoiceReportText.BranchName(new Branch { Code = "01", BranchName = "MATRIZ LTDA" }, "01"));
        Assert.Equal("01", InvoiceReportText.BranchName(null, "01"));
    }

    [Theory]
    [InlineData(false, false, "Informe o período de emissão.")]
    [InlineData(true, false, "Informe o período de emissão.")]
    public void Validate_RequiresBothDates(bool hasFrom, bool hasTo, string expected)
    {
        var request = new Request
        {
            FromDate = hasFrom ? new DateTime(2026, 7, 1) : default,
            ToDate = hasTo ? new DateTime(2026, 7, 31) : default,
        };
        Assert.Equal(expected, InvoiceReportText.Validate(request));
    }

    [Fact]
    public void Validate_RejectsInvertedPeriod()
    {
        var request = new Request { FromDate = new DateTime(2026, 7, 31), ToDate = new DateTime(2026, 7, 1) };
        Assert.Equal("A data final não pode ser anterior à inicial.", InvoiceReportText.Validate(request));
    }

    [Fact]
    public void Validate_AcceptsAValidPeriod()
    {
        var request = new Request { FromDate = new DateTime(2026, 7, 1), ToDate = new DateTime(2026, 7, 1) };
        Assert.Null(InvoiceReportText.Validate(request));
    }

    [Fact]
    public void BuildFilters_ListsOnlyWhatWasInformed()
    {
        var request = new Request
        {
            FromDate = new DateTime(2026, 7, 1),
            ToDate = new DateTime(2026, 7, 31),
            CardCode = "C001",
            NfeStatuses = [NfeStatus.Authorized],
        };

        var text = InvoiceReportText.BuildFilters(request, standalone: true, "Cliente", "COOPERATIVA", null, null, ["Tipo: Normal"]);

        Assert.Equal(
            "Emissão: 01/07/2026 a 31/07/2026 | Cliente: COOPERATIVA | Situação: Pendente, Confirmado, Retornado | Situação NF-e: Autorizada | Tipo: Normal",
            text);
    }

    [Fact]
    public void BuildFilters_LabelsNoneAsNotIssued()
    {
        var request = new Request
        {
            FromDate = new DateTime(2026, 7, 1),
            ToDate = new DateTime(2026, 7, 31),
            NfeStatuses = [NfeStatus.None, NfeStatus.Authorized],
        };

        var text = InvoiceReportText.BuildFilters(request, standalone: true, "Cliente", null, null, null, []);

        Assert.EndsWith("Situação NF-e: Não emitida, Autorizada", text);
    }

    [Fact]
    public void BuildFilters_InSapB1_OmitsTheNfeFilter()
    {
        var request = new Request
        {
            FromDate = new DateTime(2026, 7, 1),
            ToDate = new DateTime(2026, 7, 31),
            NfeStatuses = [NfeStatus.Authorized],
        };

        var text = InvoiceReportText.BuildFilters(request, standalone: false, "Cliente", null, null, null, []);

        Assert.Equal("Emissão: 01/07/2026 a 31/07/2026 | Situação: Pendente, Confirmado, Retornado", text);
    }

    [Fact]
    public void BuildFilters_WithoutDescriptions_FallsBackToCodes()
    {
        var request = new Request
        {
            FromDate = new DateTime(2026, 7, 1),
            ToDate = new DateTime(2026, 7, 31),
            BranchCode = "01",
            ItemCode = "10001",
            CardCode = "C001",
        };

        var text = InvoiceReportText.BuildFilters(request, true, "Emitente", null, null, null, []);

        Assert.Equal(
            "Emissão: 01/07/2026 a 31/07/2026 | Filial: 01 | Emitente: C001 | Produto: 10001 | Situação: Pendente, Confirmado, Retornado",
            text);
    }
}
