using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Usages;

/// <summary>A natureza guarda só o modo da contribuição social; as alíquotas vêm do parceiro (spec 2026-10-18).</summary>
public class UsageRuralContributionTests
{
    private static UsageService Service(UnitOfWork db) => new(db, NullLogger<UsageService>.Instance);

    private static UsageModel Purchase(RuralContributionMode mode) => new()
    {
        Name = "Compra produtor rural PF", Direction = UsageDirection.Incoming,
        CfopIncomingInState = "1102", CfopIncomingOutState = "2102", PisCst = "74", CofinsCst = "74",
        RequiresQuantity = true, RuralContributionMode = mode,
    };

    [Fact]
    public async Task Incoming_usage_round_trips_the_contribution_mode()
    {
        var name = Guid.NewGuid().ToString();
        var created = await Service(TestDb.CreateUnitOfWork(name)).CreateAsync(Purchase(RuralContributionMode.SenarOnly));

        var read = await Service(TestDb.CreateUnitOfWork(name)).GetByIdAsync(created.Code);

        Assert.Equal(RuralContributionMode.SenarOnly, read!.RuralContributionMode);
    }

    [Fact]
    public async Task Outgoing_usage_always_stores_none()
    {
        var name = Guid.NewGuid().ToString();
        var model = new UsageModel
        {
            Name = "Venda", Direction = UsageDirection.Outgoing,
            CfopOutgoingInState = "5102", CfopOutgoingOutState = "6102", PisCst = "49", CofinsCst = "49",
            RequiresQuantity = true, RuralContributionMode = RuralContributionMode.Full,
        };
        var created = await Service(TestDb.CreateUnitOfWork(name)).CreateAsync(model);

        var read = await Service(TestDb.CreateUnitOfWork(name)).GetByIdAsync(created.Code);

        Assert.Equal(RuralContributionMode.None, read!.RuralContributionMode);
    }
}
