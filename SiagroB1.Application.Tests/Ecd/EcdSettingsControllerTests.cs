using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.OData.Formatter;
using SiagroB1.Application.Services.Ecd;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Infra;
using SiagroB1.Web.Controllers;
using static SiagroB1.Application.Tests.Accounting.AccountingTestData;

namespace SiagroB1.Application.Tests.Ecd;

public class EcdSettingsControllerTests
{
    private static EcdSettingsController Controller(IUnitOfWork db) =>
        new(new EcdSettingsService(db), Config())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

    private static ODataActionParameters Full() => new()
    {
        ["BranchCode"] = "1",
        ["BookNumber"] = 1,
        ["HasNire"] = false,
        ["Nire"] = null,
        ["ConstitutiveActsDate"] = null,
        ["LargeCompany"] = false,
        ["ReferentialPlanCode"] = "1",
        ["SignerName"] = "Joao Diretor",
        ["SignerCpf"] = "529.982.247-25",
        ["SignerQualificationCode"] = "203",
        ["SignerEmail"] = "diretor@empresa.com.br",
        ["SignerPhone"] = null,
        ["AccountantCrcSequence"] = null,
        ["AccountantCrcValidUntil"] = null,
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
    public async Task Save_full_payload_with_nullable_keys_null_is_accepted()
    {
        var db = TestDb.CreateUnitOfWork();
        var (status, _) = Read(await Controller(db).SaveAsync(Full()));
        Assert.Equal(200, status);
        Assert.Single(db.Context.EcdSettings);
    }

    [Fact]
    public async Task Save_stores_accountant_crc_certificate()
    {
        var db = TestDb.CreateUnitOfWork();
        var p = Full();
        p["AccountantCrcSequence"] = " SP/2026/000123 ";
        p["AccountantCrcValidUntil"] = new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.FromHours(-3));
        Assert.Equal(200, Read(await Controller(db).SaveAsync(p)).Status);

        var row = db.Context.EcdSettings.AsEnumerable().Single();
        Assert.Equal("SP/2026/000123", row.AccountantCrcSequence);
        Assert.Equal(new DateTime(2026, 12, 31), row.AccountantCrcValidUntil);

        var missing = Full(); missing.Remove("AccountantCrcValidUntil");
        Assert.Equal("Parâmetro AccountantCrcValidUntil ausente.", Read(await Controller(db).SaveAsync(missing)).Body);
    }

    [Fact]
    public async Task Save_missing_HasNire_is_rejected_and_stored_row_unchanged()
    {
        var db = TestDb.CreateUnitOfWork();
        var controller = Controller(db);
        var first = Full(); first["HasNire"] = true; first["Nire"] = "41201234560"; first["BookNumber"] = 3;
        Assert.Equal(200, Read(await controller.SaveAsync(first)).Status);

        var partial = Full(); partial.Remove("HasNire");
        var (status, body) = Read(await controller.SaveAsync(partial));

        Assert.Equal(400, status);
        Assert.Equal("Parâmetro HasNire ausente.", body);
        var row = db.Context.EcdSettings.AsEnumerable().Single();
        Assert.True(row.HasNire);
        Assert.Equal("41201234560", row.Nire);
        Assert.Equal(3, row.BookNumber);
    }

    [Fact]
    public async Task Save_missing_nullable_key_is_rejected()
    {
        var db = TestDb.CreateUnitOfWork();
        var partial = Full(); partial.Remove("SignerPhone");
        var (status, body) = Read(await Controller(db).SaveAsync(partial));
        Assert.Equal(400, status);
        Assert.Equal("Parâmetro SignerPhone ausente.", body);
        Assert.Empty(db.Context.EcdSettings);
    }

    [Fact]
    public async Task Save_non_numeric_BookNumber_is_rejected()
    {
        var db = TestDb.CreateUnitOfWork();
        var p = Full(); p["BookNumber"] = "abc";
        var (status, body) = Read(await Controller(db).SaveAsync(p));
        Assert.Equal(400, status);
        Assert.Equal("Número do livro inválido.", body);
        Assert.Empty(db.Context.EcdSettings);
    }
}
