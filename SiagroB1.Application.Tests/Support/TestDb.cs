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
    public static UnitOfWork CreateUnitOfWork() => CreateUnitOfWork(Guid.NewGuid().ToString());

    /// <summary>
    /// Mesma base InMemory, mas com o nome escolhido por quem chama — para quando o teste precisa
    /// de MAIS DE UM <see cref="AppDbContext"/> sobre o mesmo banco (change trackers separados,
    /// como acontece entre escopos de DI em produção).
    /// </summary>
    public static UnitOfWork CreateUnitOfWork(string databaseName) =>
        new(new AppDbContext(Options(databaseName)));

    /// <summary>Opções da base InMemory nomeada, com o aviso de transação ignorada suprimido.</summary>
    public static DbContextOptions<AppDbContext> Options(string databaseName) =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

    /// <summary>
    /// Mesma base InMemory, com um <see cref="IInterceptor"/> plugado — usado para simular no
    /// <c>SaveChanges</c> falhas que o provider InMemory não reproduz sozinho (ex.: violação de
    /// índice único), já que ele não aplica constraints de banco real.
    /// </summary>
    public static UnitOfWork CreateUnitOfWork(IInterceptor interceptor) => CreateUnitOfWork(Guid.NewGuid().ToString(), interceptor);

    /// <summary>A base InMemory nomeada (a de um cenário já semeado), com um <see cref="IInterceptor"/> plugado.</summary>
    public static UnitOfWork CreateUnitOfWork(string databaseName, IInterceptor interceptor)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .AddInterceptors(interceptor)
            .Options;

        return new UnitOfWork(new AppDbContext(options));
    }
}
