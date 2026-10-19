namespace BidNote.Application.Collecting;

public sealed record CollectStats(int NoticeCount, int RawCount, int HistoryCount, DateTime? LastRawSavedAt, long DatabaseBytes);

public interface ICollectStatsQueries
{
    Task<CollectStats> GetStatsAsync(CancellationToken cancellationToken);
}
