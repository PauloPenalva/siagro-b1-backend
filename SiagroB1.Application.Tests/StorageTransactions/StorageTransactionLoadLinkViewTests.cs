using Dapper;
using Microsoft.Data.SqlClient;
using SiagroB1.Migrations.AppContext;

namespace SiagroB1.Application.Tests.StorageTransactions;

/// <summary>
/// GAC-1216: a view <c>STORAGE_TRANSACTIONS_LOAD_LINKS</c> diz em qual carga cada romaneio viajou.
/// É SQL puro (o provider InMemory não tem view), então roda num banco descartável do LocalDB só
/// com as colunas que a view lê — <see cref="StorageTransactionLoadLinkModelTests"/> garante que
/// essas colunas existem no modelo real.
/// </summary>
public sealed class StorageTransactionLoadLinkViewFixture : IDisposable
{
    private const string Server = @"Server=(localdb)\MSSQLLocalDB;Integrated Security=true;TrustServerCertificate=true;Connect Timeout=30";
    private readonly string _database = "SIAGRO_TEST_GAC1216_" + Guid.NewGuid().ToString("N");

    public StorageTransactionLoadLinkViewFixture()
    {
        try
        {
            using var master = new SqlConnection(Server);
            master.Execute($"CREATE DATABASE [{_database}]");
        }
        catch (SqlException ex)
        {
            throw new InvalidOperationException(
                "Estes testes exigem o SQL Server LocalDB (instância MSSQLLocalDB) — a view é SQL puro.", ex);
        }

        using var db = Open();
        db.Execute("""
            CREATE TABLE SHIPMENT_LOADS ([Key] UNIQUEIDENTIFIER PRIMARY KEY, Code VARCHAR(20));
            CREATE TABLE STORAGE_TRANSACTIONS (
                [Key] UNIQUEIDENTIFIER PRIMARY KEY,
                ShipmentLoadKey UNIQUEIDENTIFIER NULL,
                RefusedFromShipmentLoadKey UNIQUEIDENTIFIER NULL,
                ShipmentLoadTransshipmentKey UNIQUEIDENTIFIER NULL,
                ReplacedByShippingReleaseChangeKey UNIQUEIDENTIFIER NULL,
                ShippingReleaseChangeKey UNIQUEIDENTIFIER NULL,
                DischargeSalesInvoiceItemKey UNIQUEIDENTIFIER NULL);
            CREATE TABLE SHIPPING_TRANSACTIONS (
                [Key] UNIQUEIDENTIFIER PRIMARY KEY,
                RowId INT IDENTITY,
                PurchaseStorageTransactionKey UNIQUEIDENTIFIER NULL,
                SalesStorageTransactionKey UNIQUEIDENTIFIER NOT NULL);
            CREATE TABLE SHIPMENT_LOAD_TRANSSHIPMENTS ([Key] UNIQUEIDENTIFIER PRIMARY KEY, ShipmentLoadKey UNIQUEIDENTIFIER NOT NULL);
            CREATE TABLE SHIPPING_RELEASE_CHANGES ([Key] UNIQUEIDENTIFIER PRIMARY KEY, ShipmentLoadKey UNIQUEIDENTIFIER NOT NULL);
            CREATE TABLE SALES_INVOICES ([Key] UNIQUEIDENTIFIER PRIMARY KEY, ShipmentLoadKey UNIQUEIDENTIFIER NULL);
            CREATE TABLE SALES_INVOICES_ITEMS ([Key] UNIQUEIDENTIFIER PRIMARY KEY, SalesInvoiceKey UNIQUEIDENTIFIER NOT NULL);
            """);
        db.Execute(AddStorageTransactionsLoadLinksView.ViewSql);
    }

    public SqlConnection Open()
    {
        var connection = new SqlConnection($"{Server};Database={_database}");
        connection.Open();
        return connection;
    }

    public void Dispose()
    {
        SqlConnection.ClearAllPools();
        using var master = new SqlConnection(Server);
        master.Execute($"ALTER DATABASE [{_database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_database}]");
    }
}

public class StorageTransactionLoadLinkViewTests(StorageTransactionLoadLinkViewFixture fixture)
    : IClassFixture<StorageTransactionLoadLinkViewFixture>
{
    private static Guid Load(SqlConnection db, string code)
    {
        var key = Guid.NewGuid();
        db.Execute("INSERT INTO SHIPMENT_LOADS ([Key], Code) VALUES (@key, @code)", new { key, code });
        return key;
    }

    /// <summary>Insere um romaneio; <paramref name="column"/> é o vínculo (ou nenhum) que ele carrega.</summary>
    private static Guid Transaction(SqlConnection db, string? column = null, Guid? value = null)
    {
        var key = Guid.NewGuid();
        db.Execute(column is null
            ? "INSERT INTO STORAGE_TRANSACTIONS ([Key]) VALUES (@key)"
            : $"INSERT INTO STORAGE_TRANSACTIONS ([Key], {column}) VALUES (@key, @value)", new { key, value });
        return key;
    }

    private static void Shipping(SqlConnection db, Guid purchase, Guid sales) =>
        db.Execute(
            "INSERT INTO SHIPPING_TRANSACTIONS ([Key], PurchaseStorageTransactionKey, SalesStorageTransactionKey) VALUES (NEWID(), @purchase, @sales)",
            new { purchase, sales });

    private static List<string?> LoadCodes(SqlConnection db, Guid transaction) =>
        db.Query<string?>(
            "SELECT LoadCode FROM STORAGE_TRANSACTIONS_LOAD_LINKS WHERE StorageTransactionKey = @transaction",
            new { transaction }).ToList();

    [Fact]
    public void Transaction_assembled_in_a_load_points_to_it()
    {
        using var db = fixture.Open();
        var transaction = Transaction(db, "ShipmentLoadKey", Load(db, "CG-MONTADO"));

        Assert.Equal(["CG-MONTADO"], LoadCodes(db, transaction));
    }

    [Fact]
    public void Refusal_return_points_to_the_load_it_came_back_from()
    {
        using var db = fixture.Open();
        var transaction = Transaction(db, "RefusedFromShipmentLoadKey", Load(db, "CG-RECUSA"));

        Assert.Equal(["CG-RECUSA"], LoadCodes(db, transaction));
    }

    [Fact]
    public void Transshipment_entry_points_to_the_load_of_the_transshipment()
    {
        using var db = fixture.Open();
        var transshipment = Guid.NewGuid();
        db.Execute("INSERT INTO SHIPMENT_LOAD_TRANSSHIPMENTS ([Key], ShipmentLoadKey) VALUES (@transshipment, @load)",
            new { transshipment, load = Load(db, "CG-TRANSBORDO") });
        var transaction = Transaction(db, "ShipmentLoadTransshipmentKey", transshipment);

        Assert.Equal(["CG-TRANSBORDO"], LoadCodes(db, transaction));
    }

    [Fact]
    public void Purchase_leg_points_to_the_load_of_its_sales_leg()
    {
        using var db = fixture.Open();
        var purchase = Transaction(db);
        Shipping(db, purchase, Transaction(db, "ShipmentLoadKey", Load(db, "CG-EXPEDICAO")));

        Assert.Equal(["CG-EXPEDICAO"], LoadCodes(db, purchase));
    }

    [Theory]
    [InlineData("ReplacedByShippingReleaseChangeKey")]
    [InlineData("ShippingReleaseChangeKey")]
    public void Purchase_leg_of_a_release_change_points_to_the_load_of_the_change(string column)
    {
        using var db = fixture.Open();
        var change = Guid.NewGuid();
        db.Execute("INSERT INTO SHIPPING_RELEASE_CHANGES ([Key], ShipmentLoadKey) VALUES (@change, @load)",
            new { change, load = Load(db, "CG-TROCA") });
        var purchase = Transaction(db, column, change);
        // A troca tira a perna de venda original da carga: o elo que sobra é o documento da troca.
        Shipping(db, purchase, Transaction(db));

        Assert.Equal(["CG-TROCA"], LoadCodes(db, purchase));
    }

    [Fact]
    public void Discharge_adjustment_points_to_the_load_of_the_reconciled_invoice()
    {
        using var db = fixture.Open();
        var invoice = Guid.NewGuid();
        var item = Guid.NewGuid();
        db.Execute("INSERT INTO SALES_INVOICES ([Key], ShipmentLoadKey) VALUES (@invoice, @load)",
            new { invoice, load = Load(db, "CG-DESCARGA") });
        db.Execute("INSERT INTO SALES_INVOICES_ITEMS ([Key], SalesInvoiceKey) VALUES (@item, @invoice)", new { item, invoice });
        var transaction = Transaction(db, "DischargeSalesInvoiceItemKey", item);

        Assert.Equal(["CG-DESCARGA"], LoadCodes(db, transaction));
    }

    [Fact]
    public void Transaction_without_any_link_has_no_row()
    {
        using var db = fixture.Open();
        var transaction = Transaction(db);

        Assert.Empty(LoadCodes(db, transaction));
    }

    /// <summary>
    /// A navegação do romaneio para a view é 1:1 — duas linhas para o mesmo romaneio duplicariam a
    /// linha dele nas listas.
    /// </summary>
    [Fact]
    public void Purchase_leg_with_two_sales_legs_yields_a_single_row()
    {
        using var db = fixture.Open();
        var purchase = Transaction(db);
        Shipping(db, purchase, Transaction(db, "ShipmentLoadKey", Load(db, "CG-PRIMEIRA")));
        Shipping(db, purchase, Transaction(db, "ShipmentLoadKey", Load(db, "CG-ULTIMA")));

        Assert.Equal(["CG-ULTIMA"], LoadCodes(db, purchase));
    }
}
