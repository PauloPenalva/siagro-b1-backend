using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Interfaces;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using static SiagroB1.Application.Tests.Market.MarketTradeTicketTestHelpers;

namespace SiagroB1.Application.Tests.Market;

public class MarketTradeTicketServiceTests
{
    private const string Approve = PermissionCodes.MarketTicketApprove;
    private const string Board = PermissionCodes.MarketBoardApprove;

    [Fact]
    public async Task Create_FreezesServerCalculation_IgnoringClientNumbers()
    {
        var db = await SeedTicketScenarioAsync();

        var ticket = await TicketService(db).CreateAsync(Input(price: 135m), "comprador", Today, CancellationToken.None);

        var saved = await db.Context.MarketTradeTickets.AsNoTracking().SingleAsync();
        Assert.Equal(ticket.Id, saved.Id);
        Assert.Equal(MarketTradeTicketStatus.Pending, saved.Status);
        Assert.Equal(137.67m, saved.CalculatedPrice);
        Assert.Equal(135m, saved.NegotiatedPrice);
        Assert.Equal(5.7267m, saved.NetFx);
        Assert.Equal(1188.25m, saved.ExchangeQuote);
        Assert.Equal(180m, saved.FreightPerTon);
        Assert.Equal("26/27", saved.HarvestSeasonCode);
        Assert.Equal("PRODUTOR TESTE", saved.CardName);
        Assert.Equal("ARM 01", saved.WarehouseName);
        Assert.Equal("AGENTE", saved.AgentName);
        Assert.Equal("comprador", saved.CreatedBy);
        Assert.Equal(new DateTime(2027, 4, 15), saved.PaymentDate);
        Assert.Equal(new DateTime(2027, 2, 1), saved.DeliveryStartDate);
        var steps = JsonDocument.Parse(saved.StepsJson!).RootElement;
        Assert.True(steps.GetArrayLength() > 0);
        Assert.True(steps[0].TryGetProperty("key", out _));
        Assert.True(steps[0].TryGetProperty("unit", out _));
        Assert.Equal(-1.9394m, saved.DeviationPercent);
    }

    [Fact]
    public async Task Create_RowWithoutPrice_Rejected()
    {
        var db = await SeedTicketScenarioAsync();
        db.Context.MarketQuotes.Remove(await db.Context.MarketQuotes.SingleAsync(q => q.InstrumentCode == "ZSF7"));
        await db.SaveChangesAsync();

        var e = await Assert.ThrowsAsync<BusinessException>(() =>
            TicketService(db).CreateAsync(Input(), "comprador", Today, CancellationToken.None));
        Assert.StartsWith("Linha sem preço", e.Message);

        var missingLine = Input() with { PriceTableLineId = 99 };
        await Assert.ThrowsAsync<BusinessException>(() =>
            TicketService(db).CreateAsync(missingLine, "comprador", Today, CancellationToken.None));
        Assert.Empty(db.Context.MarketTradeTickets);
    }

    [Theory]
    [InlineData("X", "01", 1, "Parceiro")]
    [InlineData("F0001", "99", 1, "Armazém")]
    [InlineData("F0001", "01", 9, "Agente")]
    public async Task Create_UnknownPartnerOrWarehouseOrAgent_Rejected(string card, string warehouse, int agent, string field)
    {
        var db = await SeedTicketScenarioAsync();

        var e = await Assert.ThrowsAsync<BusinessException>(() => TicketService(db)
            .CreateAsync(Input(card: card, warehouse: warehouse, agent: agent), "comprador", Today, CancellationToken.None));
        Assert.Contains(field, e.Message);
    }

    [Fact]
    public async Task Create_UnknownBranchOrRegion_OrZeroVolume_Rejected()
    {
        var db = await SeedTicketScenarioAsync();
        var service = TicketService(db);
        await Assert.ThrowsAsync<BusinessException>(() =>
            service.CreateAsync(Input() with { BranchCode = "9" }, "c", Today, CancellationToken.None));
        await Assert.ThrowsAsync<BusinessException>(() =>
            service.CreateAsync(Input() with { LogisticRegionCode = "X" }, "c", Today, CancellationToken.None));
        await Assert.ThrowsAsync<BusinessException>(() =>
            service.CreateAsync(Input(volumeKg: 0), "c", Today, CancellationToken.None));
    }

    [Fact]
    public async Task Create_Deviation_And_BoardFlags()
    {
        var db = await SeedTicketScenarioAsync();
        var service = TicketService(db);

        var normal = await service.CreateAsync(Input(), "c", Today, CancellationToken.None);
        Assert.False(normal.RequiresBoardApproval);
        Assert.Null(normal.BoardApprovalReason);
        Assert.Equal(0m, normal.DeviationPercent);

        var big = await service.CreateAsync(Input(volumeKg: 1_200_000m), "c", Today, CancellationToken.None);
        Assert.True(big.RequiresBoardApproval);
        Assert.Equal("Volume de 1.200,000 t acima do limite de 1.000,000 t", big.BoardApprovalReason);

        var both = await service.CreateAsync(Input(volumeKg: 1_200_000m, price: 132.85m), "c", Today, CancellationToken.None);
        Assert.Equal(-3.5011m, both.DeviationPercent);
        Assert.Equal("Volume de 1.200,000 t acima do limite de 1.000,000 t; desvio de preço de -3,50 % acima do limite de 2,00 %",
            both.BoardApprovalReason);

        var deviation = await service.CreateAsync(Input(price: 141m), "c", Today, CancellationToken.None);
        Assert.True(deviation.RequiresBoardApproval);
        Assert.StartsWith("Desvio de preço de 2,42 %", deviation.BoardApprovalReason);

        var noLimits = await SeedTicketScenarioAsync(null, null);
        var free = await TicketService(noLimits).CreateAsync(Input(volumeKg: 9_000_000m, price: 200m), "c", Today, CancellationToken.None);
        Assert.False(free.RequiresBoardApproval);
    }

    [Fact]
    public async Task Approve_WithoutPermission_Forbidden()
    {
        var db = await SeedTicketScenarioAsync();
        var ticket = await TicketService(db).CreateAsync(Input(), "comprador", Today, CancellationToken.None);

        await Assert.ThrowsAsync<ForbiddenException>(() => TicketService(db).ApproveAsync(ticket.Id, "x", CancellationToken.None));
        Assert.Empty(db.Context.PurchaseContracts);
    }

    [Fact]
    public async Task Approve_NoBoardNeeded_GeneratesApprovedContract()
    {
        var db = await SeedTicketScenarioAsync();
        var ticket = await TicketService(db).CreateAsync(Input(), "comprador", Today, CancellationToken.None);

        var approved = await TicketService(db, Approve).ApproveAsync(ticket.Id, "gerente", CancellationToken.None);

        Assert.Equal(MarketTradeTicketStatus.Approved, approved.Status);
        Assert.Equal("gerente", approved.ApprovedBy);
        Assert.Null(approved.BoardApprovedBy);
        var contract = await db.Context.PurchaseContracts.AsNoTracking().SingleAsync();
        Assert.Equal(contract.Key, approved.PurchaseContractKey);
        Assert.Equal(contract.Code, approved.PurchaseContractCode);
        Assert.NotNull(contract.Code);
        Assert.Equal(ContractStatus.Approved, contract.Status);
        Assert.Equal(ContractType.Fixed, contract.Type);
        Assert.Equal(100_000m, contract.TotalVolume);
        Assert.Equal(2.2945m, contract.StandardPrice);
        Assert.Equal(new DateTime(2027, 4, 15), contract.StandardCashFlowDate);
        Assert.Equal("comprador", contract.CreatedBy);
        Assert.Equal("gerente", contract.ApprovedBy);
        Assert.Equal("KG", contract.UnitOfMeasureCode);
        Assert.Equal("TON", contract.FreightUmCode);
        Assert.Equal("01", contract.DeliveryLocationCode);
        Assert.Equal("OESTE", contract.LogisticRegionCode);
        Assert.Equal("0001", contract.BranchCode);
        Assert.Equal(FreightTerms.Fob, contract.FreightTerms);
        Assert.Equal($"Gerado pela boleta nº {ticket.Id}", contract.Comments);
        Assert.Single(db.Context.FinancialDocuments);
    }

    [Fact]
    public async Task Approve_BoardNeeded_FirstLevelGoesToPendingBoard_NoContract()
    {
        var db = await SeedTicketScenarioAsync();
        var ticket = await TicketService(db).CreateAsync(Input(volumeKg: 1_500_000m), "comprador", Today, CancellationToken.None);

        var result = await TicketService(db, Approve).ApproveAsync(ticket.Id, "gerente", CancellationToken.None);

        Assert.Equal(MarketTradeTicketStatus.PendingBoard, result.Status);
        Assert.Equal("gerente", result.ApprovedBy);
        Assert.Empty(db.Context.PurchaseContracts);

        var final = await TicketService(db, Board).ApproveAsync(ticket.Id, "diretor", CancellationToken.None);
        Assert.Equal(MarketTradeTicketStatus.Approved, final.Status);
        Assert.Equal("diretor", final.BoardApprovedBy);
        Assert.Equal("diretor", (await db.Context.PurchaseContracts.AsNoTracking().SingleAsync()).ApprovedBy);
    }

    [Fact]
    public async Task Approve_BoardMember_FromPending_FinishesInOneStep()
    {
        var db = await SeedTicketScenarioAsync();
        var ticket = await TicketService(db).CreateAsync(Input(volumeKg: 1_500_000m), "comprador", Today, CancellationToken.None);

        var result = await TicketService(db, Approve, Board).ApproveAsync(ticket.Id, "diretor", CancellationToken.None);

        Assert.Equal(MarketTradeTicketStatus.Approved, result.Status);
        Assert.Equal("diretor", result.ApprovedBy);
        Assert.Equal("diretor", result.BoardApprovedBy);
        Assert.NotNull(result.BoardApprovedAt);
        Assert.Single(db.Context.PurchaseContracts);
    }

    [Fact]
    public async Task Approve_PendingBoard_WithoutBoardPermission_Forbidden()
    {
        var db = await SeedTicketScenarioAsync();
        var ticket = await TicketService(db).CreateAsync(Input(volumeKg: 1_500_000m), "comprador", Today, CancellationToken.None);
        await TicketService(db, Approve).ApproveAsync(ticket.Id, "gerente", CancellationToken.None);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            TicketService(db, Approve).ApproveAsync(ticket.Id, "gerente2", CancellationToken.None));
        Assert.Empty(db.Context.PurchaseContracts);
    }

    [Fact]
    public async Task Approve_Twice_SecondFails_OneContract()
    {
        var db = await SeedTicketScenarioAsync();
        var ticket = await TicketService(db).CreateAsync(Input(), "comprador", Today, CancellationToken.None);
        await TicketService(db, Approve).ApproveAsync(ticket.Id, "gerente", CancellationToken.None);
        db.Context.ChangeTracker.Clear();

        await Assert.ThrowsAsync<BusinessException>(() =>
            TicketService(db, Approve, Board).ApproveAsync(ticket.Id, "outro", CancellationToken.None));

        Assert.Single(db.Context.PurchaseContracts);
        Assert.Single(db.Context.FinancialDocuments);
    }

    [Fact]
    public async Task Reject_RequiresReason_AndOpenStatus()
    {
        var db = await SeedTicketScenarioAsync();
        var ticket = await TicketService(db).CreateAsync(Input(), "comprador", Today, CancellationToken.None);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            TicketService(db).RejectAsync(ticket.Id, "caro", "comprador", CancellationToken.None));
        await Assert.ThrowsAsync<BusinessException>(() =>
            TicketService(db, Approve).RejectAsync(ticket.Id, " ", "gerente", CancellationToken.None));

        var rejected = await TicketService(db, Approve).RejectAsync(ticket.Id, "preço alto", "gerente", CancellationToken.None);
        Assert.Equal(MarketTradeTicketStatus.Rejected, rejected.Status);
        Assert.Equal("preço alto", rejected.RejectionReason);
        Assert.Equal("gerente", rejected.RejectedBy);

        await Assert.ThrowsAsync<BusinessException>(() =>
            TicketService(db, Approve).RejectAsync(ticket.Id, "de novo", "gerente", CancellationToken.None));
        await Assert.ThrowsAsync<BusinessException>(() =>
            TicketService(db, Approve).ApproveAsync(ticket.Id, "gerente", CancellationToken.None));
    }

    [Fact]
    public async Task Reject_PendingBoard_RequiresBoardPermission()
    {
        var db = await SeedTicketScenarioAsync();
        var ticket = await TicketService(db).CreateAsync(Input(volumeKg: 1_500_000m), "comprador", Today, CancellationToken.None);
        await TicketService(db, Approve).ApproveAsync(ticket.Id, "gerente", CancellationToken.None);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            TicketService(db, Approve).RejectAsync(ticket.Id, "não", "gerente", CancellationToken.None));
        var rejected = await TicketService(db, Board).RejectAsync(ticket.Id, "não", "diretor", CancellationToken.None);
        Assert.Equal(MarketTradeTicketStatus.Rejected, rejected.Status);
    }

    [Fact]
    public async Task Cancel_ByCreator_Ok_ByOther_Forbidden()
    {
        var db = await SeedTicketScenarioAsync();
        var first = await TicketService(db).CreateAsync(Input(), "comprador", Today, CancellationToken.None);
        var second = await TicketService(db).CreateAsync(Input(), "comprador", Today, CancellationToken.None);

        await Assert.ThrowsAsync<ForbiddenException>(() => TicketService(db).CancelAsync(first.Id, "outro", CancellationToken.None));

        var canceled = await TicketService(db).CancelAsync(first.Id, "comprador", CancellationToken.None);
        Assert.Equal(MarketTradeTicketStatus.Canceled, canceled.Status);
        Assert.Equal("comprador", canceled.CanceledBy);

        var byApprover = await TicketService(db, Approve).CancelAsync(second.Id, "gerente", CancellationToken.None);
        Assert.Equal(MarketTradeTicketStatus.Canceled, byApprover.Status);

        await Assert.ThrowsAsync<BusinessException>(() => TicketService(db).CancelAsync(first.Id, "comprador", CancellationToken.None));
    }

    [Fact]
    public async Task Approve_UnknownTicket_NotFound()
    {
        var db = await SeedTicketScenarioAsync();
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            TicketService(db, Approve).ApproveAsync(42, "gerente", CancellationToken.None));
    }
}
