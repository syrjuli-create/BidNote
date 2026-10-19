using BidNote.Application.Collecting;
using BidNote.Domain.Collecting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BidNote.Infrastructure.Persistence;

public sealed class ApiUsageTracker(
    BidNoteDbContext db,
    IOptions<ApiLimitOptions> options,
    TimeProvider timeProvider,
    ILogger<ApiUsageTracker> logger) : IApiUsageTracker
{
    private DateOnly Today => DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);

    public async Task<bool> CanCallAsync(string apiName, CancellationToken cancellationToken)
    {
        var today = Today;
        int count = await db.ApiUsages.Where(x => x.Date == today && x.ApiName == apiName)
            .Select(x => x.Count).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        int limit = options.Value.LimitFor(apiName);
        bool ok = count < limit * options.Value.StopAtRatio;
        if (!ok) logger.LogWarning("{Api} 오늘 호출 {Count}/{Limit}건으로 한도에 가까워 호출을 멈춥니다.", apiName, count, limit);
        return ok;
    }

    public async Task RecordAsync(string apiName, CancellationToken cancellationToken)
    {
        var today = Today;
        DateTime now = timeProvider.GetLocalNow().DateTime;
        int updated = await db.ApiUsages.Where(x => x.Date == today && x.ApiName == apiName)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Count, x => x.Count + 1).SetProperty(x => x.LastCalledAt, now), cancellationToken)
            .ConfigureAwait(false);
        if (updated > 0) return;

        db.ApiUsages.Add(new ApiUsage { Date = today, ApiName = apiName, Count = 1, LastCalledAt = now });
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ApiUsageToday>> GetTodayAsync(CancellationToken cancellationToken)
    {
        var today = Today;
        var rows = await db.ApiUsages.AsNoTracking().Where(x => x.Date == today).ToListAsync(cancellationToken).ConfigureAwait(false);
        var o = options.Value;
        var names = rows.Select(x => x.ApiName).Union(o.PerApi.Keys, StringComparer.Ordinal);
        return [.. names
            .Select(n => rows.FirstOrDefault(r => r.ApiName == n) is { } r
                ? new ApiUsageToday(n, r.Count, o.LimitFor(n), r.LastCalledAt)
                : new ApiUsageToday(n, 0, o.LimitFor(n), null))
            .OrderByDescending(x => x.Ratio).ThenBy(x => x.ApiName, StringComparer.Ordinal)];
    }
}
