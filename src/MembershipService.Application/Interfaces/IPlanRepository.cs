using MembershipService.Domain.Entities;

namespace MembershipService.Application.Interfaces;

public interface IPlanRepository
{
    Task<Plan?> GetByIdAsync(int id);
}
