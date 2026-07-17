using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.StorageTransactions;

public class StorageTransactionsGetServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();
    private readonly TestLogger<StorageTransactionsGetService> _logger = new();

    [Fact]
    public async Task GetByIdAsync_NotFound_ThrowsNotFoundException()
    {
        // Não pode ser mascarada como DefaultException pelo catch genérico —
        // o controller depende do tipo para retornar 404
        var service = new StorageTransactionsGetService(_db, _logger);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.GetByIdAsync(Guid.NewGuid()));
    }
}
