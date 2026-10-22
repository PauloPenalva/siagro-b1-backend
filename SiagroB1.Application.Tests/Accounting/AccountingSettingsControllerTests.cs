using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.OData.Formatter;
using SiagroB1.Application.Services.Accounting;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Web.Controllers;
using static SiagroB1.Application.Tests.Accounting.AccountingTestData;

namespace SiagroB1.Application.Tests.Accounting;

public class AccountingSettingsControllerTests
{
    private static AccountingSettingsController Controller(SiagroB1.Infra.IUnitOfWork db) =>
        new(new AccountingSettingsService(db), Config())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

    [Fact]
    public async Task Save_without_parameters_is_rejected_instead_of_clearing_settings()
    {
        var db = TestDb.CreateUnitOfWork();
        var controller = Controller(db);

        var result = await controller.SaveAsync(null);

        Assert.Equal(400, Assert.IsAssignableFrom<IStatusCodeActionResult>(result).StatusCode);
        Assert.Empty(db.Context.AccountingSettings);
    }

    [Fact]
    public async Task Cutover_date_keeps_the_calendar_day()
    {
        var db = TestDb.CreateUnitOfWork();
        var controller = Controller(db);

        // O OData entrega Edm.DateTimeOffset em UTC; convertido para o fuso local (-03:00) cairia no dia anterior.
        var result = await controller.SaveAsync(new ODataActionParameters
        {
            ["CutoverDate"] = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            ["ClosingFrequency"] = 1,
        });

        Assert.Equal(200, Assert.IsAssignableFrom<IStatusCodeActionResult>(result).StatusCode);
        Assert.Equal(new DateTime(2026, 1, 1), db.Context.AccountingSettings.Single().CutoverDate);
    }
}
