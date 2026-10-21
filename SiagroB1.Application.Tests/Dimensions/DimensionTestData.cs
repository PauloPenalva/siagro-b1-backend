using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Dimensions;

/// <summary>
/// O provider InMemory não aplica HasData: os testes semeiam as 5 dimensões fixas aqui,
/// com os mesmos valores iniciais da migration.
/// </summary>
public static class DimensionTestData
{
    public static async Task SeedDimensions(IUnitOfWork db)
    {
        for (var number = 1; number <= 5; number++)
        {
            db.Context.AccountingDimensions.Add(new AccountingDimension
            {
                Number = number,
                Name = $"Dimensão {number}",
                Active = number == 1,
                Role = DimensionRole.None,
            });
        }

        await db.SaveChangesAsync();
    }
}
