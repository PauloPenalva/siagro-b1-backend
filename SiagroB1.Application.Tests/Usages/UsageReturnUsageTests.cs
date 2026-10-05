using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Usages;

/// <summary>
/// Natureza de devolução (spec §5): a natureza de SAÍDA aponta a natureza de ENTRADA que a NF-e de
/// devolução de uma venda feita com ela vai usar.
/// </summary>
public class UsageReturnUsageTests
{
    private static UsageService Service(UnitOfWork db) => new(db, NullLogger<UsageService>.Instance);

    private static UsageModel Sale(string name = "Venda", int? returnUsage = null) => new()
    {
        Name = name, Direction = UsageDirection.Outgoing, CfopOutgoingInState = "5102", CfopOutgoingOutState = "6102",
        RequiresQuantity = true, ReturnUsageCode = returnUsage,
    };

    private static UsageModel Return(bool inactive = false) => new()
    {
        Name = "Entrada devolução", Direction = UsageDirection.Incoming,
        CfopIncomingInState = "1202", CfopIncomingOutState = "2202",
        PisCst = "72", CofinsCst = "72", RequiresQuantity = true, Inactive = inactive,
    };

    private static async Task<string> Rejects(Func<Task> act)
    {
        var ex = await Assert.ThrowsAsync<DefaultException>(act);
        return ex.Message;
    }

    [Fact]
    public async Task Sale_points_to_an_incoming_return_usage_and_reads_back_its_name()
    {
        var db = TestDb.CreateUnitOfWork();
        var ret = await Service(db).CreateAsync(Return());

        var sale = await Service(db).CreateAsync(Sale(returnUsage: ret.Code));
        var read = await Service(db).GetByIdAsync(sale.Code);

        Assert.Equal(ret.Code, read!.ReturnUsageCode);
        Assert.Equal("Entrada devolução", read.ReturnUsageName);
    }

    [Fact]
    public async Task Incoming_usage_cannot_have_a_return_usage()
    {
        var db = TestDb.CreateUnitOfWork();
        var ret = await Service(db).CreateAsync(Return());
        var model = Return();
        model.Name = "Outra entrada";
        model.ReturnUsageCode = ret.Code;

        Assert.Equal("Só natureza de saída tem natureza de devolução.", await Rejects(() => Service(db).CreateAsync(model)));
    }

    [Fact]
    public async Task Return_usage_must_be_incoming()
    {
        var db = TestDb.CreateUnitOfWork();
        var other = await Service(db).CreateAsync(Sale("Venda 2"));

        Assert.Equal("A natureza de devolução Venda 2 precisa ser de entrada.",
            await Rejects(() => Service(db).CreateAsync(Sale(returnUsage: other.Code))));
    }

    [Fact]
    public async Task Return_usage_must_exist()
    {
        var db = TestDb.CreateUnitOfWork();

        Assert.Equal("Natureza de devolução 999 não encontrada.",
            await Rejects(() => Service(db).CreateAsync(Sale(returnUsage: 999))));
    }

    [Fact]
    public async Task Inactive_return_usage_is_refused()
    {
        var db = TestDb.CreateUnitOfWork();
        var ret = await Service(db).CreateAsync(Return(inactive: true));

        Assert.Equal("A natureza de devolução Entrada devolução está inativa.",
            await Rejects(() => Service(db).CreateAsync(Sale(returnUsage: ret.Code))));
    }

    [Fact]
    public async Task Usage_cannot_be_its_own_return_usage()
    {
        var db = TestDb.CreateUnitOfWork();
        var sale = await Service(db).CreateAsync(Sale());
        sale.ReturnUsageCode = sale.Code;

        Assert.Equal("A natureza de devolução não pode ser a própria natureza.",
            await Rejects(() => Service(db).UpdateAsync(sale.Code, sale)));
    }

    [Fact]
    public async Task Incoming_usage_used_as_return_cannot_become_outgoing()
    {
        var db = TestDb.CreateUnitOfWork();
        var ret = await Service(db).CreateAsync(Return());
        await Service(db).CreateAsync(Sale(returnUsage: ret.Code));
        var changed = Return();
        changed.Direction = UsageDirection.Outgoing;
        changed.CfopOutgoingInState = "5102";
        changed.PisCst = null;
        changed.CofinsCst = null;

        Assert.Equal(
            "A natureza Entrada devolução é a natureza de devolução de outra natureza de saída: o tipo não pode virar Saída.",
            await Rejects(() => Service(db).UpdateAsync(ret.Code, changed)));
    }

    [Fact]
    public async Task Return_usage_in_use_cannot_be_deleted()
    {
        var db = TestDb.CreateUnitOfWork();
        var ret = await Service(db).CreateAsync(Return());
        await Service(db).CreateAsync(Sale(returnUsage: ret.Code));

        Assert.Equal(
            "Natureza de operação Entrada devolução é a natureza de devolução de outra natureza. Inative-a em vez de excluir.",
            await Rejects(() => Service(db).DeleteAsync(ret.Code)));
    }

    [Fact]
    public async Task Incoming_usage_never_keeps_a_return_usage_in_the_table()
    {
        var db = TestDb.CreateUnitOfWork();
        var ret = await Service(db).CreateAsync(Return());
        var sale = await Service(db).CreateAsync(Sale(returnUsage: ret.Code));
        sale.ReturnUsageCode = null;

        await Service(db).UpdateAsync(sale.Code, sale);

        Assert.Null((await db.Context.Usages.AsNoTracking().SingleAsync(u => u.Code == sale.Code)).ReturnUsageCode);
    }
}
