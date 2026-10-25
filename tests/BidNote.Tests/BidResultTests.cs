using System.Text.Json;
using BidNote.Application.Results;
using BidNote.Domain.Bidding;
using BidNote.Domain.Favorites;
using BidNote.Domain.Notices;
using BidNote.Domain.Results;
using BidNote.Infrastructure.G2B;
using BidNote.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace BidNote.Tests;

public sealed class BidResultMathTests
{
    [Fact]
    public void 실제_사정률은_예정가격을_기초금액으로_나눈_값이다()
    {
        Assert.Equal(100.0195, BidResultMath.ActualAssessmentRate(232_673_400, 232_628_000)!.Value, 4);
        Assert.Null(BidResultMath.ActualAssessmentRate(null, 232_628_000));
    }

    [Fact]
    public void 낙찰하한_금액은_A값을_빼고_하한율을_곱한_뒤_더해_올린다()
    {
        Assert.Equal(87_745_001, BidResultMath.LowerLimitAmount(100_000_001, 87.745, 0)); // 87,745,000.877… → 올림
        Assert.Equal(88_357_750, BidResultMath.LowerLimitAmount(100_000_000, 87.745, 5_000_000)); // 95,000,000 × 87.745% + 5,000,000
        Assert.Null(BidResultMath.LowerLimitAmount(100_000_000, 0, 0));
    }

    [Fact]
    public void 투찰금액을_사정률로_되돌리면_계산기와_맞는다()
    {
        var calc = BidCalculator.Calculate(new BidInput(100_000_000, 99.5, 87.745, 3_000_000));
        Assert.Equal(99.5, BidResultMath.AssessmentRateOf(calc.BidAmount, 100_000_000, 87.745, 3_000_000)!.Value, 4);
    }

    [Fact]
    public void 내_순위는_순위_있는_업체_중_나보다_낮은_곳의_수에_1을_더한다()
    {
        long[] ranked = [100, 110, 120, 130];

        Assert.Equal(new MyRank(MyRankKind.Ranked, 3), BidResultMath.RankOf(115, ranked, 95, 200));
        Assert.Equal(new MyRank(MyRankKind.Ranked, 1), BidResultMath.RankOf(99, ranked, 95, 200));
        Assert.Equal(MyRankKind.BelowFloor, BidResultMath.RankOf(90, ranked, 95, 200).Kind);
        Assert.Equal(MyRankKind.OverPlanned, BidResultMath.RankOf(201, ranked, 95, 200).Kind);
    }
}

public sealed class G2BResultSourceTests
{
    [Fact]
    public void 개찰_목록의_1순위_칸을_나눠_읽는다()
    {
        using var doc = JsonDocument.Parse("""
            {"bidNtceNo":"R26BK01766907","bidNtceOrd":"000","bidClsfcNo":"0","rbidNo":"000","prtcptCnum":"3548",
             "progrsDivCdNm":"개찰완료","opengDt":"2026-10-23 11:00:00",
             "opengCorpInfo":"신흥전기공사^1321362297^김용주^209456310^90.021"}
            """);

        var o = G2BResultSource.MapOpening(doc.RootElement, "inqryDiv=1");

        Assert.Equal("R26BK01766907", o.BidNo);
        Assert.Equal(3548, o.ParticipantCount);
        Assert.Equal(new DateTime(2026, 10, 23, 11, 0, 0), o.OpenedAt);
        Assert.Equal("신흥전기공사", o.WinnerName);
        Assert.Equal(209_456_310, o.WinnerAmount);
        Assert.Equal(90.021, o.WinnerBidRate);
    }

    [Fact]
    public void 순위가_없거나_금액이_빈_업체도_읽는다()
    {
        using var doc = JsonDocument.Parse("""
            [{"opengRank":"1","prcbdrNm":"신흥전기공사","prcbdrBizno":"1321362297","bidprcAmt":"209456310","bidprcrt":"90.021","rmrk":"정상"},
             {"opengRank":"","prcbdrNm":"주식회사 에스유아이씨티","bidprcAmt":"","bidprcrt":"","rmrk":"전자입찰취소신청"}]
            """);

        var rows = doc.RootElement.EnumerateArray().Select(G2BResultSource.MapRank).ToList();

        Assert.Equal(new CollectedRank(1, "신흥전기공사", "1321362297", 209_456_310, 90.021, "정상"), rows[0]);
        Assert.Null(rows[1].Rank);
        Assert.Null(rows[1].Amount);
        Assert.Equal("전자입찰취소신청", rows[1].Remark);
    }
}

public sealed class BidResultServiceTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 25, 9, 0, 0);
    private readonly TestDb testDb = new();
    private readonly StubSource source = new();
    private readonly BidResultService service;

    public BidResultServiceTests() =>
        service = new BidResultService(new Factory(testDb), source, new TestClock(Now), NullLogger<BidResultService>.Instance);

    public void Dispose() => testDb.Dispose();

    private sealed class Factory(TestDb db) : IDbContextFactory<BidNoteDbContext>
    {
        public BidNoteDbContext CreateDbContext() => db.NewContext();
    }

    private sealed class StubSource : IBidResultSource
    {
        public List<CollectedOpening> Openings { get; } = [];
        public Dictionary<string, CollectedRanking> Rankings { get; } = [];
        public List<string> RankCalls { get; } = [];
        public string SourceSystem => SourceSystems.G2B;

        public Task<IReadOnlyList<CollectedOpening>> CollectOpeningsAsync(DateTime from, DateTime to, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CollectedOpening>>([.. Openings]);

        public Task<CollectedRanking?> FetchRankingAsync(string bidNo, string bidSeq, string classNo, string rebidNo, CancellationToken cancellationToken)
        {
            RankCalls.Add($"{bidNo}-{bidSeq}-{classNo}-{rebidNo}");
            return Task.FromResult(Rankings.TryGetValue(bidNo, out var r) ? r : null);
        }
    }

    private int AddNotice(string no, long basePrice = 100_000_000, long? aValue = null)
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
            BasePrice = basePrice,
            AValue = aValue,
            LowerLimitRate = 87.745,
            DueAt = Now.AddDays(-2),
            OpenAt = Now.AddDays(-2).AddHours(1),
            PostedAt = Now.AddDays(-9),
            LastModifiedAt = Now,
            FirstCollectedAt = Now,
            LastCheckedAt = Now,
        };
        db.Notices.Add(n);
        db.SaveChanges();
        return n.Id;
    }

    private static CollectedOpening Opening(string no, string rebid = "000", int participants = 3, long winner = 88_000_000, string raw = "") => new()
    {
        BidNo = no,
        BidSeq = "000",
        ClassNo = "0",
        RebidNo = rebid,
        Status = "개찰완료",
        ParticipantCount = participants,
        OpenedAt = Now.AddDays(-2).AddHours(1),
        WinnerName = "1순위 건설",
        WinnerAmount = winner,
        WinnerBidRate = 87.8,
        RawJson = raw.Length > 0 ? raw : $"{{\"no\":\"{no}\",\"rbid\":\"{rebid}\",\"w\":{winner}}}",
    };

    private static CollectedRanking Ranking(params long[] amounts) => new(
        100_000_000, 100_000_000,
        [.. amounts.Select((a, i) => new CollectedRank(i + 1, $"업체{i + 1}", "", a, null, "정상")),
            new CollectedRank(null, "미달 업체", "", 80_000_000, null, "낙찰하한선 미달")],
        true);

    [Fact]
    public async Task 저장된_공고에만_결과를_붙이고_같은_원문은_다시_저장하지_않는다()
    {
        int id = AddNotice("R1");
        source.Openings.AddRange([Opening("R1"), Opening("NOT-SAVED")]);

        Assert.Equal(1, await service.CollectOpeningsAsync(Now.AddDays(-3), Now, CancellationToken.None));
        Assert.Equal(0, await service.CollectOpeningsAsync(Now.AddDays(-3), Now, CancellationToken.None));

        var view = await service.GetAsync(id, 10, CancellationToken.None);
        Assert.NotNull(view);
        Assert.Equal("1순위 건설", view.Winner!.CompanyName);
        Assert.True(view.NeedsRanks);
        using var db = testDb.NewContext();
        Assert.Equal(1, await db.ApiRawResponses.CountAsync());
    }

    [Fact]
    public async Task 재입찰이면_마지막_차례를_남기고_예전_순위는_버린다()
    {
        int id = AddNotice("R1");
        source.Openings.Add(Opening("R1"));
        source.Rankings["R1"] = Ranking(88_000_000, 89_000_000);
        await service.CollectOpeningsAsync(Now.AddDays(-3), Now, CancellationToken.None);
        await service.FetchRanksAsync(id, CancellationToken.None);

        source.Openings.Clear();
        source.Openings.AddRange([Opening("R1", "000"), Opening("R1", "001", winner: 87_900_000)]);
        await service.CollectOpeningsAsync(Now.AddDays(-3), Now, CancellationToken.None);

        var view = await service.GetAsync(id, 10, CancellationToken.None);
        Assert.Equal(87_900_000, view!.Winner!.Amount);
        Assert.Null(view.RanksFetchedAt);
        Assert.Empty(view.Top);
        using var db = testDb.NewContext();
        Assert.Equal(0, await db.BidResultRanks.CountAsync());
    }

    [Fact]
    public async Task 순위를_받으면_예정가격과_실제_사정률과_상위_순위를_보여_준다()
    {
        int id = AddNotice("R1");
        source.Openings.Add(Opening("R1"));
        source.Rankings["R1"] = Ranking(88_000_000, 88_100_000, 88_200_000);
        await service.CollectOpeningsAsync(Now.AddDays(-3), Now, CancellationToken.None);

        Assert.True(await service.FetchRanksAsync(id, CancellationToken.None));
        var view = await service.GetAsync(id, 2, CancellationToken.None);

        Assert.Equal(["R1-000-0-000"], source.RankCalls);
        Assert.Equal(100_000_000, view!.PlannedPrice);
        Assert.Equal(100.0, view.ActualRate);
        Assert.Equal(87_745_000, view.LowerLimitAmount);
        Assert.Equal([1, 2], view.Top.Select(x => x.Rank));
        Assert.Equal(3, view.RankedCount);
        Assert.Equal(4, view.TotalCount);
        Assert.False(view.NeedsRanks);
    }

    [Fact]
    public async Task 내_투찰_기록과_견줘_순위와_사정률_차이를_준다()
    {
        int id = AddNotice("R1");
        source.Openings.Add(Opening("R1", winner: 88_000_000));
        source.Rankings["R1"] = Ranking(88_000_000, 88_100_000, 88_200_000);
        await service.CollectOpeningsAsync(Now.AddDays(-3), Now, CancellationToken.None);
        using (var db = testDb.NewContext())
        {
            db.MyBids.Add(new MyBid { NoticeId = id, AssessmentRate = 100.4, BidAmount = 88_150_000, CreatedAt = Now, UpdatedAt = Now });
            db.SaveChanges();
        }

        var before = (await service.MyBidOutcomesAsync(CancellationToken.None))[id];
        Assert.Equal(MyRankKind.Unknown, before.Rank.Kind);

        await service.FetchRanksAsync(id, CancellationToken.None);
        var after = (await service.MyBidOutcomesAsync(CancellationToken.None))[id];

        Assert.Equal(new MyRank(MyRankKind.Ranked, 3), after.Rank);
        double winnerRate = BidResultMath.AssessmentRateOf(88_000_000, 100_000_000, 87.745, 0)!.Value;
        Assert.Equal(winnerRate, after.WinnerAssessmentRate!.Value, 6);
        Assert.Equal(100.4 - winnerRate, after.RateGap!.Value, 6);
        Assert.Equal(after, (await service.GetAsync(id, 10, CancellationToken.None))!.Mine);
    }

    [Fact]
    public async Task 순위는_내_투찰_기록과_관심_공고만_미리_받는다()
    {
        int mine = AddNotice("MINE");
        int fav = AddNotice("FAV");
        int trashed = AddNotice("TRASHED");
        int other = AddNotice("OTHER");
        int single = AddNotice("NOBODY");
        source.Openings.AddRange([Opening("MINE"), Opening("FAV"), Opening("TRASHED"), Opening("OTHER"), Opening("NOBODY", participants: 0)]);
        foreach (string no in new[] { "MINE", "FAV", "TRASHED", "OTHER", "NOBODY" }) source.Rankings[no] = Ranking(88_000_000);
        await service.CollectOpeningsAsync(Now.AddDays(-3), Now, CancellationToken.None);
        using (var db = testDb.NewContext())
        {
            db.MyBids.Add(new MyBid { NoticeId = mine, AssessmentRate = 100, BidAmount = 88_000_000, CreatedAt = Now, UpdatedAt = Now });
            db.FavoriteNotices.Add(new FavoriteNotice { NoticeId = fav, AddedAt = Now, UpdatedAt = Now });
            db.FavoriteNotices.Add(new FavoriteNotice { NoticeId = trashed, AddedAt = Now, UpdatedAt = Now, TrashedAt = Now });
            db.FavoriteNotices.Add(new FavoriteNotice { NoticeId = single, AddedAt = Now, UpdatedAt = Now });
            db.SaveChanges();
        }

        Assert.Equal(2, await service.FetchPendingRanksAsync(10, 0, CancellationToken.None));
        Assert.Equal(["FAV", "MINE"], source.RankCalls.Select(x => x.Split('-')[0]).Order());
        Assert.Equal(0, await service.FetchPendingRanksAsync(10, 0, CancellationToken.None));
        Assert.NotNull((await service.GetAsync(other, 10, CancellationToken.None))?.Status);
    }

    [Fact]
    public async Task 공고가_지워지면_개찰_결과와_순위도_함께_지워진다()
    {
        int id = AddNotice("R1");
        source.Openings.Add(Opening("R1"));
        source.Rankings["R1"] = Ranking(88_000_000);
        await service.CollectOpeningsAsync(Now.AddDays(-3), Now, CancellationToken.None);
        await service.FetchRanksAsync(id, CancellationToken.None);

        using (var db = testDb.NewContext())
            await db.Notices.Where(x => x.Id == id).ExecuteDeleteAsync();

        using var check = testDb.NewContext();
        Assert.Equal(0, await check.BidResults.CountAsync());
        Assert.Equal(0, await check.BidResultRanks.CountAsync());
    }
}
