using System.Net.Http.Json;
using System.Threading.Channels;

namespace CharleyCompany.Dashboard.Web.Services;

public sealed class RemoteOperationalEventForwarder(HttpClient httpClient, IConfiguration configuration, ILogger<RemoteOperationalEventForwarder> logger) : BackgroundService
{
    private readonly Channel<OperationalEventRequest> queue = Channel.CreateBounded<OperationalEventRequest>(new BoundedChannelOptions(5000) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

    public ValueTask QueueAsync(OperationalEventRequest item, CancellationToken cancellationToken = default) => queue.Writer.WriteAsync(item with { EventId = item.EventId ?? Guid.NewGuid() }, cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var item in queue.Reader.ReadAllAsync(stoppingToken))
        {
            var target = configuration["Observability:ForwardingUrl"];
            var key = configuration["Observability:IngestionKey"];
            if (string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(key)) continue;
            for (var attempt = 1; attempt <= 5 && !stoppingToken.IsCancellationRequested; attempt++)
            {
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Post, target) { Content = JsonContent.Create(item) };
                    request.Headers.Add("X-CCV-Observability-Key", key);
                    using var response = await httpClient.SendAsync(request, stoppingToken);
                    if (response.IsSuccessStatusCode) break;
                    if (attempt == 5) logger.LogWarning("Operational event {EventId} forwarding failed with HTTP {StatusCode}", item.EventId, response.StatusCode);
                }
                catch (Exception ex) when (attempt < 5)
                {
                    logger.LogWarning(ex, "Operational event forwarding attempt {Attempt} failed", attempt);
                }
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), stoppingToken);
            }
        }
    }
}
