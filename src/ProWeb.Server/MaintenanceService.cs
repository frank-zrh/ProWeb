using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProWeb.Server.Config;
using ProWeb.Server.Storage;

namespace ProWeb.Server;

/// <summary>
/// Periodic maintenance job that enforces TTL semantics at runtime: it purges expired sessions and
/// invalidated cache entries on a configurable interval so the SQLite store does not grow without
/// bound. Purge counts are written to the structured log for audit (aligns with F-J observability).
/// </summary>
public sealed class MaintenanceService : BackgroundService
{
    private readonly SessionRepository _sessions;
    private readonly CacheRepository _cache;
    private readonly MaintenanceOptions _options;
    private readonly ILogger<MaintenanceService> _logger;
    private readonly Func<long> _now;

    public MaintenanceService(
        SessionRepository sessions,
        CacheRepository cache,
        ProWebOptions options,
        ILogger<MaintenanceService> logger,
        Func<long>? nowProvider = null)
    {
        _sessions = sessions;
        _cache = cache;
        _options = options.Maintenance;
        _logger = logger;
        _now = nowProvider ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    /// <summary>
    /// Runs a single purge sweep and returns the number of sessions and cache entries removed.
    /// Extracted so the sweep can be unit-tested without hosting the background loop.
    /// </summary>
    public (int SessionsPurged, int CachePurged) PurgeOnce()
    {
        var now = _now();
        var sessionsPurged = _sessions.PurgeExpired(now);
        var cachePurged = _cache.PurgeExpired(now);
        if (sessionsPurged > 0 || cachePurged > 0)
        {
            _logger.LogInformation(
                "Maintenance purge removed {Sessions} expired session(s) and {Cache} expired cache entry(ies).",
                sessionsPurged, cachePurged);
        }

        return (sessionsPurged, cachePurged);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Maintenance service disabled by configuration.");
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(1, _options.PurgeIntervalSeconds));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                PurgeOnce();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Maintenance purge sweep failed; will retry next interval.");
            }

            try
            {
                await Task.Delay(interval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
