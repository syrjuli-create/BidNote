using BidNote.Application.Collecting;
using BidNote.Domain.Collecting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BidNote.Infrastructure.Persistence;

public sealed class CollectorSettingsStore(BidNoteDbContext db, IOptions<CollectorOptions> defaults, TimeProvider timeProvider) : ICollectorSettingsStore
{
    public async Task<CollectorSettingsValues> GetAsync(CancellationToken cancellationToken)
    {
        var row = await db.CollectorSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == CollectorSetting.SingletonId, cancellationToken).ConfigureAwait(false);
        return row is null
            ? new CollectorSettingsValues(defaults.Value.BackfillDays, defaults.Value.IntervalMinutes, null)
            : new CollectorSettingsValues(row.BackfillDays, row.IntervalMinutes, row.LastSuccessTo, row.ResultsLastSuccessTo, row.RegionsLastSuccessTo, row.RegionsCoveredFrom);
    }

    public async Task SaveAsync(int backfillDays, int intervalMinutes, CancellationToken cancellationToken)
    {
        CollectorRules.Validate(backfillDays, intervalMinutes);
        var row = await RowAsync(cancellationToken).ConfigureAwait(false);
        row.BackfillDays = backfillDays;
        row.IntervalMinutes = intervalMinutes;
        row.UpdatedAt = timeProvider.GetLocalNow().DateTime;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task MarkSuccessAsync(DateTime collectedTo, CancellationToken cancellationToken)
    {
        var row = await RowAsync(cancellationToken).ConfigureAwait(false);
        if (row.LastSuccessTo is null || collectedTo > row.LastSuccessTo) row.LastSuccessTo = collectedTo;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task MarkResultsSuccessAsync(DateTime collectedTo, CancellationToken cancellationToken)
    {
        var row = await RowAsync(cancellationToken).ConfigureAwait(false);
        if (row.ResultsLastSuccessTo is null || collectedTo > row.ResultsLastSuccessTo) row.ResultsLastSuccessTo = collectedTo;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task MarkRegionsSuccessAsync(DateTime collectedTo, CancellationToken cancellationToken)
    {
        var row = await RowAsync(cancellationToken).ConfigureAwait(false);
        if (row.RegionsLastSuccessTo is null || collectedTo > row.RegionsLastSuccessTo) row.RegionsLastSuccessTo = collectedTo;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task MarkRegionsCoveredFromAsync(DateTime coveredFrom, CancellationToken cancellationToken)
    {
        var row = await RowAsync(cancellationToken).ConfigureAwait(false);
        if (row.RegionsCoveredFrom is null || coveredFrom < row.RegionsCoveredFrom) row.RegionsCoveredFrom = coveredFrom;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<CollectorSetting> RowAsync(CancellationToken cancellationToken)
    {
        var row = await db.CollectorSettings.FirstOrDefaultAsync(x => x.Id == CollectorSetting.SingletonId, cancellationToken).ConfigureAwait(false);
        if (row is not null) return row;
        row = new CollectorSetting
        {
            BackfillDays = defaults.Value.BackfillDays,
            IntervalMinutes = defaults.Value.IntervalMinutes,
            UpdatedAt = timeProvider.GetLocalNow().DateTime,
        };
        db.CollectorSettings.Add(row);
        return row;
    }
}
