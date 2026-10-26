using BidNote.Application.Results;
using BidNote.Domain.Bidding;
using BidNote.Domain.Company;
using BidNote.Domain.Notices;
using BidNote.Domain.Results;
using BidNote.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace BidNote.Tests;

public sealed class CompetitorMathTests
{
    [Fact]
    public void 가운데_값과_사분위는_선형_보간한다()
    {
        double[] sorted = [99.0, 99.5, 100.0, 100.5];

        Assert.Equal(99.75, CompetitorMath.Quantile(sorted, 0.5));
        Assert.Equal(99.375, CompetitorMath.Quantile(sorted, 0.25));
        Assert.Null(CompetitorMath.Quantile([], 0.5));
    }

    [Fact]
    public void 분포는_0점1_간격으로_세고_드문_바깥_값은_양끝에_넣는다()
    {
        List<double> rates = [.. Enumerable.Repeat(100.05, 20), .. Enumerable.Repeat(100.15, 10), 100.25, 130.0];

        var bins = CompetitorMath.Histogram(rates);

        Assert.Equal(rates.Count, bins.Sum(b => b.Count));
        Assert.Equal(CompetitorMath.MinBins, bins.Count); // 값이 좁게 모여 있어도 1%p 폭은 보인다 (130% 하나가 범위를 늘리지 않는다)
        Assert.Equal(20, bins.Single(b => Math.Abs(b.From - 100.0) < 1e-9).Count);
        Assert.Equal(1, bins[^1].Count); // 예가 초과 130.0은 마지막 막대에
    }

    [Fact]
    public void 값이_하나뿐이어도_그_주변_1퍼센트포인트를_보여_준다()
    {
        var bins = CompetitorMath.Histogram([100.708]);

        Assert.Equal(CompetitorMath.MinBins, bins.Count);
        Assert.Equal(1, bins.Sum(b => b.Count));
        Assert.Contains(bins, b => b.Count == 1 && b.From <= 100.708 && 100.708 < b.To);
    }
}

public sealed class CompetitorQueriesTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 25, 9, 0, 0);
    private readonly TestDb testDb = new();

    public void Dispose() => testDb.Dispose();

    private sealed class Factory(TestDb db) : IDbContextFactory<BidNoteDbContext>
    {
        public BidNoteDbContext CreateDbContext() => db.NewContext();
    }

    /// <summary>기초금액 1억, 하한율 87.745%, A값 없음인 개찰 완료 공고 하나와 업체별 순위.</summary>
    private int AddOpened(string no, DateTime openedAt, params (string BizNo, string Name, int? Rank, long? Amount)[] bids)
    {
        using var db = testDb.NewContext();
        var n = new Notice
        {
            SourceSystem = SourceSystems.G2B,
            BusinessType = BusinessType.Construction,
            BidNo = no,
            BidSeq = "000",
            Name = "공고 " + no,
            Agency = "○○시",
            BasePrice = 100_000_000,
            LowerLimitRate = 87.745,
            DueAt = openedAt.AddHours(-1),
            OpenAt = openedAt,
            PostedAt = openedAt.AddDays(-7),
            LastModifiedAt = Now,
            FirstCollectedAt = Now,
            LastCheckedAt = Now,
        };
        db.Notices.Add(n);
        db.SaveChanges();
        db.BidResults.Add(new BidResult
        {
            NoticeId = n.Id,
            Status = "개찰완료",
            ParticipantCount = bids.Length,
            OpenedAt = openedAt,
            PlannedPrice = 100_100_000,
            RanksFetchedAt = Now,
            RanksComplete = true,
            UpdatedAt = Now,
            Ranks = [.. bids.Select(b => new BidResultRank { BizNo = b.BizNo, CompanyName = b.Name, Rank = b.Rank, Amount = b.Amount, Remark = b.Rank is null ? "낙찰하한선 미달" : "정상" })],
        });
        db.SaveChanges();
        return n.Id;
    }

    private static long AmountFor(double rate) => BidCalculator.Calculate(new BidInput(100_000_000, rate, 87.745)).BidAmount;

    [Fact]
    public async Task 자주_만난_업체순으로_세고_사정률_범위를_준다()
    {
        AddOpened("N1", Now.AddDays(-3), ("1110000001", "가건설", 1, AmountFor(100.1)), ("2220000002", "나건설", 2, AmountFor(100.3)));
        AddOpened("N2", Now.AddDays(-2), ("1110000001", "가건설", 2, AmountFor(100.2)), ("2220000002", "나건설", null, AmountFor(99.0)));
        AddOpened("N3", Now.AddDays(-1), ("1110000001", "가건설(주)", 1, AmountFor(100.0)), ("3330000003", "다건설", 2, null));

        var overview = await new CompetitorQueries(new Factory(testDb)).OverviewAsync(null, 1, 10, CancellationToken.None);

        Assert.Equal((3, 2), (overview.NoticeCount, overview.CompanyCount)); // 금액 없는 다건설은 세지 않는다
        var ga = overview.Companies[0];
        Assert.Equal(("1110000001", 3, 2), (ga.BizNo, ga.Meetings, ga.Wins));
        Assert.Equal(100.1, ga.MedianRate!.Value, 3);
        Assert.Equal(Now.AddDays(-1), ga.LastMetAt);
        Assert.Equal(["나건설"], (await new CompetitorQueries(new Factory(testDb)).OverviewAsync("나건", 1, 10, CancellationToken.None)).Companies.Select(c => c.Name));
        Assert.Single((await new CompetitorQueries(new Factory(testDb)).OverviewAsync(null, 3, 10, CancellationToken.None)).Companies);
    }

    [Fact]
    public async Task 공고_하나의_참가_업체_중_자주_만난_경쟁사를_세고_우리_회사는_뺀다()
    {
        AddOpened("N1", Now.AddDays(-3), ("1110000001", "가건설", 1, AmountFor(100.1)), ("2220000002", "나건설", 2, AmountFor(100.3)), ("9990000009", "우리건설", 3, AmountFor(100.4)));
        AddOpened("N2", Now.AddDays(-2), ("1110000001", "가건설", 2, AmountFor(100.2)), ("9990000009", "우리건설", 1, AmountFor(100.0)));
        int n3 = AddOpened("N3", Now.AddDays(-1), ("1110000001", "가건설", 2, AmountFor(100.0)), ("2220000002", "나건설", 1, AmountFor(99.9)),
            ("3330000003", "다건설", 3, AmountFor(100.5)), ("9990000009", "우리건설", null, AmountFor(98.0)));
        using (var db = testDb.NewContext())
        {
            db.CompanyProfiles.Add(new CompanyProfile { BizNo = "999-00-00009", UpdatedAt = Now });
            db.SaveChanges();
        }
        var queries = new CompetitorQueries(new Factory(testDb));

        var result = await queries.ForNoticeAsync(n3, 2, 10, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(2, result.FrequentCount); // 가건설 3번, 나건설 2번 (다건설 1번, 우리 회사는 뺀다)
        Assert.Equal(["가건설", "나건설"], result.Companies.Select(c => c.Summary.Name));
        Assert.Equal((2, 100.0), (result.Companies[0].ThisRank!.Value, Math.Round(result.Companies[0].ThisRate!.Value, 3)));
        Assert.Equal(1, (await queries.ForNoticeAsync(n3, 3, 10, CancellationToken.None))!.FrequentCount);
        Assert.Null(await queries.ForNoticeAsync(99999, 2, 10, CancellationToken.None)); // 순위를 받지 않은 공고
    }

    [Fact]
    public async Task 업체_상세는_공고별_투찰과_실제_사정률_내_사정률을_준다()
    {
        int n1 = AddOpened("N1", Now.AddDays(-3), ("1110000001", "가건설", 1, AmountFor(100.1)));
        AddOpened("N2", Now.AddDays(-1), ("1110000001", "가건설", null, AmountFor(98.5)));
        using (var db = testDb.NewContext())
        {
            db.MyBids.Add(new MyBid { NoticeId = n1, AssessmentRate = 100.05, BidAmount = AmountFor(100.05), CreatedAt = Now, UpdatedAt = Now });
            db.SaveChanges();
        }

        var d = await new CompetitorQueries(new Factory(testDb)).GetAsync("1110000001", CancellationToken.None);

        Assert.NotNull(d);
        Assert.Equal(["N2", "N1"], d.Bids.Select(b => b.NoticeName[3..])); // 최근 개찰순
        var first = d.Bids.Single(b => b.NoticeId == n1);
        Assert.Equal(100.1, first.AssessmentRate!.Value, 3);
        Assert.Equal(100.1, first.ActualRate!.Value, 6);
        Assert.Equal(100.05, first.MyRate);
        Assert.Equal(2, d.Histogram.Sum(b => b.Count));
        Assert.Null(await new CompetitorQueries(new Factory(testDb)).GetAsync("9999999999", CancellationToken.None));
    }
}

public sealed class CompetitorRankFetchTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 25, 9, 0, 0);
    private readonly TestDb testDb = new();

    public void Dispose() => testDb.Dispose();

    private sealed class Factory(TestDb db) : IDbContextFactory<BidNoteDbContext>
    {
        public BidNoteDbContext CreateDbContext() => db.NewContext();
    }

    private sealed class Source : BidNote.Application.Results.IBidResultSource
    {
        public List<string> Calls { get; } = [];
        public string SourceSystem => SourceSystems.G2B;
        public Task<IReadOnlyList<CollectedOpening>> CollectOpeningsAsync(DateTime from, DateTime to, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CollectedOpening>>([]);
        public Task<CollectedRanking?> FetchRankingAsync(string bidNo, string bidSeq, string classNo, string rebidNo, CancellationToken cancellationToken)
        {
            Calls.Add(bidNo);
            return Task.FromResult<CollectedRanking?>(new CollectedRanking(null, null, [new CollectedRank(1, "가건설", "1110000001", 88_000_000, null, "정상")], true));
        }
    }

    private void AddOpened(string no, string license, DateTime openedAt)
    {
        using var db = testDb.NewContext();
        var n = new Notice
        {
            SourceSystem = SourceSystems.G2B,
            BusinessType = BusinessType.Construction,
            BidNo = no,
            BidSeq = "000",
            Name = "공고 " + no,
            Agency = "○○시",
            DueAt = openedAt.AddHours(-1),
            OpenAt = openedAt,
            PostedAt = openedAt.AddDays(-7),
            LastModifiedAt = Now,
            FirstCollectedAt = Now,
            LastCheckedAt = Now,
            Licenses = [new NoticeLicense { GroupNo = 1, Name = license, Alternatives = "" }],
        };
        db.Notices.Add(n);
        db.SaveChanges();
        db.BidResults.Add(new BidResult { NoticeId = n.Id, Status = "개찰완료", ParticipantCount = 10, OpenedAt = openedAt, UpdatedAt = Now });
        db.SaveChanges();
    }

    [Fact]
    public async Task 경쟁사용_순위는_내_면허로_참여할_수_있었던_최근_공고만_최근순으로_받는다()
    {
        AddOpened("OLD", "전기공사업", Now.AddDays(-40));
        AddOpened("OTHER", "토목공사업", Now.AddDays(-1));
        AddOpened("MINE-1", "전기공사업", Now.AddDays(-3));
        AddOpened("MINE-2", "전기공사업", Now.AddDays(-2));
        var source = new Source();
        var service = new BidResultService(new Factory(testDb), source, new TestClock(Now), NullLogger<BidResultService>.Instance);

        Assert.Equal(0, await service.FetchPendingRanksAsync(10, 1, CancellationToken.None)); // 면허를 아직 안 넣음

        using (var db = testDb.NewContext())
        {
            db.CompanyLicenses.Add(new CompanyLicense { Name = "전기공사업", AddedAt = Now });
            db.SaveChanges();
        }
        Assert.Equal(1, await service.FetchPendingRanksAsync(10, 1, CancellationToken.None));
        Assert.Equal(1, await service.FetchPendingRanksAsync(10, 1, CancellationToken.None));
        Assert.Equal(0, await service.FetchPendingRanksAsync(10, 1, CancellationToken.None));
        Assert.Equal(["MINE-2", "MINE-1"], source.Calls);
    }
}
