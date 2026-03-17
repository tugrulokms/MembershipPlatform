namespace MembershipService.Infrastructure.Messaging;

public sealed class RabbitMqSettings
{
    public string HostName { get; set; } = "localhost";
    public string UserName { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public string Exchange { get; set; } = "membership.events";
    public string ExchangeDlx { get; set; } = "membership.events.dlx";
}
