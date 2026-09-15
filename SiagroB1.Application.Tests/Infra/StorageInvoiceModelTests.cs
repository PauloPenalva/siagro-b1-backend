using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Tests.Infra;

public class StorageInvoiceModelTests
{
    // Usa o provider SqlServer só para materializar o modelo relacional; sem conexão.
    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=model-inspection-only;Database=none")
            .Options);

    /// <summary>
    /// O mesmo lote pode ter mais de uma fatura ativa no mesmo período: fechar de novo um
    /// período já faturado gera outra fatura com os registros que ficaram pendentes. Um
    /// índice único aqui transformaria esse fechamento em erro de banco (2601).
    /// </summary>
    [Fact]
    public void Lot_and_period_index_is_not_unique()
    {
        using var context = CreateContext();

        var index = context.Model.FindEntityType(typeof(StorageInvoice))!
            .GetIndexes()
            .Single(x => x.Properties.Select(p => p.Name)
                .SequenceEqual([nameof(StorageInvoice.StorageAddressCode), nameof(StorageInvoice.PeriodStart), nameof(StorageInvoice.PeriodEnd)]));

        Assert.False(index.IsUnique);
    }
}
