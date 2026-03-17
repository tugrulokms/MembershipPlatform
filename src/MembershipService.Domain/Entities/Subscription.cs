using MembershipService.Domain.Enums;
using MembershipService.Domain.Events;

namespace MembershipService.Domain.Entities
{
    public class Subscription : BaseEntity
    {
        public Guid Id { get; set; }
        public required int UserId { get; set; }
        public SubscriptionStatus Status { get; private set; } = SubscriptionStatus.Active;
        public DateTime CurrentPeriodStart { get; set; } = DateTime.UtcNow;
        public DateTime CurrentPeriodEnd { get; set; } = DateTime.UtcNow.AddMonths(1);
        public required Plan Plan { get; set; }
        public required int PlanId { get; set; }
        public DateTime? CanceledAt { get; set; }
        public DateTime? LastBillingEventTimestamp { get; set; }
        public DateTime? PastDueAt { get; set; }

        public static Subscription Create(int userId, int planId, Plan plan)
        {
            var subscription = new Subscription
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                PlanId = planId,
                Plan = plan,
                CurrentPeriodStart = DateTime.UtcNow,
                CurrentPeriodEnd = DateTime.UtcNow.AddMonths((int)plan.BillingPeriod)
            };
            subscription.RaiseDomainEvent(new SubscriptionCreatedEvent(
            subscription.Id, userId, planId, subscription.CurrentPeriodStart, subscription.CurrentPeriodEnd                
            ));

            return subscription;
        }

        public void Activate()
        {
            
            if (!(Status == SubscriptionStatus.PastDue || Status == SubscriptionStatus.Canceled))
                throw new InvalidOperationException("Transition to active isn't valid if subscription isn't canceled or past due!");
            else
            {
                Status = SubscriptionStatus.Active;
                CanceledAt = null;
            }   
        }

        public void Cancel()
        {
            if (Status != SubscriptionStatus.Active)
                throw new InvalidOperationException("Transition to cancel isn't valid if subscription isn't active!");
            else
            {
                Status = SubscriptionStatus.Canceled;
                CanceledAt = DateTime.UtcNow;
                RaiseDomainEvent(new SubscriptionCanceledEvent(Id, UserId, CanceledAt.Value, CurrentPeriodEnd));
            }
        }

        public void MarkPastDue()
        {
            if (Status != SubscriptionStatus.Active)
                throw new InvalidOperationException("Transition to past due isn't valid if subscription isn't active!");
            else
            {
                Status = SubscriptionStatus.PastDue;
                PastDueAt = DateTime.UtcNow;
            } 
        }

        public void Expire()
        {
            if (!(Status == SubscriptionStatus.PastDue || Status == SubscriptionStatus.Canceled))
                throw new InvalidOperationException("Transition to expire isn't valid if subscription isn't past due or canceled!");
            else
            {
                Status = SubscriptionStatus.Expired;
                RaiseDomainEvent(new SubscriptionExpiredEvent(Id, UserId, DateTime.UtcNow));
            }
        }

        public bool IsStaleEvent(DateTime eventOccurredAt)
        {
            return LastBillingEventTimestamp.HasValue && eventOccurredAt <= LastBillingEventTimestamp.Value;
        }
    }
}