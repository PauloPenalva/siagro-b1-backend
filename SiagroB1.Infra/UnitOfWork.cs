using Microsoft.EntityFrameworkCore.Storage;
using SiagroB1.Infra.Context;

namespace SiagroB1.Infra;

public class UnitOfWork(AppDbContext context) : IUnitOfWork
{
    private IDbContextTransaction _transaction;

    public AppDbContext Context => context;
    
    public async Task BeginTransactionAsync()
    {
        if (_transaction == null)
        {
            _transaction = await context.Database.BeginTransactionAsync();
        }
    }
    
    public async Task CommitAsync()
    {
        try
        {
            await _transaction.CommitAsync();
            await _transaction.DisposeAsync();
            _transaction =  null;
        }
        catch
        {
            await RollbackAsync();
            throw;
        }
    }
    
    public async Task RollbackAsync()
    {
        if (_transaction != null)
        {
            await _transaction.RollbackAsync();
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }
    
    public async Task SaveChangesAsync()
    {
        await context.SaveChangesAsync();
    }
    
    public void Dispose()
    {
        _transaction?.Dispose();
        context.Dispose();
    }
}