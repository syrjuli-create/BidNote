using BidNote.Application.Bidding;
using BidNote.Domain.Bidding;
using BidNote.Domain.Favorites;
using Microsoft.EntityFrameworkCore;

namespace BidNote.Infrastructure.Persistence;

/// <summary>내 투찰 기록 저장. 화면의 버튼이 겹쳐 눌려도 섞이지 않게 호출마다 DB 연결을 새로 연다.</summary>
public sealed class MyBidStore(IDbContextFactory<BidNoteDbContext> factory, TimeProvider timeProvider) : IMyBidStore
{
    public async Task<bool> SaveAsync(MyBidInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.AssessmentRate <= 0) throw new ArgumentOutOfRangeException(nameof(input), "사정률은 0보다 커야 합니다.");
        if (input.BidAmount <= 0) throw new ArgumentOutOfRangeException(nameof(input), "투찰금액은 0보다 커야 합니다.");
        string memo = (input.Memo ?? "").Trim();
        if (memo.Length > MyBid.MaxMemoLength) memo = memo[..MyBid.MaxMemoLength];

        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var notice = await db.Notices.AsNoTracking().Where(x => x.Id == input.NoticeId)
            .Select(x => new { x.BasePrice, x.AValue }).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"공고 {input.NoticeId}를 찾을 수 없습니다.");

        DateTime now = timeProvider.GetLocalNow().DateTime;
        var row = await db.MyBids.FirstOrDefaultAsync(x => x.NoticeId == input.NoticeId, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            row = new MyBid { NoticeId = input.NoticeId, CreatedAt = now };
            db.MyBids.Add(row);
        }
        row.AssessmentRate = input.AssessmentRate;
        row.BidAmount = input.BidAmount;
        row.BasePriceAtBid = notice.BasePrice;
        row.AValueAtBid = notice.AValue;
        row.Memo = memo;
        row.UpdatedAt = now;

        // 관심 공고에 담아 둔 공고면 투찰 완료 폴더로 옮긴다 (이미 투찰 완료면 그대로).
        var favorite = await db.FavoriteNotices
            .FirstOrDefaultAsync(f => f.NoticeId == input.NoticeId && f.TrashedAt == null, cancellationToken).ConfigureAwait(false);
        bool moved = favorite is not null && favorite.Folder != FavoriteFolder.Submitted;
        if (moved)
        {
            favorite!.Folder = FavoriteFolder.Submitted;
            favorite.UpdatedAt = now;
        }
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return moved;
    }

    public async Task RemoveAsync(int noticeId, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await db.MyBids.Where(x => x.NoticeId == noticeId).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<MyBidView?> GetAsync(int noticeId, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.MyBids.AsNoTracking().Where(x => x.NoticeId == noticeId)
            .Select(x => new MyBidView(x.NoticeId, x.AssessmentRate, x.BidAmount, x.BasePriceAtBid, x.AValueAtBid, x.Memo, x.UpdatedAt))
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<MyBidItem>> ListAsync(DateTime now, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var all = await db.MyBids.AsNoTracking()
            .Select(b => new MyBidItem(b.NoticeId, b.Notice!.BidNo, b.Notice.Name, b.Notice.Agency, b.Notice.Region, b.Notice.License,
                b.Notice.BasePrice, b.Notice.DueAt, b.Notice.OpenAt, b.Notice.IsActive,
                b.AssessmentRate, b.BidAmount, b.BasePriceAtBid, b.Memo, b.UpdatedAt))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return [.. all.Where(x => x.StageAt(now) == MyBidStage.BeforeDue).OrderBy(x => x.DueAt)
            .Concat(all.Where(x => x.StageAt(now) != MyBidStage.BeforeDue).OrderByDescending(x => x.OpenAt))];
    }
}
