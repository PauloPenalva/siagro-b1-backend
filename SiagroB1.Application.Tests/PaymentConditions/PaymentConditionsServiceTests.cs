using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.PaymentConditions;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Web.Controllers;

namespace SiagroB1.Application.Tests.PaymentConditions;

/// <summary>Cadastro de condição de pagamento (só STANDALONE): dias, início da contagem e meio.</summary>
public class PaymentConditionsServiceTests
{
    private static PaymentCondition Condition(string days = "30, 60 ,90", string means = "15") => new()
    {
        Name = "30/60/90 boleto", Days = days, StartRule = PaymentStartRule.IssueDate, PaymentMeans = means,
    };

    [Fact]
    public async Task Create_normalizes_the_days()
    {
        var db = TestDb.CreateUnitOfWork();

        await new PaymentConditionsService(db).CreateAsync(Condition());

        Assert.Equal("30,60,90", (await db.Context.PaymentConditions.SingleAsync()).Days);
    }

    [Theory]
    [InlineData("60,30", "15", "crescentes")]
    [InlineData("30", "05", "05")]
    public async Task Create_rejects_invalid_conditions(string days, string means, string expected)
    {
        var db = TestDb.CreateUnitOfWork();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            new PaymentConditionsService(db).CreateAsync(Condition(days, means)));

        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public async Task Create_requires_a_name()
    {
        var db = TestDb.CreateUnitOfWork();
        var condition = Condition();
        condition.Name = " ";

        await Assert.ThrowsAsync<DefaultException>(() => new PaymentConditionsService(db).CreateAsync(condition));
    }

    [Fact]
    public async Task Delete_is_refused_when_a_document_uses_the_condition()
    {
        var db = TestDb.CreateUnitOfWork();
        var service = new PaymentConditionsService(db);
        var condition = await service.CreateAsync(Condition());
        db.Context.SalesInvoices.Add(new SalesInvoice { Key = Guid.NewGuid(), CardCode = "C1", PaymentConditionCode = condition.Code });
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => service.DeleteAsync(condition.Code));

        Assert.Contains("inative", ex.Message);
    }

    [Fact]
    public void Controller_refuses_outside_standalone()
    {
        var db = TestDb.CreateUnitOfWork();
        var controller = new PaymentConditionsController(new PaymentConditionsService(db), TaxTestServices.Config("SAPB1"));

        // ODataController.BadRequest(string) devolve BadRequestODataResult, não BadRequestObjectResult.
        var result = Assert.IsAssignableFrom<IStatusCodeActionResult>(controller.Get());
        Assert.Equal(StatusCodes.Status400BadRequest, result.StatusCode);
    }
}
