using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Usages;

/// <summary>As três alíquotas do Funrural vão e voltam pela natureza (spec 2026-10-09 §2).</summary>
public class UsageFunruralTests
{
    private static UsageService Service(UnitOfWork db) => new(db, NullLogger<UsageService>.Instance);

    private static UsageModel Purchase() => new()
    {
        Name = "Compra produtor rural PF", Direction = UsageDirection.Incoming,
        CfopIncomingInState = "1102", CfopIncomingOutState = "2102", PisCst = "74", CofinsCst = "74",
        RequiresQuantity = true, FunruralInssRate = 1.32m, FunruralRatRate = 0.11m, FunruralSenarRate = 0.20m,
    };

    [Fact]
    public async Task Rates_are_saved_and_read_back()
    {
        var name = Guid.NewGuid().ToString();
        var db = TestDb.CreateUnitOfWork(name);

        var created = await Service(db).CreateAsync(Purchase());
        var read = await Service(TestDb.CreateUnitOfWork(name)).GetByIdAsync(created.Code);

        Assert.Equal((1.32m, 0.11m, 0.20m), (read!.FunruralInssRate, read.FunruralRatRate, read.FunruralSenarRate));
    }

    [Fact]
    public async Task Update_can_clear_the_rates()
    {
        var name = Guid.NewGuid().ToString();
        var db = TestDb.CreateUnitOfWork(name);
        var created = await Service(db).CreateAsync(Purchase());

        var model = Purchase();
        model.FunruralInssRate = null;
        model.FunruralRatRate = null;
        model.FunruralSenarRate = 0.20m;
        await Service(db).UpdateAsync(created.Code, model);

        var read = await Service(TestDb.CreateUnitOfWork(name)).GetByIdAsync(created.Code);
        Assert.Equal(((decimal?)null, (decimal?)null, 0.20m), (read!.FunruralInssRate, read.FunruralRatRate, read.FunruralSenarRate));
    }
}
