namespace MembershipService.Application.Interfaces;

// Why a separate IUnitOfWork instead of calling SaveChanges on each repository?
// Because a single use case may modify multiple aggregates (e.g., create subscription
// AND materialize entitlements). All changes should commit or roll back as one
// atomic operation. The use case — not the repository — decides when to commit.
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
