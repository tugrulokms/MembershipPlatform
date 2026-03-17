using MembershipService.Application.Interfaces;
using MembershipService.Domain.Entities;

namespace MembershipService.Application.Subscriptions.Create;

public sealed class CreateSubscriptionHandler
{
    private readonly IPlanRepository _planRepository;
    private readonly ISubscriptionRepository _subscriptionRepository;
    private readonly IUnitOfWork _unitOfWork;

    public CreateSubscriptionHandler(
        IPlanRepository planRepository,
        ISubscriptionRepository subscriptionRepository,
        IUnitOfWork unitOfWork)
    {
        _planRepository = planRepository;
        _subscriptionRepository = subscriptionRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<CreateSubscriptionResult> HandleAsync(CreateSubscriptionCommand command)
    {
        // 1. Validate: does the plan exist and is it subscribable?
        var plan = await _planRepository.GetByIdAsync(command.PlanId);
        if (plan is null)
            throw new InvalidOperationException($"Plan {command.PlanId} not found.");
        if (!plan.IsActive)
            throw new InvalidOperationException($"Plan {command.PlanId} is no longer available.");

        // 2. Business rule: does the user already have an active subscription?
        var existing = await _subscriptionRepository.GetActiveByUserIdAsync(command.UserId);
        if (existing is not null)
            throw new InvalidOperationException($"User {command.UserId} already has an active subscription.");

        // 3. Create the domain entity.
        // Notice: the Subscription starts as Active. In a real system you might start
        // as Pending and wait for payment confirmation, but for our flow the billing
        // event has already confirmed payment before we reach this point.

        var subscription = Subscription.Create(
            command.UserId,
            command.PlanId,
            plan
        );
        // 4. Persist
        await _subscriptionRepository.AddAsync(subscription);
        await _unitOfWork.SaveChangesAsync();

        return new CreateSubscriptionResult(subscription.Id);
    }
}
