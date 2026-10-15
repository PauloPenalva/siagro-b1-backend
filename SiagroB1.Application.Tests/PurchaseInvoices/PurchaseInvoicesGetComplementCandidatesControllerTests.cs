using Microsoft.AspNetCore.Mvc;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Dtos;
using SiagroB1.Web.Functions.PurchaseInvoices;
using static SiagroB1.Application.Tests.Support.ValueComplementNfeSeed;

namespace SiagroB1.Application.Tests.PurchaseInvoices;

/// <summary>M4: os códigos chegam como literal de string do OData — aspas nas pontas e o apóstrofo interno dobrado.</summary>
public class PurchaseInvoicesGetComplementCandidatesControllerTests
{
    [Fact]
    public async Task Code_with_an_apostrophe_is_read_from_the_odata_literal()
    {
        var s = await SeedAsync();
        await ChangeAsync(s.Db, s.OriginalKey, o => o.CardCode = "D'AGUA");
        var controller = new PurchaseInvoicesGetComplementCandidatesController(
            new PurchaseInvoicesGetComplementCandidatesService(s.Db));

        var result = await controller.Get("'D''AGUA'", "'01'");

        var rows = Assert.IsAssignableFrom<IEnumerable<PurchaseInvoiceComplementCandidateDto>>(
            Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(s.OriginalItemKey, Assert.Single(rows).ItemKey);
    }
}
