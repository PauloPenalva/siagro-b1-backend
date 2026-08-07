using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseInvoices;

/// <summary>
/// Registro do documento de entrada e a trava de chave de NF-e duplicada.
///
/// A trava tem duas camadas: o índice único filtrado no banco é a rede de segurança, e a checagem
/// do serviço existe para a mensagem sair legível em pt-BR. Cancelar LIBERA a chave — relançar a
/// mesma nota depois de cancelar é caminho legítimo.
/// </summary>
public class PurchaseInvoicesCreateTests
{
    private const string Chave = "35260800000000000000550010000000011000000017";

    private static PurchaseInvoicesCreateService Service(UnitOfWork db) =>
        new(db,
            new FakeBusinessPartnerService(
                names: new Dictionary<string, string> { ["F0001"] = "PRODUTOR TESTE" }),
            new FakeItemService(
                names: new Dictionary<string, string> { ["SOJA"] = "SOJA EM GRAOS" }));

    private static PurchaseInvoice NewInvoice(string? chave = Chave)
    {
        var invoice = new PurchaseInvoice { CardCode = "F0001", ChaveNFe = chave };
        invoice.AddItem(new PurchaseInvoiceItem { ItemCode = "SOJA", Quantity = 10m, UnitPrice = 1m });
        return invoice;
    }

    [Fact]
    public async Task Document_without_items_is_refused()
    {
        var db = TestDb.CreateUnitOfWork();

        var empty = new PurchaseInvoice { CardCode = "F0001" };

        await Assert.ThrowsAsync<DefaultException>(() => Service(db).ExecuteAsync(empty, "tester"));
    }

    [Fact]
    public async Task Document_is_born_pending_and_stamped()
    {
        var db = TestDb.CreateUnitOfWork();

        var invoice = NewInvoice();
        await Service(db).ExecuteAsync(invoice, "tester");

        Assert.Equal(InvoiceStatus.Pending, invoice.InvoiceStatus);
        Assert.Equal("tester", invoice.CreatedBy);
        Assert.NotNull(invoice.CreatedAt);
        Assert.Single(await db.Context.PurchaseInvoices.ToListAsync());
    }

    [Fact]
    public async Task Card_name_is_denormalized_from_the_partner_registry()
    {
        var db = TestDb.CreateUnitOfWork();

        var invoice = NewInvoice();
        await Service(db).ExecuteAsync(invoice, "tester");

        // Denormalizado para a grade e os relatórios não dependerem do cadastro, que em modo
        // SAPB1 nem é local.
        Assert.Equal("PRODUTOR TESTE", invoice.CardName);
    }

    [Fact]
    public async Task Card_name_sent_by_the_client_is_not_overwritten()
    {
        var db = TestDb.CreateUnitOfWork();

        var invoice = NewInvoice();
        invoice.CardName = "NOME DO XML";
        await Service(db).ExecuteAsync(invoice, "tester");

        // O nome lido do XML do emitente vale mais que o do cadastro: é o que consta na nota.
        Assert.Equal("NOME DO XML", invoice.CardName);
    }

    [Fact]
    public async Task Blank_card_name_is_resolved_from_the_partner_registry()
    {
        var db = TestDb.CreateUnitOfWork();

        var invoice = NewInvoice();
        invoice.CardName = "";
        await Service(db).ExecuteAsync(invoice, "tester");

        // VAZIO é o que a TELA manda, e é diferente de null: o value help copia a descrição com
        // group ID null de propósito (quem manda no campo desnormalizado é o servidor), e o
        // create() do UI5 precisa declarar a propriedade como "" para a primeira digitação não
        // abrir "Must not change a property before it has been read". Tratar só null deixava o
        // nome do emitente EM BRANCO na tela e no banco.
        Assert.Equal("PRODUTOR TESTE", invoice.CardName);
    }

    [Fact]
    public async Task Duplicated_access_key_is_refused()
    {
        var db = TestDb.CreateUnitOfWork();

        await Service(db).ExecuteAsync(NewInvoice(), "tester");

        await Assert.ThrowsAsync<DefaultException>(
            () => Service(db).ExecuteAsync(NewInvoice(), "tester"));
    }

    [Fact]
    public async Task Cancelled_document_releases_the_access_key()
    {
        var db = TestDb.CreateUnitOfWork();

        var first = NewInvoice();
        await Service(db).ExecuteAsync(first, "tester");

        first.InvoiceStatus = InvoiceStatus.Cancelled;
        await db.SaveChangesAsync();

        // Relançar a mesma NF depois de cancelar é caminho legítimo — por isso o índice único é
        // FILTRADO por status.
        await Service(db).ExecuteAsync(NewInvoice(), "tester");

        Assert.Equal(2, await db.Context.PurchaseInvoices.CountAsync());
    }

    [Fact]
    public async Task Documents_without_access_key_do_not_collide()
    {
        var db = TestDb.CreateUnitOfWork();

        // Documento digitado à mão pode não ter chave: a trava não pode transformar isso em
        // "só um documento sem chave no sistema inteiro".
        await Service(db).ExecuteAsync(NewInvoice(chave: null), "tester");
        await Service(db).ExecuteAsync(NewInvoice(chave: null), "tester");

        Assert.Equal(2, await db.Context.PurchaseInvoices.CountAsync());
    }

    [Fact]
    public async Task Blank_access_key_does_not_collide_either()
    {
        var db = TestDb.CreateUnitOfWork();

        // String vazia é o que o diálogo manda quando o operador apaga o campo, e ela colide
        // consigo mesma numa query de igualdade se não for tratada como ausente.
        await Service(db).ExecuteAsync(NewInvoice(chave: ""), "tester");
        await Service(db).ExecuteAsync(NewInvoice(chave: ""), "tester");

        Assert.Equal(2, await db.Context.PurchaseInvoices.CountAsync());
    }

    [Fact]
    public async Task Item_name_is_resolved_from_the_registry_when_the_line_arrives_blank()
    {
        var db = TestDb.CreateUnitOfWork();

        // É o caso do value help: a descrição é copiada para a tela com group ID null e NÃO entra
        // no deep-insert, então a linha chega com o produto certo e o nome vazio.
        var invoice = new PurchaseInvoice { CardCode = "F0001", ChaveNFe = null };
        invoice.AddItem(new PurchaseInvoiceItem
        {
            ItemCode = "SOJA", ItemName = "", Quantity = 10m, UnitPrice = 1m,
        });

        await Service(db).ExecuteAsync(invoice, "tester");

        Assert.Equal("SOJA EM GRAOS", invoice.Items.Single().ItemName);
    }

    [Fact]
    public async Task Item_name_read_from_the_xml_wins_over_the_registry()
    {
        var db = TestDb.CreateUnitOfWork();

        // A descrição do XML é a que CONSTA NA NOTA do fornecedor — sobrescrevê-la pelo cadastro
        // descaracterizaria o documento fiscal de terceiro.
        var invoice = new PurchaseInvoice { CardCode = "F0001", ChaveNFe = null };
        invoice.AddItem(new PurchaseInvoiceItem
        {
            ItemCode = "SOJA", ItemName = "SOJA GRAO A GRANEL", Quantity = 10m, UnitPrice = 1m,
        });

        await Service(db).ExecuteAsync(invoice, "tester");

        Assert.Equal("SOJA GRAO A GRANEL", invoice.Items.Single().ItemName);
    }

    [Fact]
    public async Task Item_code_outside_the_registry_keeps_the_line_without_a_name()
    {
        var db = TestDb.CreateUnitOfWork();

        // Insumo/serviço, ou código do emitente que não existe no cadastro local: a linha grava
        // assim mesmo. O documento de entrada é de CONTROLE e não pode barrar por isso.
        var invoice = new PurchaseInvoice { CardCode = "F0001", ChaveNFe = null };
        invoice.AddItem(new PurchaseInvoiceItem
        {
            ItemCode = "DESCONHECIDO", ItemName = "", Quantity = 1m, UnitPrice = 1m,
        });

        await Service(db).ExecuteAsync(invoice, "tester");

        Assert.Null(invoice.Items.Single().ItemName);
    }
}
