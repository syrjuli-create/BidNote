using BidNote.Application.Results;
using BidNote.Domain.Results;
using Microsoft.EntityFrameworkCore;

namespace BidNote.Infrastructure.Persistence;

/// <summary>
/// 경쟁사 투찰 성향. 업체별 순위를 받아 둔 공고(내 투찰·관심 공고, 내 면허로 참여할 수 있었던 최근 공고)에서 센다.
/// 업체는 사업자번호로 구분하고, 투찰금액이 없는 줄(입찰 취소 등)은 뺀다. 순위 없는 줄(하한 미달·예가 초과)도 그 업체가 써낸 값이라 넣는다.
/// </summary>
public sealed class CompetitorQueries(IDbContextFactory<BidNoteDbContext> factory) : ICompetitorQueries
{
    public async Task<CompetitorOverview> OverviewAsync(string? search, int minMeetings, int top, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        int notices = await db.BidResults.CountAsync(r => r.RanksFetchedAt != null, cancellationToken).ConfigureAwait(false);
        var bids = db.BidResultRanks.AsNoTracking().Where(x => x.BizNo != "" && x.Amount != null);
        int companies = await bids.Select(x => x.BizNo).Distinct().CountAsync(cancellationToken).ConfigureAwait(false);

        string text = (search ?? "").Trim();
        if (text.Length > 0) bids = bids.Where(x => x.CompanyName.Contains(text));
        var groups = await bids.GroupBy(x => x.BizNo)
            .Select(g => new
            {
                BizNo = g.Key,
                Name = g.Max(x => x.CompanyName),
                Meetings = g.Select(x => x.BidResultId).Distinct().Count(),
                Wins = g.Count(x => x.Rank == 1),
            })
            .Where(x => x.Meetings >= minMeetings)
            .OrderByDescending(x => x.Meetings).ThenByDescending(x => x.Wins).ThenBy(x => x.BizNo)
            .Take(Math.Clamp(top, 1, 500))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var bizNos = groups.Select(x => x.BizNo).ToList();
        var rates = (await RowsAsync(db, x => bizNos.Contains(x.BizNo), cancellationToken).ConfigureAwait(false))
            .GroupBy(x => x.BizNo).ToDictionary(g => g.Key, g => g.ToList());
        return new CompetitorOverview(notices, companies, [.. groups.Select(g => Summarize(g.BizNo, g.Name ?? "", g.Meetings, g.Wins, rates.GetValueOrDefault(g.BizNo, [])))]);
    }

    public async Task<NoticeCompetitors?> ForNoticeAsync(int noticeId, int minMeetings, int top, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var result = await db.BidResults.AsNoTracking().Where(r => r.NoticeId == noticeId)
            .Select(r => new { r.Id, r.RanksFetchedAt }).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (result?.RanksFetchedAt is null) return null;

        string me = BidResultMath.BizNoKey(await db.CompanyProfiles.AsNoTracking().Select(x => x.BizNo).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false));
        var participants = (await db.BidResultRanks.AsNoTracking()
                .Where(x => x.BidResultId == result.Id && x.BizNo != "" && x.Amount != null)
                .Select(x => x.BizNo).Distinct().ToListAsync(cancellationToken).ConfigureAwait(false))
            .Where(b => me.Length == 0 || BidResultMath.BizNoKey(b) != me)
            .ToList();
        var frequent = await db.BidResultRanks.AsNoTracking()
            .Where(x => participants.Contains(x.BizNo) && x.Amount != null)
            .GroupBy(x => x.BizNo)
            .Select(g => new { BizNo = g.Key, Name = g.Max(x => x.CompanyName), Meetings = g.Select(x => x.BidResultId).Distinct().Count(), Wins = g.Count(x => x.Rank == 1) })
            .Where(x => x.Meetings >= minMeetings)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var shown = frequent.OrderByDescending(x => x.Meetings).ThenByDescending(x => x.Wins).ThenBy(x => x.BizNo, StringComparer.Ordinal)
            .Take(Math.Clamp(top, 1, 100)).ToList();
        var bizNos = shown.Select(x => x.BizNo).ToList();
        var rows = (await RowsAsync(db, x => bizNos.Contains(x.BizNo), cancellationToken).ConfigureAwait(false))
            .GroupBy(x => x.BizNo).ToDictionary(g => g.Key, g => g.ToList());
        return new NoticeCompetitors(frequent.Count, minMeetings, [.. shown.Select(g =>
        {
            var all = rows.GetValueOrDefault(g.BizNo, []);
            var here = all.FirstOrDefault(x => x.NoticeId == noticeId);
            return new NoticeCompetitor(Summarize(g.BizNo, g.Name ?? "", g.Meetings, g.Wins, all), here?.Rank, here?.Rate);
        })]);
    }

    public async Task<CompetitorDetail?> GetAsync(string bizNo, CancellationToken cancellationToken)
    {
        string key = (bizNo ?? "").Trim();
        if (key.Length == 0) return null;
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var rows = await RowsAsync(db, x => x.BizNo == key, cancellationToken).ConfigureAwait(false);
        if (rows.Count == 0) return null;

        var noticeIds = rows.Select(x => x.NoticeId).Distinct().ToList();
        var mine = await db.MyBids.AsNoTracking().Where(b => noticeIds.Contains(b.NoticeId))
            .ToDictionaryAsync(b => b.NoticeId, b => b.AssessmentRate, cancellationToken).ConfigureAwait(false);
        string name = rows.OrderByDescending(x => x.OpenedAt).First().Name;
        var summary = Summarize(key, name, noticeIds.Count, rows.Count(x => x.Rank == 1), rows);
        var bids = rows.OrderByDescending(x => x.OpenedAt).ThenBy(x => x.Rank ?? int.MaxValue)
            .Select(x => new CompetitorBid(x.NoticeId, x.NoticeName, x.OpenedAt, x.ParticipantCount, x.Rank, x.Amount, x.Rate,
                BidResultMath.ActualAssessmentRate(x.PlannedPrice, x.BasePrice), x.Remark, mine.TryGetValue(x.NoticeId, out double r) ? r : null))
            .ToList();
        return new CompetitorDetail(summary, bids, CompetitorMath.Histogram([.. rows.Where(x => x.Rate is not null).Select(x => x.Rate!.Value)]));
    }

    private sealed record Row(string BizNo, string Name, int NoticeId, string NoticeName, DateTime? OpenedAt, int ParticipantCount,
        int? Rank, long? Amount, string Remark, long? BasePrice, long? PlannedPrice, double? Rate);

    private static CompetitorSummary Summarize(string bizNo, string name, int meetings, int wins, IReadOnlyList<Row> rows)
    {
        var sorted = rows.Where(x => x.Rate is not null).Select(x => x.Rate!.Value).Order().ToList();
        return new CompetitorSummary(bizNo, name, meetings, wins,
            CompetitorMath.Quantile(sorted, 0.5), CompetitorMath.Quantile(sorted, 0.25), CompetitorMath.Quantile(sorted, 0.75),
            rows.Max(x => x.OpenedAt));
    }

    /// <summary>순위 줄을 개찰 결과·공고와 붙이고 사정률을 계산한다 (기초금액은 예비가격 상세의 값을 먼저 쓴다).</summary>
    private static async Task<List<Row>> RowsAsync(BidNoteDbContext db, System.Linq.Expressions.Expression<Func<BidResultRank, bool>> where,
        CancellationToken cancellationToken)
    {
        var raw = await db.BidResultRanks.AsNoTracking().Where(where).Where(x => x.Amount != null)
            .Join(db.BidResults.AsNoTracking(), x => x.BidResultId, r => r.Id, (x, r) => new
            {
                x.BizNo,
                x.CompanyName,
                r.NoticeId,
                NoticeName = r.Notice!.Name,
                r.OpenedAt,
                r.ParticipantCount,
                x.Rank,
                x.Amount,
                x.Remark,
                BasePrice = r.ResultBasePrice ?? r.Notice.BasePrice,
                r.PlannedPrice,
                r.Notice.LowerLimitRate,
                r.Notice.AValue,
            })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return [.. raw.Select(x => new Row(x.BizNo, x.CompanyName, x.NoticeId, x.NoticeName, x.OpenedAt, x.ParticipantCount, x.Rank, x.Amount, x.Remark,
            x.BasePrice, x.PlannedPrice, BidResultMath.AssessmentRateOf(x.Amount, x.BasePrice, x.LowerLimitRate, x.AValue ?? 0)))];
    }
}
