namespace EventHub.Core.Common;

public interface IUnitOfWork {
    Task BeginTransactionAsync();
    Task CommitAsync();
    Task RollbackAsync();
}