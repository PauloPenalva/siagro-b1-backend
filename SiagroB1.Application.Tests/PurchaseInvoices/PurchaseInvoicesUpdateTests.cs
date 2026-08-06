using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseInvoices;

/// <summary>
/// Atualização do documento de entrada — cabeçalho E LINHAS.
///
/// É a correção da regressão que motivou a rotina nova: <c>CustomerReturnsUpdateService</c>
/// atualizava só o cabeçalho e nunca tocava <c>existing.Items</c>, e com o Detail read-only não
/// havia caminho nenhum para amarrar uma linha depois de gravar.
/// </summary>
public class PurchaseInvoicesUpdateTests
{
    private static async Task<(UnitOfWork db, PurchaseInvoice saved)> SeedAsync()
    {
        var db = TestDb.CreateUnitOfWork();

        var invoice = new PurchaseInvoice
        {
            Key = Guid.NewGuid(),
            CardCode = "F0001",
            TaxDocumentNumber = "1",
        };
        invoice.AddItem(new PurchaseInvoiceItem
        {
            Key = Guid.NewGuid(), ItemCode = "SOJA", Quantity = 10m, UnitPrice = 1m,
        });

        db.Context.PurchaseInvoices.Add(invoice);
        await db.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        return (db, invoice);
    }

    private static FakeBusinessPartnerService Partners() =>
        new(names: new Dictionary<string, string>
        {
            ["F0001"] = "PRODUTOR TESTE",
            ["F0002"] = "OUTRO PARCEIRO",
        });

    private static PurchaseInvoice Incoming(Guid? lineKey, Guid? originKey = null)
    {
        var incoming = new PurchaseInvoice { CardCode = "F0001" };
        incoming.AddItem(new PurchaseInvoiceItem
        {
            Key = lineKey,
            ItemCode = "SOJA",
            Quantity = 10m,
            UnitPrice = 1m,
            SalesInvoiceItemKey = originKey,
        });
        return incoming;
    }

    [Fact]
    public async Task Header_fields_are_updated()
    {
        var (db, saved) = await SeedAsync();

        var incoming = Incoming(saved.Items.First().Key);
        incoming.TaxDocumentNumber = "999";
        incoming.TaxDocumentSeries = "2";
        incoming.TotalDocumentValue = 1234.56m;

        await new PurchaseInvoicesUpdateService(db, Partners()).ExecuteAsync(saved.Key, incoming, "tester");

        var reloaded = await db.Context.PurchaseInvoices.AsNoTracking()
            .FirstAsync(x => x.Key == saved.Key);

        Assert.Equal("999", reloaded.TaxDocumentNumber);
        Assert.Equal("2", reloaded.TaxDocumentSeries);
        Assert.Equal(1234.56m, reloaded.TotalDocumentValue);
        Assert.Equal("tester", reloaded.UpdatedBy);
    }

    [Fact]
    public async Task Changing_the_issuer_is_persisted()
    {
        var (db, saved) = await SeedAsync();

        var incoming = Incoming(saved.Items.First().Key);
        incoming.CardCode = "F0002";
        incoming.CardName = "OUTRO PARCEIRO";

        await new PurchaseInvoicesUpdateService(db, Partners()).ExecuteAsync(saved.Key, incoming, "tester");

        var reloaded = await db.Context.PurchaseInvoices.AsNoTracking()
            .FirstAsync(x => x.Key == saved.Key);

        // O emitente é editável enquanto o documento está pendente, e trocá-lo era descartado em
        // SILÊNCIO: o serviço copiava todo o resto do cabeçalho menos estes dois campos. A tela
        // gravava sem erro e recarregava com o emitente antigo.
        Assert.Equal("F0002", reloaded.CardCode);
        Assert.Equal("OUTRO PARCEIRO", reloaded.CardName);
    }

    [Fact]
    public async Task Name_sent_by_the_client_survives_when_the_issuer_does_not_change()
    {
        var (db, saved) = await SeedAsync();

        var incoming = Incoming(saved.Items.First().Key);
        incoming.CardName = "NOME COMO CONSTA NA NOTA";

        await new PurchaseInvoicesUpdateService(db, Partners()).ExecuteAsync(
            saved.Key, incoming, "tester");

        var reloaded = await db.Context.PurchaseInvoices.AsNoTracking()
            .FirstAsync(x => x.Key == saved.Key);

        // Sem troca de emitente o cadastro NÃO manda: num documento fiscal de terceiro vale o nome
        // que consta na nota, que é o que a importação do XML gravou.
        Assert.Equal("NOME COMO CONSTA NA NOTA", reloaded.CardName);
    }

    [Fact]
    public async Task Changing_the_issuer_refreshes_the_denormalized_name()
    {
        var (db, saved) = await SeedAsync();

        var incoming = Incoming(saved.Items.First().Key);
        incoming.CardCode = "F0002";
        incoming.CardName = "PRODUTOR TESTE";   // nome do emitente ANTERIOR, que a tela reenvia

        await new PurchaseInvoicesUpdateService(db, Partners()).ExecuteAsync(
            saved.Key, incoming, "tester");

        var reloaded = await db.Context.PurchaseInvoices.AsNoTracking()
            .FirstAsync(x => x.Key == saved.Key);

        // O value help copia a descrição com group ID null de propósito, então o PATCH chega com o
        // nome VELHO enquanto o código já é o novo. Sem re-resolver, o documento fica com o código
        // de um parceiro e o nome de outro — e é o nome que aparece na lista e nos relatórios.
        Assert.Equal("OUTRO PARCEIRO", reloaded.CardName);
    }

    [Fact]
    public async Task Line_binding_is_persisted()
    {
        var (db, saved) = await SeedAsync();
        var originKey = Guid.NewGuid();

        await new PurchaseInvoicesUpdateService(db, Partners())
            .ExecuteAsync(saved.Key, Incoming(saved.Items.First().Key, originKey), "tester");

        var reloaded = await db.Context.PurchaseInvoices.AsNoTracking()
            .Include(x => x.Items).FirstAsync(x => x.Key == saved.Key);

        // A amarração é o motivo de o Update existir.
        Assert.Equal(originKey, reloaded.Items.Single().SalesInvoiceItemKey);
    }

    [Fact]
    public async Task Existing_line_keeps_its_identity_when_only_the_binding_changes()
    {
        var (db, saved) = await SeedAsync();
        var lineKey = saved.Items.First().Key;

        await new PurchaseInvoicesUpdateService(db, Partners())
            .ExecuteAsync(saved.Key, Incoming(lineKey, Guid.NewGuid()), "tester");

        var reloaded = await db.Context.PurchaseInvoices.AsNoTracking()
            .Include(x => x.Items).FirstAsync(x => x.Key == saved.Key);

        // Reconciliar, e não limpar-e-recriar: a linha que só mudou de amarração continua sendo
        // a mesma linha.
        Assert.Equal(lineKey, reloaded.Items.Single().Key);
    }

    [Fact]
    public async Task New_line_is_inserted_and_removed_line_is_deleted()
    {
        var (db, saved) = await SeedAsync();

        var incoming = new PurchaseInvoice { CardCode = "F0001" };
        // A linha original NÃO vem: deve ser removida. Uma nova entra no lugar.
        incoming.AddItem(new PurchaseInvoiceItem
        {
            Key = Guid.NewGuid(), ItemCode = "MILHO", Quantity = 5m, UnitPrice = 2m,
        });

        await new PurchaseInvoicesUpdateService(db, Partners()).ExecuteAsync(saved.Key, incoming, "tester");

        var reloaded = await db.Context.PurchaseInvoices.AsNoTracking()
            .Include(x => x.Items).FirstAsync(x => x.Key == saved.Key);

        Assert.Equal("MILHO", reloaded.Items.Single().ItemCode);

        // A relação é OPCIONAL (PurchaseInvoiceKey é Guid?), então tirar da coleção sem remover
        // do DbSet deixaria a linha antiga órfã com FK nula em vez de apagá-la — invisível pelo
        // Include e visível na tabela.
        Assert.Equal(1, await db.Context.PurchaseInvoicesItems.CountAsync());
    }

    [Fact]
    public async Task Line_without_key_is_inserted()
    {
        var (db, saved) = await SeedAsync();

        var incoming = Incoming(saved.Items.First().Key);
        // Linha nova digitada na grade chega sem Key: quem a gera é o servidor.
        incoming.AddItem(new PurchaseInvoiceItem { ItemCode = "MILHO", Quantity = 7m, UnitPrice = 3m });

        await new PurchaseInvoicesUpdateService(db, Partners()).ExecuteAsync(saved.Key, incoming, "tester");

        var reloaded = await db.Context.PurchaseInvoices.AsNoTracking()
            .Include(x => x.Items).FirstAsync(x => x.Key == saved.Key);

        Assert.Equal(2, reloaded.Items.Count);
        Assert.All(reloaded.Items, i => Assert.NotNull(i.Key));
    }

    [Fact]
    public async Task Cancelled_document_cannot_be_updated()
    {
        var (db, saved) = await SeedAsync();

        var tracked = await db.Context.PurchaseInvoices.FirstAsync(x => x.Key == saved.Key);
        tracked.InvoiceStatus = InvoiceStatus.Cancelled;
        await db.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        await Assert.ThrowsAsync<DefaultException>(
            () => new PurchaseInvoicesUpdateService(db, Partners())
                .ExecuteAsync(saved.Key, Incoming(null), "tester"));
    }

    [Fact]
    public async Task Confirmed_document_cannot_be_updated()
    {
        var (db, saved) = await SeedAsync();

        var tracked = await db.Context.PurchaseInvoices.FirstAsync(x => x.Key == saved.Key);
        tracked.InvoiceStatus = InvoiceStatus.Confirmed;
        await db.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        // Confirmado tem efeito de negócio pendurado (Fase 3): o caminho é estornar antes.
        await Assert.ThrowsAsync<DefaultException>(
            () => new PurchaseInvoicesUpdateService(db, Partners())
                .ExecuteAsync(saved.Key, Incoming(null), "tester"));
    }

    [Fact]
    public async Task Patch_flow_through_get_by_id_actually_applies_the_changes()
    {
        var (db, saved) = await SeedAsync();

        // Reproduz o caminho do controller no PATCH: lê pelo GetService, muta o que voltou (é o
        // que o Delta faz) e manda para o Update.
        var fetched = await new PurchaseInvoicesGetService(db).GetByIdAsync(saved.Key);
        fetched.TaxDocumentNumber = "777";
        fetched.Items.First().SalesInvoiceItemKey = Guid.NewGuid();

        await new PurchaseInvoicesUpdateService(db, Partners()).ExecuteAsync(saved.Key, fetched, "tester");

        var reloaded = await db.Context.PurchaseInvoices.AsNoTracking()
            .Include(x => x.Items).FirstAsync(x => x.Key == saved.Key);

        // Se o GetService devolvesse entidade RASTREADA, o Update compararia o objeto consigo
        // mesmo e a linha não mudaria — sem erro nenhum.
        Assert.Equal("777", reloaded.TaxDocumentNumber);
        Assert.NotNull(reloaded.Items.Single().SalesInvoiceItemKey);
    }

    [Fact]
    public async Task Unknown_document_throws_not_found()
    {
        var db = TestDb.CreateUnitOfWork();

        await Assert.ThrowsAsync<NotFoundException>(
            () => new PurchaseInvoicesUpdateService(db, Partners())
                .ExecuteAsync(Guid.NewGuid(), Incoming(null), "tester"));
    }
}
