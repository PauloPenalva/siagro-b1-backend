using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.OData.Deltas;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.Ecf;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra;
using SiagroB1.Web.Controllers;
using static SiagroB1.Application.Tests.Accounting.AccountingTestData;

namespace SiagroB1.Application.Tests.Ecf;

public class EcfControllersTests
{
    private static T Ctx<T>(T c) where T : ControllerBase
    {
        c.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        return c;
    }

    private static EcfSettingsController Settings(IUnitOfWork db) => Ctx(new EcfSettingsController(new EcfSettingsService(db), Config()));

    private static ODataActionParameters Full() => new()
    {
        ["BranchCode"] = "1",
        ["AssessmentForm"] = "Annual",
        ["MainCnae"] = "0111301",
        ["LegalEntityQualification"] = "01",
        ["PreviousEcfHash"] = null,
    };

    private static (int? Status, string? Body) Read(IActionResult r) =>
        ((r as IStatusCodeActionResult)?.StatusCode,
         r switch
         {
             Microsoft.AspNetCore.OData.Results.BadRequestODataResult bad => bad.Error.Message,
             ObjectResult o => o.Value?.ToString(),
             _ => null,
         });

    [Fact]
    public async Task Save_full_payload_is_accepted()
    {
        var db = TestDb.CreateUnitOfWork();
        Assert.Equal(200, Read(await Settings(db).SaveAsync(Full())).Status);
        Assert.Equal("0111301", db.Context.EcfSettings.Single().MainCnae);
    }

    [Theory]
    [InlineData("BranchCode")]
    [InlineData("AssessmentForm")]
    [InlineData("MainCnae")]
    [InlineData("LegalEntityQualification")]
    [InlineData("PreviousEcfHash")]
    public async Task Save_missing_key_is_rejected_and_nothing_stored(string key)
    {
        var db = TestDb.CreateUnitOfWork();
        var p = Full(); p.Remove(key);
        var (status, body) = Read(await Settings(db).SaveAsync(p));
        Assert.Equal(400, status);
        Assert.Equal($"Parâmetro {key} ausente.", body);
        Assert.Empty(db.Context.EcfSettings);
    }

    [Fact]
    public async Task Save_invalid_form_and_business_error_are_400()
    {
        var db = TestDb.CreateUnitOfWork();
        var p = Full(); p["AssessmentForm"] = "xyz";
        Assert.Equal("Forma de apuração inválida.", Read(await Settings(db).SaveAsync(p)).Body);
        var q = Full(); q["MainCnae"] = "12";
        var (status, body) = Read(await Settings(db).SaveAsync(q));
        Assert.Equal(400, status);
        Assert.Equal("CNAE deve ter 7 dígitos.", body);
    }

    [Fact]
    public async Task Partner_post_with_business_error_returns_400_not_500()
    {
        var db = TestDb.CreateUnitOfWork();
        var c = Ctx(new EcfPartnersController(new EcfPartnerService(db.Context, NullLogger<EcfPartnerService>.Instance), Config()));
        var (status, body) = Read(await c.Post(new EcfPartner { BranchCode = "1", Name = "X", TaxId = "123", Qualification = "49" }));
        Assert.Equal(400, status);
        Assert.Equal("CPF/CNPJ do sócio inválido.", body);
    }

    [Fact]
    public async Task Adjustment_patch_validates_and_persists()
    {
        var db = TestDb.CreateUnitOfWork();
        var service = new EcfAdjustmentService(db.Context, NullLogger<EcfAdjustmentService>.Instance);
        var c = Ctx(new EcfAdjustmentsController(service, Config()));
        var created = await service.CreateAsync(new EcfAdjustment { BranchCode = "1", Year = 2025, TableCode = "T", Amount = 10 });

        var bad = new Delta<EcfAdjustment>(); bad.TrySetPropertyValue("Amount", 0m);
        Assert.Equal(400, Read(await c.Patch(created.Id, bad)).Status);

        var good = new Delta<EcfAdjustment>(); good.TrySetPropertyValue("Amount", 25m);
        Assert.Equal(204, Read(await c.Patch(created.Id, good)).Status);
        Assert.Equal(25m, db.Context.EcfAdjustments.Single().Amount);
    }
}
