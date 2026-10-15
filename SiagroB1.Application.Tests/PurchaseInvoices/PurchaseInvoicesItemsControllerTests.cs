using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Web.Controllers;

namespace SiagroB1.Application.Tests.PurchaseInvoices;

/// <summary>
/// B1: "Incluir Item" na edição do documento de entrada dava "405 Method Not Allowed". A grade é
/// ligada em <c>Items</c> (relativa ao cabeçalho), então o <c>create()</c> do UI5 posta em
/// <c>PurchaseInvoices({key})/Items</c> — caminho que só tinha GET. O POST por convenção
/// (<c>PurchaseInvoicesItems</c>) não cobre a navegação.
/// </summary>
public class PurchaseInvoicesItemsControllerTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private PurchaseInvoicesItemsController NewController() =>
        new PurchaseInvoicesItemsController(new PurchaseInvoicesItemsGetService(_db),
            new PurchaseInvoicesItemsCreateService(
                _db,
                new FakeItemService(new Dictionary<string, string> { ["SOJA"] = "SOJA DO CADASTRO" }),
                TaxTestServices.InactivePurchaseApply(_db)),
            null!, null!)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

    private async Task<PurchaseInvoice> SeedAsync()
    {
        var invoice = new PurchaseInvoice
        {
            Key = Guid.NewGuid(), CardCode = "F0001", InvoiceStatus = InvoiceStatus.Pending,
        };
        _db.Context.PurchaseInvoices.Add(invoice);
        await _db.Context.SaveChangesAsync();
        return invoice;
    }

    [Fact]
    public void Navigation_post_is_routed_in_both_odata_path_styles()
    {
        var templates = typeof(PurchaseInvoicesItemsController)
            .GetMethods()
            .SelectMany(m => m.GetCustomAttributes<HttpPostAttribute>())
            .Select(a => a.Template)
            .ToList();

        Assert.Contains("odata/PurchaseInvoices({key:guid})/Items", templates);
        Assert.Contains("odata/PurchaseInvoices/{key:guid}/Items", templates);
    }

    [Fact]
    public async Task Navigation_post_creates_the_line_under_the_route_invoice()
    {
        var invoice = await SeedAsync();
        var controller = NewController();
        var line = new PurchaseInvoiceItem { ItemCode = "SOJA", UnitOfMeasureCode = "KG", Quantity = 10m };

        var result = await controller.PostToInvoice(invoice.Key, line);

        Assert.True(result is not ObjectResult { StatusCode: 500 or 400 }, (result as ObjectResult)?.Value?.ToString());
        Assert.Equal(invoice.Key, line.PurchaseInvoiceKey);
        var saved = await _db.Context.PurchaseInvoicesItems.AsNoTracking().SingleAsync();
        Assert.Equal(invoice.Key, saved.PurchaseInvoiceKey);
    }

    [Fact]
    public async Task Route_key_wins_over_the_body_invoice_key()
    {
        var invoice = await SeedAsync();
        var line = new PurchaseInvoiceItem
        {
            ItemCode = "SOJA", UnitOfMeasureCode = "KG", Quantity = 10m, PurchaseInvoiceKey = Guid.NewGuid(),
        };

        await NewController().PostToInvoice(invoice.Key, line);

        Assert.Equal(invoice.Key, (await _db.Context.PurchaseInvoicesItems.AsNoTracking().SingleAsync()).PurchaseInvoiceKey);
    }

    [Fact]
    public async Task Navigation_post_on_a_confirmed_invoice_is_a_bad_request()
    {
        var invoice = new PurchaseInvoice
        {
            Key = Guid.NewGuid(), CardCode = "F0001", InvoiceStatus = InvoiceStatus.Confirmed,
        };
        _db.Context.PurchaseInvoices.Add(invoice);
        await _db.Context.SaveChangesAsync();

        var result = await NewController().PostToInvoice(
            invoice.Key, new PurchaseInvoiceItem { ItemCode = "SOJA", UnitOfMeasureCode = "KG", Quantity = 1m });

        Assert.IsType<Microsoft.AspNetCore.OData.Results.BadRequestODataResult>(result);
    }
}
