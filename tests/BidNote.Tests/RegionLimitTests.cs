using System.Text.Json;
using BidNote.Application.Collecting;
using BidNote.Application.Company;
using BidNote.Application.Home;
using BidNote.Domain.Company;
using BidNote.Domain.Notices;
using BidNote.Infrastructure.G2B;
using BidNote.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace BidNote.Tests;

public sealed class RegionEligibilityTests
{
    private static RegionVerdict Check(string[] notice, params string[] mine) => RegionEligibility.Check(notice, mine);

    [Fact]
    public void 공고_지역_중_한_곳에_소재지가_있으면_참여_가능()
    {
        string[] limits = ["충청북도 충주시", "충청북도 제천시", "충청북도 단양군"];

        Assert.Equal(RegionVerdict.Within, Check(limits, "충청북도 제천시"));
        Assert.Equal(RegionVerdict.Outside, Check(limits, "충청북도 청주시"));
        Assert.Equal(RegionVerdict.Within, Check(limits, "대전광역시 서구", "충청북도 단양군")); // 지사
    }

    [Fact]
    public void 시도로_제한하면_그_시도_안의_시군구도_된다()
    {
        Assert.Equal(RegionVerdict.Within, Check(["충청북도"], "충청북도 충주시"));
        Assert.Equal(RegionVerdict.Within, Check(["충청북도"], "충청북도"));
        Assert.Equal(RegionVerdict.Outside, Check(["충청북도"], "충청남도 아산시"));
    }

    [Fact]
    public void 시도만_넣었는데_공고가_시군구로_제한하면_판단하지_않는다()
    {
        Assert.Equal(RegionVerdict.Unknown, Check(["충청북도 충주시"], "충청북도"));
        Assert.Equal(RegionVerdict.Outside, Check(["충청북도 충주시"], "경기도"));
    }

    [Fact]
    public void 제한이_없거나_소재지를_안_넣었으면()
    {
        Assert.Equal(RegionVerdict.NoLimit, Check([], "충청북도 충주시"));
        Assert.Equal(RegionVerdict.Unknown, Check(["충청북도"]));
    }

    [Fact]
    public void 시도의_옛_이름과_새_이름을_같게_본다()
    {
        Assert.Equal(RegionVerdict.Within, Check(["강원특별자치도 원주시"], "강원도 원주시"));
        Assert.Equal(RegionVerdict.Within, Check(["전북특별자치도"], "전라북도 전주시"));
    }

    [Fact]
    public void 지역_밖이면_참여_가능에서_빠지고_모르면_빠지지_않는다()
    {
        var eligible = new Eligibility(EligibilityKind.Eligible, 1, []);

        Assert.False((eligible with { Region = RegionVerdict.Outside }).CanBid);
        Assert.True((eligible with { Region = RegionVerdict.Unknown }).CanBid);
        Assert.True((eligible with { Region = RegionVerdict.NoLimit }).CanBid);
    }
}

public sealed class G2BRegionLimitSourceTests
{
    [Fact]
    public void 공사만_골라_공고번호_차수로_묶는다()
    {
        using var doc = JsonDocument.Parse("""
            [{"bidNtceNo":"R1","bidNtceOrd":"000","lmtSno":"2","prtcptPsblRgnNm":"충청북도 제천시","bsnsDivNm":"공사"},
             {"bidNtceNo":"R1","bidNtceOrd":"000","lmtSno":"1","prtcptPsblRgnNm":"충청북도 충주시","bsnsDivNm":"공사"},
             {"bidNtceNo":"R2","bidNtceOrd":"000","lmtSno":"1","prtcptPsblRgnNm":"대전광역시","bsnsDivNm":"물품"},
             {"bidNtceNo":"R1","bidNtceOrd":"001","lmtSno":"1","prtcptPsblRgnNm":"충청북도","bsnsDivNm":"공사"}]
            """);

        var groups = G2BRegionLimitSource.Group(doc.RootElement.EnumerateArray(), "inqryDiv=1");

        Assert.Equal(2, groups.Count);
        Assert.Equal(["충청북도 충주시", "충청북도 제천시"], groups.Single(g => g.BidSeq == "000").Regions);
        Assert.Equal(["충청북도"], groups.Single(g => g.BidSeq == "001").Regions);
    }
}

public sealed class RegionLimitServiceTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 25, 9, 0, 0);
    private readonly TestDb testDb = new();
    private readonly StubSource source = new();
    private readonly RegionLimitService service;

    public RegionLimitServiceTests() =>
        service = new RegionLimitService(new Factory(testDb), source, new TestClock(Now), NullLogger<RegionLimitService>.Instance);

    public void Dispose() => testDb.Dispose();

    private sealed class Factory(TestDb db) : IDbContextFactory<BidNoteDbContext>
    {
        public BidNoteDbContext CreateDbContext() => db.NewContext();
    }

    private sealed class StubSource : IRegionLimitSource
    {
        public List<CollectedRegionLimit> Rows { get; } = [];
        public string SourceSystem => SourceSystems.G2B;
        public Task<IReadOnlyList<CollectedRegionLimit>> CollectAsync(DateTime from, DateTime to, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CollectedRegionLimit>>([.. Rows]);
    }

    private int AddNotice(string no, string seq = "000")
    {
        using var db = testDb.NewContext();
        var n = new Notice
        {
            SourceSystem = SourceSystems.G2B,
            BusinessType = BusinessType.Construction,
            BidNo = no,
            BidSeq = seq,
            Name = "공고 " + no,
            Agency = "○○시",
            DueAt = Now.AddDays(2),
            OpenAt = Now.AddDays(3),
            PostedAt = Now.AddDays(-1),
            LastModifiedAt = Now,
            FirstCollectedAt = Now,
            LastCheckedAt = Now,
        };
        db.Notices.Add(n);
        db.SaveChanges();
        return n.Id;
    }

    private static CollectedRegionLimit Row(string no, string seq, params string[] regions) =>
        new(no, seq, regions, JsonSerializer.Serialize(new { no, seq, regions }), G2BRegionLimitSource.RegionApi, "");

    private List<string> RegionsOf(int id)
    {
        using var db = testDb.NewContext();
        return [.. db.NoticeRegionLimits.Where(x => x.NoticeId == id).OrderBy(x => x.Id).Select(x => x.Name)];
    }

    [Fact]
    public async Task 저장된_공고에만_붙이고_같은_원문은_다시_저장하지_않는다()
    {
        int id = AddNotice("R1");
        source.Rows.AddRange([Row("R1", "000", "충청북도 충주시", "충청북도 제천시"), Row("NOT-SAVED", "000", "대전광역시")]);

        Assert.False(await service.HasAnyAsync(CancellationToken.None));
        Assert.Equal(1, await service.CollectAsync(Now.AddDays(-1), Now, CancellationToken.None));
        Assert.Equal(0, await service.CollectAsync(Now.AddDays(-1), Now, CancellationToken.None));

        Assert.True(await service.HasAnyAsync(CancellationToken.None));
        Assert.Equal(["충청북도 충주시", "충청북도 제천시"], RegionsOf(id));
        using var db = testDb.NewContext();
        Assert.Equal(1, await db.ApiRawResponses.CountAsync());
        Assert.Equal("", (await db.Notices.SingleAsync()).RawHash); // 공고 원문 Hash(정정 이력)는 건드리지 않는다
    }

    [Fact]
    public async Task 정정_차수의_지역으로_바꾸고_낮은_차수는_무시한다()
    {
        int id = AddNotice("R1", "001");
        source.Rows.AddRange([Row("R1", "000", "충청북도 충주시"), Row("R1", "001", "충청북도")]);
        await service.CollectAsync(Now.AddDays(-1), Now, CancellationToken.None);
        Assert.Equal(["충청북도"], RegionsOf(id));

        source.Rows.Clear();
        source.Rows.Add(Row("R1", "000", "충청북도 충주시"));
        await service.CollectAsync(Now.AddDays(-1), Now, CancellationToken.None);
        Assert.Equal(["충청북도"], RegionsOf(id));
    }

    [Fact]
    public async Task 소재지를_넣으면_지역_밖_공고를_따로_세고_목록에서_뺀다()
    {
        var store = new CompanyStore(new Factory(testDb), new TestClock(Now));
        int inside = AddNotice("IN");
        int outside = AddNotice("OUT");
        int free = AddNotice("FREE");
        using (var db = testDb.NewContext())
        {
            foreach (int id in new[] { inside, outside, free })
                db.NoticeLicenses.Add(new NoticeLicense { NoticeId = id, GroupNo = 1, Name = "토목공사업", Alternatives = "" });
            db.SaveChanges();
        }
        source.Rows.AddRange([Row("IN", "000", "충청북도 충주시"), Row("OUT", "000", "경상남도 진주시")]);
        await service.CollectAsync(Now.AddDays(-1), Now, CancellationToken.None);
        await store.AddLicenseAsync("토목공사업", CancellationToken.None);

        // 소재지를 안 넣었으면 지역은 모름 → 빼지 않는다
        Assert.Equal(new EligibilitySummary(3, 0, 0, 0, 0, 2), await store.SummaryAsync(Now, CancellationToken.None));

        Assert.True(await store.AddLocationAsync("  충청북도   충주시 ", true, CancellationToken.None));
        Assert.False(await store.AddLocationAsync("충청북도 충주시", false, CancellationToken.None));
        Assert.True(await store.AddLocationAsync("대전광역시 서구", true, CancellationToken.None));
        Assert.Equal([new CompanyLocationInfo("대전광역시 서구", true), new CompanyLocationInfo("충청북도 충주시", false)],
            (await store.GetAsync(CancellationToken.None)).Locations);

        Assert.Equal(new EligibilitySummary(2, 0, 0, 0, 1, 0), await store.SummaryAsync(Now, CancellationToken.None));
        Assert.Equal(RegionVerdict.Outside, (await store.CheckAsync(outside, CancellationToken.None))!.Region);
        using var ctx = testDb.NewContext();
        var list = await new NoticeRepository(ctx).SearchAsync(new NoticeFilter { MyLicensesOnly = true }, Now, CancellationToken.None);
        Assert.Equal(["FREE", "IN"], list.Items.Select(x => x.BidNo).Order());
        Assert.Equal([new LocationOption("경상남도 진주시", 1), new LocationOption("충청북도 충주시", 1)], await store.LocationOptionsAsync(Now, CancellationToken.None));
    }
}
