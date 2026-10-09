using SiagroB1.Domain.Enums;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Helpers;
using static SiagroB1.Application.Tests.Support.ContractPositionSeed;

namespace SiagroB1.Application.Tests.Reports;

public class ContractPositionTextTests
{
    private sealed class Request : ContractPositionReportRequest;

    [Theory]
    [InlineData(ContractStatus.Draft, "Rascunho")]
    [InlineData(ContractStatus.Approved, "Aprovado")]
    [InlineData(ContractStatus.Finished, "Finalizado")]
    [InlineData(ContractStatus.Canceled, "Cancelado")]
    [InlineData(ContractStatus.InApproval, "Em Aprovação")]
    [InlineData(ContractStatus.Rejected, "Rejeitado")]
    public void StatusText_MatchesTheScreen(ContractStatus status, string expected) =>
        Assert.Equal(expected, ContractPositionText.StatusText(status));

    [Fact]
    public void StatusText_NullIsDraft() => Assert.Equal("Rascunho", ContractPositionText.StatusText(null));

    [Theory]
    [InlineData(ContractType.Fixed, "FIX - Preço Fixo", "FIX")]
    [InlineData(ContractType.ToBeDetermined, "PAF - Preço a Fixar", "PAF")]
    public void TypeText_MatchesTheScreen(ContractType type, string full, string shortText)
    {
        Assert.Equal(full, ContractPositionText.TypeText(type));
        Assert.Equal(shortText, ContractPositionText.TypeShort(type));
    }

    [Fact]
    public void EffectiveStatuses_EmptyMeansOnlyApproved()
    {
        Assert.Equal(new[] { ContractStatus.Approved }, ContractPositionText.EffectiveStatuses(null));
        Assert.Equal(new[] { ContractStatus.Approved }, ContractPositionText.EffectiveStatuses([]));
        Assert.Equal(
            new[] { ContractStatus.Finished, ContractStatus.Approved },
            ContractPositionText.EffectiveStatuses([ContractStatus.Finished, ContractStatus.Approved, ContractStatus.Finished]));
    }

    [Fact]
    public void QueryStatuses_AddsNullOnlyWhenDraftIsChosen()
    {
        Assert.DoesNotContain(null, ContractPositionText.QueryStatuses(null));
        Assert.Contains(null, ContractPositionText.QueryStatuses([ContractStatus.Draft]));
    }

    [Fact]
    public void StatusFilter_DescribesTheChoice()
    {
        Assert.Equal("Situação: Aprovado", ContractPositionText.StatusFilter(ContractPositionText.EffectiveStatuses(null)));
        Assert.Equal("Situação: todas", ContractPositionText.StatusFilter(Enum.GetValues<ContractStatus>()));
        Assert.Equal(
            "Situação: Aprovado, Finalizado",
            ContractPositionText.StatusFilter([ContractStatus.Finished, ContractStatus.Approved]));
    }

    [Fact]
    public void DeliveryEnd_IsEmptyWithoutDeadline()
    {
        Assert.Equal("31/12/2026", ContractPositionText.DeliveryEnd(new DateTime(2026, 12, 31)));
        Assert.Equal("", ContractPositionText.DeliveryEnd(default));
        Assert.Equal("", ContractPositionText.DeliveryEnd(new DateTime(1900, 1, 1)));
        Assert.False(ContractPositionText.HasDeadline(new DateTime(1900, 1, 1)));
    }

    [Fact]
    public void BuildFilters_ListsOnlyWhatWasInformed()
    {
        var positions = new List<ContractPosition>
        {
            new() { ItemCode = "10001", ItemName = "SOJA EM GRÃOS", CardCode = "F001", CardName = "PRODUTOR RURAL" },
        };

        Assert.Equal(
            "Posição em: 08/10/2026 | Lado: Compra e venda | Situação: Aprovado",
            ContractPositionText.BuildFilters(Today, new Request(), ContractPositionSide.Both, null, positions));

        var request = new Request
        {
            BranchCode = "01",
            ItemCode = "10001",
            HarvestSeasonCode = "25/26",
            CardCode = "F001",
            Type = ContractType.ToBeDetermined,
            DeliveryEndDateUntil = new DateTime(2026, 12, 31),
            Statuses = [ContractStatus.Approved, ContractStatus.Finished],
        };
        Assert.Equal(
            "Posição em: 08/10/2026 | Filial: MATRIZ | Produto: SOJA EM GRÃOS (10001) | Safra: 25/26 | " +
            "Parceiro: PRODUTOR RURAL | Tipo: PAF - Preço a Fixar | Término da entrega até: 31/12/2026 | " +
            "Situação: Aprovado, Finalizado",
            ContractPositionText.BuildFilters(Today, request, null, "MATRIZ", positions));

        Assert.Equal(
            "Posição em: 08/10/2026 | Lado: Venda | Filial: 01 | Produto: 10001 | Safra: 25/26 | Parceiro: F001 | " +
            "Tipo: PAF - Preço a Fixar | Término da entrega até: 31/12/2026 | Situação: Aprovado, Finalizado",
            ContractPositionText.BuildFilters(Today, request, ContractPositionSide.Sales, null, []));
    }

    [Fact]
    public void InSectionOrder_CashFlowThenDeadlineThenCode()
    {
        var positions = new List<ContractPosition>
        {
            new() { Code = "PC000005", StandardCashFlowDate = null, DeliveryEndDate = new DateTime(2026, 11, 30) },
            new() { Code = "PC000004", StandardCashFlowDate = new DateTime(2026, 11, 10), DeliveryEndDate = default },
            new() { Code = "PC000003", StandardCashFlowDate = new DateTime(2026, 11, 10), DeliveryEndDate = new DateTime(2026, 12, 31) },
            new() { Code = "PC000002", StandardCashFlowDate = new DateTime(2026, 11, 10), DeliveryEndDate = new DateTime(2026, 11, 30) },
            new() { Code = "PC000001", StandardCashFlowDate = new DateTime(2026, 11, 10), DeliveryEndDate = new DateTime(2026, 11, 30) },
            new() { Code = "PC000006", StandardCashFlowDate = new DateTime(2026, 10, 15), DeliveryEndDate = new DateTime(2027, 1, 31) },
        };

        Assert.Equal(
            new[] { "PC000006", "PC000001", "PC000002", "PC000003", "PC000004", "PC000005" },
            ContractPositionText.InSectionOrder(positions).Select(p => p.Code));
    }
}
