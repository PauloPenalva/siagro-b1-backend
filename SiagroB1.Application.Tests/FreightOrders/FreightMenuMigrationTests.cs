using System.Reflection;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using SiagroB1.Migrations.CommonContext;

namespace SiagroB1.Application.Tests.FreightOrders;

/// <summary>Lê as operações da migration <see cref="AddFreightOrderMenus"/> sem tocar no banco.</summary>
public class FreightMenuMigrationTests
{
    private static IReadOnlyList<MigrationOperation> Operations(string method)
    {
        var builder = new MigrationBuilder(activeProvider: null);
        typeof(AddFreightOrderMenus)
            .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(new AddFreightOrderMenus(), [builder]);
        return builder.Operations;
    }

    private static object? Value(InsertDataOperation op, string column) =>
        op.Values[0, Array.IndexOf(op.Columns, column)];

    [Theory]
    [InlineData("freightOrders", "Ordens de Frete", 6)]
    [InlineData("freightInvoices", "Faturas de Frete", 7)]
    public void Creates_standalone_only_items_under_tms(string key, string title, int order)
    {
        var item = Operations("Up").OfType<InsertDataOperation>()
            .Single(o => o.Table == "MENU_ITEMS" && (string)Value(o, "Key")! == key);

        Assert.Equal(title, Value(item, "Title"));
        Assert.Equal("tms", Value(item, "ParentKey"));
        Assert.Equal(order, Value(item, "Order"));
        Assert.Equal(true, Value(item, "StandaloneOnly"));
    }

    [Fact]
    public void Grants_admin_and_copies_roles_that_see_cte()
    {
        var ops = Operations("Up");
        var roleMenus = ops.OfType<InsertDataOperation>().Single(o => o.Table == "ROLE_MENUS");
        Assert.Equal(2, roleMenus.Values.GetLength(0));
        Assert.All(Enumerable.Range(0, 2), i => Assert.Equal("ADMIN", roleMenus.Values[i, 1]));
        Assert.Equal(2, ops.OfType<SqlOperation>().Count(o => o.Sql.Contains("'transportDocuments'")));
    }

    [Fact]
    public void Down_deletes_role_menus_before_menu_items()
    {
        var sql = Operations("Down").OfType<SqlOperation>().Select(o => o.Sql).ToList();
        Assert.Equal(2, sql.Count);
        Assert.Contains("ROLE_MENUS", sql[0]);
        Assert.Contains("MENU_ITEMS", sql[1]);
    }
}
