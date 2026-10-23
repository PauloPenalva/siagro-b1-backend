using System.Reflection;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using SiagroB1.Migrations.CommonContext;

namespace SiagroB1.Application.Tests.Mdfe;

/// <summary>
/// Lê as operações da migration <see cref="AddTmsMenuModule"/> sem tocar no banco: o módulo TMS nasce
/// no menu e o CT-e e os cadastros de transporte mudam para ele só por UpdateData (ROLE_MENUS segue a
/// chave do item, então mover não pode apagar permissão).
/// </summary>
public class TmsMenuMigrationTests
{
    private static IReadOnlyList<MigrationOperation> Operations(string method)
    {
        var builder = new MigrationBuilder(activeProvider: null);
        typeof(AddTmsMenuModule)
            .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(new AddTmsMenuModule(), [builder]);
        return builder.Operations;
    }

    private static object? Value(InsertDataOperation op, string column) =>
        op.Values[0, Array.IndexOf(op.Columns, column)];

    [Fact]
    public void Creates_tms_module_with_folder_blank_icon()
    {
        var tms = Operations("Up").OfType<InsertDataOperation>()
            .Single(o => o.Table == "MENU_ITEMS" && (string)Value(o, "Key")! == "tms");

        Assert.Equal("TMS", Value(tms, "Title"));
        Assert.Equal("sap-icon://folder-blank", Value(tms, "Icon"));
        Assert.Null(Value(tms, "ParentKey"));
        // Veículos/Motoristas servem a balança no SAPB1: o módulo não pode sumir nesse modo.
        Assert.Equal(false, Value(tms, "StandaloneOnly"));
    }

    [Fact]
    public void Moved_items_keep_their_standalone_only_flag()
    {
        // A flag de cada item não é tocada: UpdateData só muda ParentKey e Order.
        var moved = Operations("Up").OfType<UpdateDataOperation>().ToList();

        Assert.NotEmpty(moved);
        Assert.All(moved, o => Assert.Equal(["ParentKey", "Order"], o.Columns));
        Assert.All(Operations("Down").OfType<UpdateDataOperation>(),
            o => Assert.Equal(["ParentKey", "Order"], o.Columns));
    }

    [Fact]
    public void Moves_cte_and_transport_master_data_to_tms_without_touching_role_menus()
    {
        var ops = Operations("Up");

        var moved = ops.OfType<UpdateDataOperation>().ToList();
        Assert.All(moved, o => Assert.Equal("MENU_ITEMS", o.Table));
        Assert.Equal(
            ["motoristas", "transportDocuments", "truckCombinations", "veiculos"],
            moved.Select(o => (string)o.KeyValues[0, 0]!).Order().ToArray());
        Assert.All(moved, o => Assert.Equal("tms", o.Values[0, Array.IndexOf(o.Columns, "ParentKey")]));
        Assert.Empty(ops.OfType<DeleteDataOperation>());

        var manifests = ops.OfType<InsertDataOperation>()
            .Single(o => o.Table == "MENU_ITEMS" && (string)Value(o, "Key")! == "transportManifests");
        Assert.Equal("Manifestos (MDF-e)", Value(manifests, "Title"));
        Assert.Equal("tms", Value(manifests, "ParentKey"));
        Assert.Equal(true, Value(manifests, "StandaloneOnly"));
    }

    [Fact]
    public void Down_restores_previous_parents_and_orders()
    {
        var restored = Operations("Down").OfType<UpdateDataOperation>()
            .ToDictionary(
                o => (string)o.KeyValues[0, 0]!,
                o => (Parent: (string)o.Values[0, Array.IndexOf(o.Columns, "ParentKey")]!,
                      Order: (int)o.Values[0, Array.IndexOf(o.Columns, "Order")]!));

        Assert.Equal(("sales", 16), restored["transportDocuments"]);
        Assert.Equal(("registers", 4), restored["truckCombinations"]);
        Assert.Equal(("registers", 4), restored["veiculos"]);
        Assert.Equal(("registers", 3), restored["motoristas"]);
    }
}
