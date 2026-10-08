using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.Nfe;

/// <summary>NF-e com a numeração inutilizada: o documento fica congelado como o da NF-e cancelada.</summary>
public class NfeVoidedLockTests
{
    [Fact]
    public void Voided_is_frozen_with_its_own_message()
    {
        Assert.True(NfeLockRules.IsFrozen(NfeStatus.Voided));
        Assert.Equal(NfeLockRules.VoidedMessage, NfeLockRules.FrozenMessage(NfeStatus.Voided));
        Assert.Equal("A numeração da NF-e deste documento foi inutilizada na SEFAZ: o documento não pode mudar.",
            NfeLockRules.VoidedMessage);
    }

    [Fact]
    public void Voided_sales_invoice_is_not_deletable()
    {
        var ex = Assert.Throws<DefaultException>(() =>
            SalesInvoiceNfeLock.EnsureDeletable(new SalesInvoice { CardCode = "C1", NfeStatus = NfeStatus.Voided }));

        Assert.Contains("inutilizada", ex.Message);
    }

    [Fact]
    public void Voided_purchase_invoice_is_not_deletable()
    {
        var ex = Assert.Throws<DefaultException>(() =>
            PurchaseInvoiceNfeLock.EnsureDeletable(new PurchaseInvoice { CardCode = "F1", NfeStatus = NfeStatus.Voided }));

        Assert.Contains("inutilizada", ex.Message);
    }
}
