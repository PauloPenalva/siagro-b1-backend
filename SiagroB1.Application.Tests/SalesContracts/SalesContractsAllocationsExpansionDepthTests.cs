using System.Reflection;
using Microsoft.AspNetCore.OData.Query;
using SiagroB1.Web.Controllers;

namespace SiagroB1.Application.Tests.SalesContracts;

/// <summary>
/// GAC-1216: a coluna "Carga" de Entregas de Contratos de Venda lê
/// <c>SalesInvoiceItem/SalesInvoice/ShipmentLoad</c> — três níveis de $expand. O padrão do OData é
/// dois, e com ele a tela inteira responde 400 ("$expand path which is too deep").
/// </summary>
public class SalesContractsAllocationsExpansionDepthTests
{
    [Fact]
    public void Every_get_allows_expanding_down_to_the_load_of_the_invoice()
    {
        var gets = typeof(SalesContractsAllocationsController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.Name == "Get")
            .ToList();

        Assert.NotEmpty(gets);
        Assert.All(gets, m => Assert.True(m.GetCustomAttribute<EnableQueryAttribute>()!.MaxExpansionDepth >= 3));
    }
}
