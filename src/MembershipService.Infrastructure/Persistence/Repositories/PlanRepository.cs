using MembershipService.Application.Interfaces;
using MembershipService.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MembershipService.Infrastructure.Persistence.Repositories;

public sealed class PlanRepository : IPlanRepository
{
    private readonly MembershipDbContext _db;

    public PlanRepository(MembershipDbContext db) => _db = db;

    public async Task<Plan?> GetByIdAsync(int id)
        => await _db.Plans.FirstOrDefaultAsync(p => p.Id == id);
}
