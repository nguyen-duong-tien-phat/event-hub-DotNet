using EventHub.Core.Interfaces;
using Microsoft.EntityFrameworkCore.Storage;

namespace EventHub.Infrastructure.Data;

public class UnitOfWork(AppDbContext db): IUnitOfWork {
    private IDbContextTransaction? _transaction;

    public async Task BeginTransactionAsync() {
        _transaction = await db.Database.BeginTransactionAsync();
    }
    
    public async Task CommitAsync() {
        if (_transaction != null ) await _transaction.CommitAsync();
    }
    
    public async Task RollbackAsync() {
        if (_transaction != null ) await _transaction.RollbackAsync();
    }
}