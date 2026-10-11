using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Web.Controllers;

namespace SiagroB1.Application.Tests.StorageDeposits;

/// <summary>O vínculo SalesInvoice.ShipmentReleaseKey só nasce no "Emitir remessa": a API genérica não o aceita do cliente.</summary>
public class SalesInvoiceRemittanceLinkApiTests
{
    [Fact]
    public async Task Put_and_patch_path_keeps_the_original_release_link()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        var remittance = await seed.AddRemittanceAsync(100_000m, InvoiceStatus.Pending);
        db.Context.ChangeTracker.Clear();

        var incoming = await db.Context.SalesInvoices.AsNoTracking().Include(i => i.Items).FirstAsync(i => i.Key == remittance.Key);
        incoming.ShipmentReleaseKey = null;
        var update = new SalesInvoicesUpdateService(db, new FakeBusinessPartnerService(), TaxTestServices.InactiveApply(db),
            TaxTestServices.InactiveFiscalComplement(db), NullLogger<SalesInvoicesUpdateService>.Instance);
        await update.ExecuteAsync(remittance.Key, incoming, "tester");

        db.Context.ChangeTracker.Clear();
        Assert.Equal(seed.Release.Key, (await db.Context.SalesInvoices.FirstAsync(i => i.Key == remittance.Key)).ShipmentReleaseKey);
    }

    [Fact]
    public async Task Update_cannot_attach_a_regular_invoice_to_a_release()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        var invoice = new SalesInvoice { Key = Guid.NewGuid(), CardCode = "C1", InvoiceStatus = InvoiceStatus.Pending, BranchCode = StorageDepositSeed.Branch };
        db.Context.SalesInvoices.Add(invoice);
        await db.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        var incoming = await db.Context.SalesInvoices.AsNoTracking().FirstAsync(i => i.Key == invoice.Key);
        incoming.ShipmentReleaseKey = seed.Release.Key;
        var update = new SalesInvoicesUpdateService(db, new FakeBusinessPartnerService(), TaxTestServices.InactiveApply(db),
            TaxTestServices.InactiveFiscalComplement(db), NullLogger<SalesInvoicesUpdateService>.Instance);
        await update.ExecuteAsync(invoice.Key, incoming, "tester");

        db.Context.ChangeTracker.Clear();
        Assert.Null((await db.Context.SalesInvoices.FirstAsync(i => i.Key == invoice.Key)).ShipmentReleaseKey);
    }

    [Fact]
    public async Task Generic_post_discards_the_release_link_sent_by_the_client()
    {
        var entity = new SalesInvoice { CardCode = "C1", ShipmentReleaseKey = Guid.NewGuid() };
        // Serviços nulos: a chamada de criação estoura (capturada pelo controller em 500) DEPOIS da limpeza,
        // que é o que se prova aqui; a criação real é coberta pelos testes do Emitir remessa.
        var controller = new SalesInvoicesController(null!, null!, null!, null!);

        var result = await controller.Post(entity);

        Assert.IsType<ObjectResult>(result);
        Assert.Null(entity.ShipmentReleaseKey);
    }
}
