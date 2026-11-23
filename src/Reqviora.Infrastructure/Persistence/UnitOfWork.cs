using Reqviora.Application.Common.Interfaces;

namespace Reqviora.Infrastructure.Persistence;

public sealed class UnitOfWork(ReqvioraDbContext dbContext) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
