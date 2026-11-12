using System.Reflection;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Domain.Dtos;

namespace SiagroB1.Application.Tests.SalesContracts;

/// <summary>
/// GAC-1216: a aba "Entregas" do contrato de venda mostra a carga de cada linha. A carga é a do
/// documento de saída (<c>SALES_INVOICES.ShipmentLoadKey</c>); documento sem carga (fluxo legado,
/// migração) fica em branco, por isso o LEFT JOIN. A constante é privada e o projeto não tem
/// InternalsVisibleTo, então é lida por reflexão.
/// </summary>
public class SalesContractsGetAllocationsByContractSqlTests
{
    private static string ReadSql() =>
        (string)typeof(SalesContractsGetAllocationsByContractService)
            .GetField("SQL", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;

    [Fact]
    public void Query_returns_the_load_code_of_the_invoice()
    {
        var sql = ReadSql();

        Assert.Contains("L.Code AS LoadCode", sql);
        Assert.Contains("LEFT JOIN SHIPMENT_LOADS L", sql);
        Assert.Contains("L.[Key] = INV.ShipmentLoadKey", sql);
    }

    [Fact]
    public void Dto_exposes_the_load_code()
    {
        Assert.NotNull(typeof(SalesContractsGetAllocationsByContractDto).GetProperty("LoadCode"));
        Assert.NotNull(typeof(PurchaseContractAllocationsByContractDto).GetProperty("LoadCode"));
    }
}
