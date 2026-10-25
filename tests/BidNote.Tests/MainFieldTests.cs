using BidNote.Domain.Collecting;
using BidNote.Domain.Company;
using BidNote.Domain.Notices;
using BidNote.Infrastructure.G2B;
using BidNote.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace BidNote.Tests;

public sealed class MainFieldTests
{
    [Fact]
    public void 주력분야_원문을_묶음별로_읽고_번호는_버린다()
    {
        Assert.Equal([["포장공사"]], G2BCollector.MainFieldList("[1^포장공사]"));
        Assert.Equal([["토공사", "포장공사"]], G2BCollector.MainFieldList("[1^토공사^포장공사]"));
        Assert.Equal([["도장공사"], ["습식·방수공사"]], G2BCollector.MainFieldList("[1^도장공사],[2^습식·방수공사]"));
        Assert.Empty(G2BCollector.MainFieldList(""));
    }

    [Fact]
    public void 저장할_때_묶음은_세로줄_분야는_캐럿으로_잇는다()
    {
        string joined = NoticeLicense.JoinMainFields([["토공사", "포장공사"], ["도장공사"]]);

        Assert.Equal("토공사^포장공사|도장공사", joined);
        Assert.Equal([["토공사", "포장공사"], ["도장공사"]], NoticeLicense.SplitMainFields(joined));
        Assert.Empty(NoticeLicense.SplitMainFields(""));
    }

    private static MainFieldVerdict Check(LicenseRow[] rows, string license, params string[] fields) =>
        MainFieldEligibility.Check(rows, new Dictionary<string, IReadOnlyCollection<string>> { [license] = fields.ToHashSet() });

    [Fact]
    public void 묶음_하나의_분야를_모두_가지면_맞고_다르면_다름()
    {
        LicenseRow[] rows = [new(1, "지반조성ㆍ포장공사업", [], [["포장공사"], ["토공사"]])];

        Assert.Equal(MainFieldVerdict.Within, Check(rows, "지반조성ㆍ포장공사업", "토공사"));
        Assert.Equal(MainFieldVerdict.Mismatch, Check(rows, "지반조성ㆍ포장공사업", "보링·그라우팅·파일공사"));
        Assert.Equal(MainFieldVerdict.Mismatch, Check([new(1, "지반조성ㆍ포장공사업", [], [["토공사", "포장공사"]])], "지반조성ㆍ포장공사업", "토공사"));
    }

    [Fact]
    public void 주력분야를_안_넣었거나_제한이_없거나_대체_업종으로_충족하면()
    {
        LicenseRow[] rows = [new(1, "지반조성ㆍ포장공사업", [], [["포장공사"]])];

        Assert.Equal(MainFieldVerdict.Unknown, Check(rows, "지반조성ㆍ포장공사업"));
        Assert.Equal(MainFieldVerdict.NoLimit, Check([new(1, "지반조성ㆍ포장공사업", [])], "지반조성ㆍ포장공사업", "포장공사"));
        Assert.Equal(MainFieldVerdict.NoLimit, Check([new(1, "건축공사업", ["토목건축공사업"], [["건축공사"]])], "토목건축공사업", "토목공사"));
    }

    [Fact]
    public void 주력분야가_달라도_참여_가능에서_빼지_않는다()
    {
        Assert.True(new Eligibility(EligibilityKind.Eligible, 1, [], MainField: MainFieldVerdict.Mismatch).CanBid);
    }
}

public sealed class CompanyMainFieldStoreTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 25, 9, 0, 0);
    private readonly TestDb testDb = new();
    private readonly CompanyStore store;

    public CompanyMainFieldStoreTests() => store = new CompanyStore(new Factory(testDb), new TestClock(Now));

    public void Dispose() => testDb.Dispose();

    private sealed class Factory(TestDb db) : IDbContextFactory<BidNoteDbContext>
    {
        public BidNoteDbContext CreateDbContext() => db.NewContext();
    }

    private int AddNotice(string no, string license, string? mainFields)
    {
        using var db = testDb.NewContext();
        var n = new Notice
        {
            SourceSystem = SourceSystems.G2B,
            BidNo = no,
            BidSeq = "000",
            Name = "공고 " + no,
            Agency = "○○시",
            DueAt = Now.AddDays(2),
            OpenAt = Now.AddDays(3),
            PostedAt = Now.AddDays(-1),
            LastModifiedAt = Now,
            FirstCollectedAt = Now,
            LastCheckedAt = Now,
            Licenses = [new NoticeLicense { GroupNo = 1, Name = license, Alternatives = "", MainFields = mainFields }],
        };
        db.Notices.Add(n);
        db.SaveChanges();
        return n.Id;
    }

    [Fact]
    public async Task 면허별_주력분야를_저장하고_판정하며_요약에_참고로_센다()
    {
        int paving = AddNotice("PAVE", "지반조성ㆍ포장공사업", "포장공사");
        int earth = AddNotice("EARTH", "지반조성ㆍ포장공사업", "토공사");
        await store.AddLicenseAsync("지반조성ㆍ포장공사업", CancellationToken.None);

        Assert.Equal(MainFieldVerdict.Unknown, (await store.CheckAsync(paving, CancellationToken.None))!.MainField);

        Assert.True(await store.SaveLicenseMainFieldsAsync("지반조성ㆍ포장공사업", [" 포장공사 ", "", "포장공사"], CancellationToken.None));
        Assert.Equal(["포장공사"], (await store.GetAsync(CancellationToken.None)).Licenses.Single().MainFields);

        Assert.Equal(MainFieldVerdict.Within, (await store.CheckAsync(paving, CancellationToken.None))!.MainField);
        var other = await store.CheckAsync(earth, CancellationToken.None);
        Assert.Equal((MainFieldVerdict.Mismatch, true), (other!.MainField, other.CanBid));
        var summary = await store.SummaryAsync(Now, CancellationToken.None);
        Assert.Equal((2, 1), (summary.Eligible, summary.MainFieldMismatch));

        var options = await store.MainFieldOptionsAsync(Now, CancellationToken.None);
        Assert.Equal(["토공사", "포장공사"], options["지반조성ㆍ포장공사업"].Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task 시작할_때_주력분야도_저장된_원문에서_채운다()
    {
        int id = AddNotice("R1", "지반조성ㆍ포장공사업", null);
        using (var db = testDb.NewContext())
        {
            db.ApiRawResponses.Add(new ApiRawResponse
            {
                SourceSystem = SourceSystems.G2B,
                ApiName = G2BCollector.ListApi,
                BidNo = "R1",
                ResponseJson = """
                    {"notice":{},"licenses":[{"lmtGrpNo":"1","lcnsLmtNm":"지반조성ㆍ포장공사업/4989","permsnIndstrytyList":"","indstrytyMfrcFldList":"[1^토공사^포장공사]"}]}
                    """,
                CollectedAt = Now,
            });
            db.SaveChanges();
        }

        using (var db = testDb.NewContext())
            await DatabaseInitializer.FillLicenseDetailsAsync(db, NullLogger.Instance, CancellationToken.None);

        using var check = testDb.NewContext();
        Assert.Equal("토공사^포장공사", (await check.NoticeLicenses.SingleAsync(x => x.NoticeId == id)).MainFields);
    }
}
