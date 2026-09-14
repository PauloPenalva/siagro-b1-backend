using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SiagroB1.Infra;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Tests.Support;

public static class TestDb
{
    /// <summary>
    /// Creates a UnitOfWork backed by an isolated EF Core InMemory database.
    /// Transactions are no-ops in the InMemory provider, so the
    /// TransactionIgnoredWarning is suppressed.
    /// </summary>
    public static UnitOfWork CreateUnitOfWork()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new UnitOfWork(new AppDbContext(options));
    }

    /// <summary>
    /// Mesma base InMemory, com um <see cref="IInterceptor"/> plugado — usado para simular no
    /// <c>SaveChanges</c> falhas que o provider InMemory não reproduz sozinho (ex.: violação de
    /// índice único), já que ele não aplica constraints de banco real.
    /// </summary>
    public static UnitOfWork CreateUnitOfWork(IInterceptor interceptor)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .AddInterceptors(interceptor)
            .Options;

        return new UnitOfWork(new AppDbContext(options));
    }
}
