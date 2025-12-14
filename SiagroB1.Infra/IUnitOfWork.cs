using Microsoft.EntityFrameworkCore;
using SiagroB1.Infra.Context;

namespace SiagroB1.Infra;

public interface IUnitOfWork 
{
    Task BeginTransactionAsync();
    Task CommitAsync();
    Task RollbackAsync();
    AppDbContext Context { get; }
    Task SaveChangesAsync();
}