using Wright.Messaging.Client;
using Wright.Messaging.Contracts;

namespace CharleyCompany.Dashboard.Web.Services;

public interface ICharlieTextMessagingService
{
    bool IsConfigured { get; }
    Task SetConsentAsync(string userId, string phoneNumber, bool optedIn, CancellationToken cancellationToken);
    Task SendVerificationAsync(string userId, string phoneNumber, string code, CancellationToken cancellationToken);
    Task SendOperationalAsync(string userId, string phoneNumber, string body, string idempotencyKey, CancellationToken cancellationToken, string? actionUrl = null, string? actionLabel = null);
}

public sealed class WrightCharlieTextMessagingService(IMessagingClient client) : ICharlieTextMessagingService
{
    public bool IsConfigured => true;

    public async Task SetConsentAsync(string userId, string phoneNumber, bool optedIn, CancellationToken cancellationToken) =>
        _ = await client.SetConsentAsync(new UpdateConsentRequest(phoneNumber, optedIn, userId), cancellationToken);

    public async Task SendVerificationAsync(string userId, string phoneNumber, string code, CancellationToken cancellationToken) =>
        _ = await client.SendAsync(new SendTextRequest(
            phoneNumber,
            $"Charlie Company verification code: {code}. It expires in 10 minutes. Do not share this code.",
            userId,
            $"charlie-company:phone-verification:{userId}:{DateTimeOffset.UtcNow:yyyyMMddHHmm}"), cancellationToken);

    public async Task SendOperationalAsync(string userId, string phoneNumber, string body, string idempotencyKey, CancellationToken cancellationToken, string? actionUrl = null, string? actionLabel = null) =>
        _ = await client.SendAsync(new SendTextRequest(
            phoneNumber,
            body,
            userId,
            idempotencyKey,
            string.IsNullOrWhiteSpace(actionUrl) ? null : new MessageActionLink(new Uri(actionUrl), actionLabel ?? "View report")), cancellationToken);
}

public sealed class UnconfiguredCharlieTextMessagingService : ICharlieTextMessagingService
{
    public bool IsConfigured => false;
    public Task SetConsentAsync(string userId, string phoneNumber, bool optedIn, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task SendVerificationAsync(string userId, string phoneNumber, string code, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Text messaging is not configured.");
    public Task SendOperationalAsync(string userId, string phoneNumber, string body, string idempotencyKey, CancellationToken cancellationToken, string? actionUrl = null, string? actionLabel = null) =>
        throw new InvalidOperationException("Text messaging is not configured.");
}
