using BidNote.Application.Favorites;
using BidNote.Domain.Favorites;
using Microsoft.EntityFrameworkCore;

namespace BidNote.Infrastructure.Persistence;

/// <summary>
/// 관심 공고 저장. 화면의 별 버튼 여러 개가 동시에 눌려도 겹치지 않게 호출마다 DB 연결을 새로 연다.
/// </summary>
public sealed class FavoriteStore(IDbContextFactory<BidNoteDbContext> factory, TimeProvider timeProvider) : IFavoriteStore
{
    public async Task<bool> ToggleAsync(int noticeId, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var row = await db.FavoriteNotices.FirstOrDefaultAsync(x => x.NoticeId == noticeId, cancellationToken).ConfigureAwait(false);
        if (row is not null)
        {
            db.FavoriteNotices.Remove(row);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }
        if (!await db.Notices.AnyAsync(x => x.Id == noticeId, cancellationToken).ConfigureAwait(false))
            throw new KeyNotFoundException($"공고 {noticeId}를 찾을 수 없습니다.");
        DateTime now = timeProvider.GetLocalNow().DateTime;
        db.FavoriteNotices.Add(new FavoriteNotice { NoticeId = noticeId, AddedAt = now, UpdatedAt = now });
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task RemoveAsync(int noticeId, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await db.FavoriteNotices.Where(x => x.NoticeId == noticeId).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveMemoAsync(int noticeId, string memo, CancellationToken cancellationToken)
    {
        string text = (memo ?? "").Trim();
        if (text.Length > FavoriteNotice.MaxMemoLength) text = text[..FavoriteNotice.MaxMemoLength];
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        DateTime now = timeProvider.GetLocalNow().DateTime;
        var row = await db.FavoriteNotices.FirstOrDefaultAsync(x => x.NoticeId == noticeId, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            row = new FavoriteNotice { NoticeId = noticeId, AddedAt = now };
            db.FavoriteNotices.Add(row);
        }
        row.Memo = text;
        row.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlySet<int>> FavoriteIdsAsync(IReadOnlyCollection<int> noticeIds, CancellationToken cancellationToken)
    {
        if (noticeIds.Count == 0) return new HashSet<int>();
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var ids = await db.FavoriteNotices.Where(x => noticeIds.Contains(x.NoticeId)).Select(x => x.NoticeId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return ids.ToHashSet();
    }

    public async Task<string?> MemoAsync(int noticeId, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.FavoriteNotices.Where(x => x.NoticeId == noticeId).Select(x => x.Memo)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<FavoriteList> ListAsync(DateTime now, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var all = await db.FavoriteNotices.AsNoTracking()
            .Select(f => new FavoriteItem(f.NoticeId, f.Notice!.BidNo, f.Notice.Name, f.Notice.Agency, f.Notice.Region, f.Notice.License,
                f.Notice.BasePrice, f.Notice.DueAt, f.Notice.IsActive, f.Notice.CorrectionCount, f.Memo, f.AddedAt))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return new FavoriteList(
            [.. all.Where(x => x.IsActive && x.DueAt > now).OrderBy(x => x.DueAt)],
            [.. all.Where(x => !x.IsActive || x.DueAt <= now).OrderByDescending(x => x.DueAt)]);
    }

    public async Task<int> CountOpenAsync(DateTime now, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.FavoriteNotices.CountAsync(f => f.Notice!.IsActive && f.Notice.DueAt > now, cancellationToken).ConfigureAwait(false);
    }
}
