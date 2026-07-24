using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Tests.Infra;

/// <summary>
/// Mapeamento relacional dos comentários de contrato. Protege o nome da tabela, das colunas e —
/// principalmente — a navegação <c>CommentEntries</c>: ela não pode se chamar <c>Comments</c>,
/// que já é o escalar de observação do cabeçalho.
/// </summary>
public class ContractCommentModelTests
{
    /// <summary>
    /// Usa o provider SqlServer só para materializar o modelo relacional; sem conexão.
    /// </summary>
    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=model-inspection-only;Database=none").Options;
        return new AppDbContext(options);
    }

    [Fact]
    public void PurchaseContractComment_IsMappedToExpectedTableAndColumns()
    {
        using var context = CreateContext();

        var entityType = context.Model.FindEntityType(typeof(PurchaseContractComment));
        Assert.NotNull(entityType);
        Assert.Equal("PURCHASE_CONTRACTS_COMMENTS", entityType.GetTableName());

        var props = entityType.GetProperties().Select(p => p.Name).ToHashSet();
        Assert.Contains("Key", props);
        Assert.Contains("PurchaseContractKey", props);
        Assert.Contains("CommentedAt", props);
        Assert.Contains("CommentedBy", props);
        Assert.Contains("CommentText", props);
    }

    [Fact]
    public void SalesContractComment_IsMappedToExpectedTableAndColumns()
    {
        using var context = CreateContext();

        var entityType = context.Model.FindEntityType(typeof(SalesContractComment));
        Assert.NotNull(entityType);
        Assert.Equal("SALES_CONTRACTS_COMMENTS", entityType.GetTableName());

        var props = entityType.GetProperties().Select(p => p.Name).ToHashSet();
        Assert.Contains("Key", props);
        Assert.Contains("SalesContractKey", props);
        Assert.Contains("CommentedAt", props);
        Assert.Contains("CommentedBy", props);
        Assert.Contains("CommentText", props);
    }

    [Fact]
    public void Contracts_ExposeCommentEntriesNavigationAndKeepScalarComments()
    {
        using var context = CreateContext();

        foreach (var contractType in new[] { typeof(PurchaseContract), typeof(SalesContract) })
        {
            var entityType = context.Model.FindEntityType(contractType);
            Assert.NotNull(entityType);

            Assert.NotNull(entityType.FindNavigation("CommentEntries"));
            // A observação do cabeçalho continua escalar — é o motivo do nome CommentEntries.
            Assert.NotNull(entityType.FindProperty("Comments"));
        }
    }
}
