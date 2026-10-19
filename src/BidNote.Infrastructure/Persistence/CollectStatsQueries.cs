using BidNote.Application.Collecting;
using Microsoft.EntityFrameworkCore;

namespace BidNote.Infrastructure.Persistence;

/// <summary>SQLite 파일 위치. 수집 현황 화면에서 DB 크기를 보여 줄 때 쓴다.</summary>
public sealed record StorageLocation(string DatabasePath);

public sealed class CollectStatsQueries(BidNoteDbContext db, StorageLocation storage) : ICollectStatsQueries
{
    public async Task<CollectStats> GetStatsAsync(CancellationToken cancellationToken)
    {
        int notices = await db.Notices.CountAsync(cancellationToken).ConfigureAwait(false);
        int raws = await db.ApiRawResponses.CountAsync(cancellationToken).ConfigureAwait(false);
        int histories = await db.NoticeHistories.CountAsync(cancellationToken).ConfigureAwait(false);
        DateTime? lastRaw = await db.ApiRawResponses.OrderByDescending(x => x.Id)
            .Select(x => (DateTime?)x.CollectedAt).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        // WAL 모드면 최근 쓰기가 -wal 파일에 먼저 쌓이므로 함께 더한다.
        long bytes = new[] { "", "-wal" }.Select(s => new FileInfo(storage.DatabasePath + s)).Where(f => f.Exists).Sum(f => f.Length);
        return new CollectStats(notices, raws, histories, lastRaw, bytes);
    }
}
