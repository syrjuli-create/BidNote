using System.Security.Cryptography;
using System.Text;
using BidNote.Application.Collecting;
using BidNote.Application.Results;
using BidNote.Domain.Collecting;
using BidNote.Domain.Company;
using BidNote.Domain.Notices;
using BidNote.Domain.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BidNote.Infrastructure.Persistence;

/// <summary>
/// 개찰 결과 저장·조회. 개찰 목록은 이미 저장된 공고에만 붙이고(아직 받지 못한 공고는 건너뜀),
/// 원문은 새 결과이거나 바뀐 결과일 때만 남긴다. 업체별 순위는 호출이 많아 필요한 공고만 받는다.
/// </summary>
public sealed class BidResultService(
    IDbContextFactory<BidNoteDbContext> factory,
    IBidResultSource source,
    TimeProvider timeProvider,
    ILogger<BidResultService> logger) : IBidResultService
{
    public async Task<bool> HasAnyAsync(CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.BidResults.AnyAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> CollectOpeningsAsync(DateTime from, DateTime to, CancellationToken cancellationToken)
    {
        var rows = await source.CollectOpeningsAsync(from, to, cancellationToken).ConfigureAwait(false);
        if (rows.Count == 0) return 0;

        // 재입찰이면 같은 공고가 여러 줄 온다. 가장 마지막 재입찰(분류 번호가 여럿이면 가장 앞)만 남긴다.
        var latest = rows.Where(x => x.BidNo.Length > 0)
            .GroupBy(x => x.BidNo, StringComparer.Ordinal)
            .Select(g => g.OrderByDescending(x => x.RebidNo, StringComparer.Ordinal).ThenBy(x => x.ClassNo, StringComparer.Ordinal).First())
            .ToList();

        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var bidNos = latest.Select(x => x.BidNo).ToList();
        var notices = await db.Notices.AsNoTracking()
            .Where(n => n.SourceSystem == source.SourceSystem && n.BusinessType == BusinessType.Construction && bidNos.Contains(n.BidNo))
            .Select(n => new { n.Id, n.BidNo })
            .ToDictionaryAsync(n => n.BidNo, n => n.Id, StringComparer.Ordinal, cancellationToken).ConfigureAwait(false);
        var noticeIds = notices.Values.ToList();
        var existing = await db.BidResults.Where(r => noticeIds.Contains(r.NoticeId))
            .ToDictionaryAsync(r => r.NoticeId, cancellationToken).ConfigureAwait(false);

        DateTime now = timeProvider.GetLocalNow().DateTime;
        int updated = 0, missing = 0;
        foreach (var row in latest)
        {
            if (!notices.TryGetValue(row.BidNo, out int noticeId))
            {
                missing++;
                continue;
            }
            string hash = HashOf(row.RawJson);
            if (existing.TryGetValue(noticeId, out var result) && result.RawHash == hash) continue;

            if (result is null)
            {
                result = new BidResult { NoticeId = noticeId };
                db.BidResults.Add(result);
                existing[noticeId] = result;
            }
            else if (result.BidSeq != row.BidSeq || result.ClassNo != row.ClassNo || result.RebidNo != row.RebidNo)
            {
                // 다른 차례(재입찰 등)의 결과로 바뀌면 이전 차례의 순위·예정가격은 버리고 다시 받는다.
                await db.BidResultRanks.Where(x => x.BidResultId == result.Id).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
                result.PlannedPrice = null;
                result.ResultBasePrice = null;
                result.RanksFetchedAt = null;
                result.RanksComplete = false;
            }

            result.BidSeq = row.BidSeq;
            result.ClassNo = row.ClassNo;
            result.RebidNo = row.RebidNo;
            result.Status = row.Status;
            result.ParticipantCount = row.ParticipantCount;
            result.OpenedAt = row.OpenedAt;
            result.WinnerName = Cut(row.WinnerName);
            result.WinnerBizNo = row.WinnerBizNo;
            result.WinnerAmount = row.WinnerAmount;
            result.WinnerBidRate = row.WinnerBidRate;
            result.RawHash = hash;
            result.UpdatedAt = now;
            db.ApiRawResponses.Add(new ApiRawResponse
            {
                SourceSystem = source.SourceSystem,
                ApiName = row.ApiName,
                RequestParameter = row.RequestParameter,
                BusinessType = BusinessType.Construction,
                BidNo = row.BidNo,
                ResponseJson = row.RawJson,
                HttpStatus = 200,
                CollectedAt = now,
                ParsedAt = now,
                ParseVersion = CollectService.ParseVersion,
            });
            updated++;
        }
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        logger.LogInformation("{Source} 개찰 결과: 받은 {Count}건, 반영 {Updated}건, 공고를 받지 않아 건너뜀 {Missing}건", source.SourceSystem, latest.Count, updated, missing);
        return updated;
    }

    public async Task<bool> FetchRanksAsync(int noticeId, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var result = await db.BidResults.Include(x => x.Notice)
            .FirstOrDefaultAsync(x => x.NoticeId == noticeId, cancellationToken).ConfigureAwait(false);
        if (result?.Notice is null || !result.IsOpened || result.ParticipantCount == 0) return false;

        var ranking = await source.FetchRankingAsync(result.Notice.BidNo, result.BidSeq, result.ClassNo, result.RebidNo, cancellationToken).ConfigureAwait(false);
        if (ranking is null) return false;

        await db.BidResultRanks.Where(x => x.BidResultId == result.Id).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        db.BidResultRanks.AddRange(ranking.Ranks.Select(r => new BidResultRank
        {
            BidResultId = result.Id,
            Rank = r.Rank,
            CompanyName = Cut(r.CompanyName),
            BizNo = r.BizNo,
            Amount = r.Amount,
            BidRate = r.BidRate,
            Remark = r.Remark,
        }));
        result.PlannedPrice = ranking.PlannedPrice ?? result.PlannedPrice;
        result.ResultBasePrice = ranking.BasePrice ?? result.ResultBasePrice;
        result.RanksFetchedAt = timeProvider.GetLocalNow().DateTime;
        result.RanksComplete = ranking.Complete;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>경쟁사 성향용으로 순위를 받을 공고를 고르는 기간 (최근 개찰).</summary>
    public static readonly TimeSpan CompetitorWindow = TimeSpan.FromDays(30);

    public async Task<int> FetchPendingRanksAsync(int max, int competitorMax, CancellationToken cancellationToken)
    {
        List<int> ids;
        await using (var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            ids = await db.BidResults.AsNoTracking()
                .Where(r => r.RanksFetchedAt == null && r.ParticipantCount > 0 && r.Status.Contains("개찰완료")
                    && (db.MyBids.Any(b => b.NoticeId == r.NoticeId) || db.FavoriteNotices.Any(f => f.NoticeId == r.NoticeId && f.TrashedAt == null)))
                .OrderByDescending(r => r.OpenedAt)
                .Select(r => r.NoticeId)
                .Take(max)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            if (competitorMax > 0)
                ids.AddRange(await CompetitorCandidatesAsync(db, ids, competitorMax, cancellationToken).ConfigureAwait(false));
        }
        int fetched = 0;
        foreach (int id in ids)
            if (await FetchRanksAsync(id, cancellationToken).ConfigureAwait(false)) fetched++;
        return fetched;
    }

    /// <summary>
    /// 경쟁사 성향을 쌓을 공고: 최근 개찰했고 순위를 아직 받지 않았으며 내 면허로 참여할 수 있었던 공고(면허만 본다), 최근 개찰순.
    /// 면허를 등록하지 않았으면 고르지 않는다.
    /// </summary>
    private async Task<List<int>> CompetitorCandidatesAsync(BidNoteDbContext db, IReadOnlyCollection<int> exclude, int max, CancellationToken cancellationToken)
    {
        var mine = (await db.CompanyLicenses.AsNoTracking().Select(x => x.Name).ToListAsync(cancellationToken).ConfigureAwait(false))
            .ToHashSet(StringComparer.Ordinal);
        if (mine.Count == 0) return [];
        DateTime since = timeProvider.GetLocalNow().DateTime - CompetitorWindow;
        var recent = await db.BidResults.AsNoTracking()
            .Where(r => r.RanksFetchedAt == null && r.ParticipantCount > 0 && r.Status.Contains("개찰완료") && r.OpenedAt >= since && !exclude.Contains(r.NoticeId))
            .OrderByDescending(r => r.OpenedAt)
            .Select(r => r.NoticeId)
            .Take(1000)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var rows = await db.NoticeLicenses.AsNoTracking().Where(l => recent.Contains(l.NoticeId))
            .Select(l => new { l.NoticeId, l.GroupNo, l.Name, l.Alternatives })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var eligible = rows.GroupBy(r => r.NoticeId)
            .Where(g => LicenseEligibility.Check(g.Select(r => new LicenseRow(r.GroupNo, r.Name, NoticeLicense.SplitAlternatives(r.Alternatives))), mine).Kind == EligibilityKind.Eligible)
            .Select(g => g.Key)
            .ToHashSet();
        return [.. recent.Where(eligible.Contains).Take(max)];
    }

    public async Task<BidResultView?> GetAsync(int noticeId, int top, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var row = await db.BidResults.AsNoTracking().Where(x => x.NoticeId == noticeId)
            .Select(x => new { Result = x, x.Notice!.BasePrice, x.Notice.LowerLimitRate, x.Notice.AValue })
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (row is null) return null;
        var r = row.Result;
        var basis = new Basis(r.ResultBasePrice ?? row.BasePrice, row.LowerLimitRate, row.AValue ?? 0, r.PlannedPrice);

        var ranked = await db.BidResultRanks.AsNoTracking().Where(x => x.BidResultId == r.Id && x.Rank != null)
            .OrderBy(x => x.Rank).Take(Math.Max(top, 1))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        int rankedCount = await db.BidResultRanks.CountAsync(x => x.BidResultId == r.Id && x.Rank != null, cancellationToken).ConfigureAwait(false);
        int totalCount = await db.BidResultRanks.CountAsync(x => x.BidResultId == r.Id, cancellationToken).ConfigureAwait(false);

        BidRankView? winner = r.WinnerAmount is not null || r.WinnerName.Length > 0
            ? new BidRankView(1, r.WinnerName, r.WinnerAmount, r.WinnerBidRate, basis.RateOf(r.WinnerAmount), "")
            : ranked.FirstOrDefault() is { } first ? ToView(first, basis) : null;

        var myBid = await db.MyBids.AsNoTracking().Where(x => x.NoticeId == noticeId)
            .Select(x => new { x.BidAmount, x.AssessmentRate }).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        MyBidOutcome? mine = myBid is null ? null
            : await OutcomeAsync(db, noticeId, r, basis, myBid.BidAmount, myBid.AssessmentRate, cancellationToken).ConfigureAwait(false);

        return new BidResultView(noticeId, r.Status, r.ParticipantCount, r.OpenedAt,
            r.PlannedPrice, BidResultMath.ActualAssessmentRate(r.PlannedPrice, basis.BasePrice), basis.Floor,
            winner, [.. ranked.Select(x => ToView(x, basis))], rankedCount, totalCount,
            r.RanksFetchedAt, r.RanksComplete, mine);
    }

    public async Task<IReadOnlyDictionary<int, MyBidOutcome>> MyBidOutcomesAsync(CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var rows = await db.MyBids.AsNoTracking()
            .Join(db.BidResults.AsNoTracking(), b => b.NoticeId, r => r.NoticeId, (b, r) => new
            {
                b.NoticeId,
                b.BidAmount,
                b.AssessmentRate,
                Result = r,
                r.Notice!.BasePrice,
                r.Notice.LowerLimitRate,
                r.Notice.AValue,
            })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var outcomes = new Dictionary<int, MyBidOutcome>();
        foreach (var x in rows)
        {
            var basis = new Basis(x.Result.ResultBasePrice ?? x.BasePrice, x.LowerLimitRate, x.AValue ?? 0, x.Result.PlannedPrice);
            outcomes[x.NoticeId] = await OutcomeAsync(db, x.NoticeId, x.Result, basis, x.BidAmount, x.AssessmentRate, cancellationToken).ConfigureAwait(false);
        }
        return outcomes;
    }

    /// <summary>사정률을 역산할 때 쓰는 기준값. 기초금액은 예비가격 상세의 값을 먼저 쓴다.</summary>
    private sealed record Basis(long? BasePrice, double LowerLimitRate, long AValue, long? PlannedPrice)
    {
        public long? Floor => BidResultMath.LowerLimitAmount(PlannedPrice, LowerLimitRate, AValue);

        public double? RateOf(long? amount) => BidResultMath.AssessmentRateOf(amount, BasePrice, LowerLimitRate, AValue);
    }

    private static BidRankView ToView(BidResultRank x, Basis basis) =>
        new(x.Rank, x.CompanyName, x.Amount, x.BidRate, basis.RateOf(x.Amount), x.Remark);

    private static async Task<MyBidOutcome> OutcomeAsync(BidNoteDbContext db, int noticeId, BidResult r, Basis basis,
        long myAmount, double myRate, CancellationToken cancellationToken)
    {
        double? winnerRate = basis.RateOf(r.WinnerAmount);
        MyRank rank = new(MyRankKind.Unknown, null);
        if (r.RanksFetchedAt is not null)
        {
            var amounts = await db.BidResultRanks.AsNoTracking()
                .Where(x => x.BidResultId == r.Id && x.Rank != null && x.Amount != null && x.Amount < myAmount)
                .Select(x => x.Amount!.Value)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            rank = BidResultMath.RankOf(myAmount, amounts, basis.Floor, r.PlannedPrice);
        }
        return new MyBidOutcome(noticeId, r.Status, r.ParticipantCount,
            BidResultMath.ActualAssessmentRate(r.PlannedPrice, basis.BasePrice),
            winnerRate, winnerRate is { } w ? myRate - w : null, rank);
    }

    private static string Cut(string s) => s.Length <= BidResultRank.MaxNameLength ? s : s[..BidResultRank.MaxNameLength];

    private static string HashOf(string raw) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
}
