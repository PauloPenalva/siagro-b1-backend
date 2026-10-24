using System.Reflection;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.FreightInvoices;
using SiagroB1.Domain.Enums;
using SiagroB1.Reports.Services;

namespace SiagroB1.Application.Tests.FreightInvoices;

public class FreightInvoiceUniqueViolationTests
{
    /// <summary>SqlException não tem construtor público: monta pela reflexão, com o número de erro pedido.</summary>
    private static SqlException Sql(int number)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        var error = (SqlError)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(SqlError));
        typeof(SqlError).GetField("_number", flags)!.SetValue(error, number);
        var errors = (SqlErrorCollection)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(SqlErrorCollection));
        var listField = typeof(SqlErrorCollection).GetField("_errors", flags)!;
        listField.SetValue(errors, new List<object> { error });
        var ex = (SqlException)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(SqlException));
        typeof(SqlException).GetField("_errors", flags)!.SetValue(ex, errors);
        return ex;
    }

    [Theory]
    [InlineData(2601)]
    [InlineData(2627)]
    public void Detects_unique_violation_in_inner_chain(int number)
    {
        var ex = new DbUpdateException("x", new InvalidOperationException("y", Sql(number)));
        Assert.True(FreightInvoicesSaveService.IsUniqueViolation(ex));
    }

    [Fact]
    public void Ignores_other_errors()
    {
        Assert.False(FreightInvoicesSaveService.IsUniqueViolation(new DbUpdateException("x", Sql(547))));
        Assert.False(FreightInvoicesSaveService.IsUniqueViolation(new DbUpdateException("x")));
    }

    [Fact]
    public void MethodLabel_aggregation_is_freight_invoice()
        => Assert.Equal("Fatura de frete", FinancialSettlementsReportService.MethodLabel(FinancialSettlementOrigin.Aggregation));
}
