using System.Text.Json;
using BidNote.Application.Company;
using BidNote.Application.Home;
using BidNote.Domain.Collecting;
using BidNote.Domain.Company;
using BidNote.Domain.Notices;
using BidNote.Infrastructure.G2B;
using BidNote.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace BidNote.Tests;

public sealed class LicenseEligibilityTests
{
    private static LicenseRow Row(int group, string name, params string[] alternatives) => new(group, name, alternatives);

    private static Eligibility Check(IEnumerable<LicenseRow> rows, params string[] mine) =>
        LicenseEligibility.Check(rows, mine.ToHashSet(StringComparer.Ordinal));

    [Fact]
    public void 그룹끼리는_하나만_갖추면_된다()
    {
        LicenseRow[] rows = [Row(1, "토목건축공사업"), Row(2, "건축공사업")];

        var byBuilding = Check(rows, "건축공사업");
        Assert.Equal((EligibilityKind.Eligible, 2), (byBuilding.Kind, byBuilding.GroupNo));
        Assert.Empty(byBuilding.Missing);
        Assert.Equal(EligibilityKind.Eligible, Check(rows, "토목건축공사업").Kind);
    }

    [Fact]
    public void 그룹_안의_면허는_모두_갖춰야_한다()
    {
        LicenseRow[] rows = [Row(1, "일반소방시설공사업(전기)"), Row(1, "일반소방시설공사업(기계)")];

        var partial = Check(rows, "일반소방시설공사업(전기)");

        Assert.Equal(EligibilityKind.NotEligible, partial.Kind);
        Assert.Equal(["일반소방시설공사업(기계)"], partial.Missing);
        Assert.Equal(EligibilityKind.Eligible, Check(rows, "일반소방시설공사업(전기)", "일반소방시설공사업(기계)").Kind);
    }

    [Fact]
    public void 대신_인정하는_업종이_있으면_그_줄을_충족한다()
    {
        LicenseRow[] rows =
        [
            Row(1, "전문소방시설공사업"),
            Row(2, "일반소방시설공사업(전기)", "전문소방시설공사업"),
            Row(2, "일반소방시설공사업(기계)", "전문소방시설공사업"),
        ];

        Assert.Equal(EligibilityKind.Eligible, Check([rows[1], rows[2]], "전문소방시설공사업").Kind);
    }

    [Fact]
    public void 빠진_면허는_가장_가까운_그룹_기준으로_알려_준다()
    {
        LicenseRow[] rows = [Row(1, "토목공사업"), Row(1, "포장공사업"), Row(1, "상하수도공사업"), Row(2, "조경공사업"), Row(2, "포장공사업")];

        var result = Check(rows, "포장공사업");

        Assert.Equal((EligibilityKind.NotEligible, 2), (result.Kind, result.GroupNo));
        Assert.Equal(["조경공사업"], result.Missing);
    }

    [Fact]
    public void 면허_제한이_없으면_판단하지_않는다()
    {
        Assert.Equal(EligibilityKind.Unknown, Check([], "토목공사업").Kind);
    }
}

public sealed class LicenseParsingTests
{
    [Fact]
    public void 허용_업종의_대괄호와_코드를_떼고_여러_개도_읽는다()
    {
        Assert.Equal(["토목건축공사업"], G2BCollector.LicenseList("[토목건축공사업/0003]"));
        Assert.Equal(["토목건축공사업", "전문소방시설공사업"], G2BCollector.LicenseList("[토목건축공사업/0003], [전문소방시설공사업/0040]"));
        Assert.Empty(G2BCollector.LicenseList(""));
    }

    [Fact]
    public void 면허_제한_원문_한_줄을_읽는다()
    {
        using var doc = JsonDocument.Parse("""
            {"lmtGrpNo":"2","lmtSno":"1","lcnsLmtNm":"건축공사업/0002","permsnIndstrytyList":"[토목건축공사업/0003]"}
            """);

        var l = G2BCollector.MapLicense(doc.RootElement);

        Assert.Equal(2, l.GroupNo);
        Assert.Equal("건축공사업", l.Name);
        Assert.Equal(["토목건축공사업"], l.Alternatives!);
    }
}

public sealed class CompanyStoreTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 25, 9, 0, 0);
    private readonly TestDb testDb = new();
    private readonly CompanyStore store;

    public CompanyStoreTests() => store = new CompanyStore(new Factory(testDb), new TestClock(Now));

    public void Dispose() => testDb.Dispose();

    private sealed class Factory(TestDb db) : IDbContextFactory<BidNoteDbContext>
    {
        public BidNoteDbContext CreateDbContext() => db.NewContext();
    }

    private int AddNotice(string no, bool open = true, params (int Group, string Name, string Alternatives)[] licenses)
    {
        using var db = testDb.NewContext();
        var n = new Notice
        {
            BidNo = no,
            BidSeq = "000",
            Name = "공고 " + no,
            Agency = "○○시",
            DueAt = open ? Now.AddDays(2) : Now.AddDays(-1),
            OpenAt = Now.AddDays(3),
            PostedAt = Now.AddDays(-1),
            LastModifiedAt = Now,
            FirstCollectedAt = Now,
            LastCheckedAt = Now,
            Licenses = [.. licenses.Select(l => new NoticeLicense { GroupNo = l.Group, Name = l.Name, Alternatives = l.Alternatives })],
        };
        db.Notices.Add(n);
        db.SaveChanges();
        return n.Id;
    }

    [Fact]
    public async Task 회사_정보와_면허를_저장하고_같은_면허는_한_번만_담는다()
    {
        await store.SaveProfileAsync(new CompanyProfileInput("  ○○건설  ", "123-45-67890", "대전", "a@b.kr"), CancellationToken.None);
        Assert.True(await store.AddLicenseAsync(" 토목공사업 ", CancellationToken.None));
        Assert.False(await store.AddLicenseAsync("토목공사업", CancellationToken.None));
        Assert.False(await store.AddLicenseAsync("  ", CancellationToken.None));
        Assert.True(await store.AddLicenseAsync("건축공사업", CancellationToken.None));

        var info = await store.GetAsync(CancellationToken.None);

        Assert.Equal(new CompanyInfo("○○건설", "123-45-67890", "대전", "a@b.kr", info.Licenses), info);
        Assert.Equal(["건축공사업", "토목공사업"], info.LicenseNames);

        await store.RemoveLicenseAsync("토목공사업", CancellationToken.None);
        Assert.Equal(["건축공사업"], (await store.GetAsync(CancellationToken.None)).LicenseNames);
    }

    [Fact]
    public async Task 진행_공고를_내_면허로_나눠_세고_공고_하나도_판정한다()
    {
        int civil = AddNotice("CIVIL", true, (1, "토목공사업", "토목건축공사업"));
        int pair = AddNotice("PAIR", true, (1, "토목공사업", ""), (1, "포장공사업", ""));
        AddNotice("NONE", true);
        AddNotice("CLOSED", false, (1, "토목공사업", ""));

        Assert.Null(await store.CheckAsync(civil, CancellationToken.None));
        Assert.Equal(new EligibilitySummary(0, 0, 0), await store.SummaryAsync(Now, CancellationToken.None));

        await store.AddLicenseAsync("토목건축공사업", CancellationToken.None);

        Assert.Equal(new EligibilitySummary(1, 1, 1), await store.SummaryAsync(Now, CancellationToken.None));
        Assert.Equal(EligibilityKind.Eligible, (await store.CheckAsync(civil, CancellationToken.None))!.Kind);
        var p = await store.CheckAsync(pair, CancellationToken.None);
        Assert.Equal(["토목공사업", "포장공사업"], p!.Missing);
    }

    [Fact]
    public async Task 면허_고르기_목록은_진행_공고에_나온_면허와_허용_업종을_센다()
    {
        AddNotice("A", true, (1, "건축공사업", "토목건축공사업"));
        AddNotice("B", true, (1, "건축공사업", ""), (2, "토목건축공사업", ""));
        AddNotice("CLOSED", false, (1, "전기공사업", ""));

        var options = await store.LicenseOptionsAsync(Now, CancellationToken.None);

        Assert.Equal([new LicenseOption("건축공사업", 2), new LicenseOption("토목건축공사업", 2)], options);
    }

    [Fact]
    public async Task 목록에서_내_면허로_참여_가능한_공고만_거른다()
    {
        AddNotice("OK", true, (1, "전기공사업", ""));
        AddNotice("NO", true, (1, "정보통신공사업", ""));
        AddNotice("UNKNOWN", true);
        await store.AddLicenseAsync("전기공사업", CancellationToken.None);

        using var ctx = testDb.NewContext();
        var result = await new NoticeRepository(ctx).SearchAsync(new NoticeFilter { MyLicensesOnly = true }, Now, CancellationToken.None);

        Assert.Equal(["OK"], result.Items.Select(x => x.BidNo));
    }

    [Fact]
    public async Task 시작할_때_허용_업종을_저장된_원문에서_한_번_채운다()
    {
        int id = AddNotice("R1", true, (1, "토목건축공사업", null!), (2, "건축공사업", null!));
        using (var db = testDb.NewContext())
        {
            db.ApiRawResponses.Add(new ApiRawResponse
            {
                SourceSystem = SourceSystems.G2B,
                ApiName = G2BCollector.ListApi,
                BidNo = "R1",
                ResponseJson = """
                    {"notice":{},"licenses":[
                      {"lmtGrpNo":"1","lcnsLmtNm":"토목건축공사업/0003","permsnIndstrytyList":""},
                      {"lmtGrpNo":"2","lcnsLmtNm":"건축공사업/0002","permsnIndstrytyList":"[토목건축공사업/0003]"}]}
                    """,
                CollectedAt = Now,
            });
            db.SaveChanges();
        }

        using (var db = testDb.NewContext())
            Assert.Equal(1, await DatabaseInitializer.FillLicenseAlternativesAsync(db, NullLogger.Instance, CancellationToken.None));
        using (var db = testDb.NewContext())
            Assert.Equal(0, await DatabaseInitializer.FillLicenseAlternativesAsync(db, NullLogger.Instance, CancellationToken.None));

        using var check = testDb.NewContext();
        var rows = await check.NoticeLicenses.Where(x => x.NoticeId == id).OrderBy(x => x.GroupNo).ToListAsync();
        Assert.Equal(["", "토목건축공사업"], rows.Select(x => x.Alternatives));
    }

    [Fact]
    public void 저장한_검색_조건에_내_면허_조건도_남는다()
    {
        Assert.Equal("new=true&mine=true", SearchQuery.Normalize("mine=true&new=true&page=2"));
        Assert.Equal(["내 면허"], SearchQuery.Describe("mine=true"));
    }
}
