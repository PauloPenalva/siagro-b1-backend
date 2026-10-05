using SiagroB1.Application.Services.Taxes;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.Taxes;

public class NfeReturnConferenceTests
{
    [Fact]
    public void Purchase_return_names_the_purchase_in_the_message()
    {
        var bought = new PurchaseInvoiceItem { ItemCode = "SOJA", CstIcms = "51", IcmsRate = 18m };
        var returned = new PurchaseInvoiceItem { ItemCode = "SOJA", CstIcms = "00", IcmsRate = 18m };

        var e = Assert.Throws<DefaultException>(() => NfeReturnConference.Ensure(returned, bought, "DEVOLUCAO DE COMPRA", "compra"));

        Assert.Equal(
            "Item SOJA: a natureza de devolução DEVOLUCAO DE COMPRA não reproduz a tributação da compra — " +
            "CST do ICMS: compra 51, devolução 00.", e.Message);
    }

    [Fact]
    public void Matching_taxation_passes()
    {
        var bought = new PurchaseInvoiceItem { ItemCode = "SOJA", CstIcms = "51", IcmsRate = 18m, IcmsDeferral = 100m };
        var returned = new PurchaseInvoiceItem { ItemCode = "SOJA", CstIcms = "51", IcmsRate = 18m, IcmsDeferral = 100m };

        NfeReturnConference.Ensure(returned, bought, "DEVOLUCAO DE COMPRA", "compra");
    }
}
