using CharleyCompany.Dashboard.Web.Models;

namespace CharleyCompany.Dashboard.Web.Services;

public sealed class DashboardNotificationService
{
    private readonly object syncRoot = new();
    private readonly Queue<DashboardEvent> events = new();

    public event Func<Task>? DashboardChanged;

    public IReadOnlyList<DashboardEvent> RecentEvents
    {
        get
        {
            lock (syncRoot)
            {
                return events.ToArray();
            }
        }
    }

    public async Task PublishAsync(DashboardEvent dashboardEvent)
    {
        lock (syncRoot)
        {
            events.Enqueue(dashboardEvent);

            while (events.Count > 8)
            {
                events.Dequeue();
            }
        }

        if (DashboardChanged is not null)
        {
            await DashboardChanged.Invoke();
        }
    }

    public async Task NotifyChangedAsync()
    {
        if (DashboardChanged is not null)
        {
            await DashboardChanged.Invoke();
        }
    }
}

