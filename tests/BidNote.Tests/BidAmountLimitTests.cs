using System.Text.Json;
using BidNote.Application.Company;
using BidNote.Application.Home;
using BidNote.Domain.Company;
using BidNote.Domain.Notices;
using BidNote.Infrastructure.G2B;
using BidNote.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BidNote.Tests;

public sealed class AmountLimitRuleTests
{
    private const long Eok = 100_000_000;

    private static LicenseRow Row(int group, string name, params string[] alternatives) => new(group, name, alternatives);

    [Fact]
    public void 상한은_시공능력평가액과_실적_나누기_배수_중_작은_값이다()
    {
        Assert.Equal(30 * Eok, LicenseEligibility.AmountLimit(30 * Eok, null, null));
        Assert.Equal(40 * Eok, LicenseEligibility.AmountLimit(null, 120 * Eok, 3));
        Assert.Equal(30 * Eok, LicenseEligibility.AmountLimit(30 * Eok, 120 * Eok, 3));
        Assert.Null(LicenseEligibility.AmountLimit(null, 120 * Eok, null)); // 배수 없이 실적만으로는 계산하지 않는다
        Assert.Null(LicenseEligibility.AmountLimit(0, 0, 0));
    }

    [Fact]
    public void 추정가격이_상한_이내면_참여_가능_넘으면_금액_초과()
    {
        LicenseRow[] rows = [Row(1, "토목공사업")];
        var mine = new Dictionary<string, long?> { ["토목공사업"] = 30 * Eok };

        var within = LicenseEligibility.Check(rows, mine, 30 * Eok);
        var over = LicenseEligibility.Check(rows, mine, 30 * Eok + 1);

        Assert.Equal((EligibilityKind.Eligible, AmountVerdict.Within, (long?)(30 * Eok), true), (within.Kind, within.Amount, within.AmountLimit, within.CanBid));
        Assert.Equal((EligibilityKind.Eligible, AmountVerdict.Over, false), (over.Kind, over.Amount, over.CanBid));
    }

    [Fact]
    public void 상한이나_추정가격을_모르면_금액은_따지지_않는다()
    {
        LicenseRow[] rows = [Row(1, "토목공사업")];

        var noLimit = LicenseEligibility.Check(rows, new Dictionary<string, long?> { ["토목공사업"] = null }, 99 * Eok);
        var noPrice = LicenseEligibility.Check(rows, new Dictionary<string, long?> { ["토목공사업"] = Eok }, null);

        Assert.Equal((AmountVerdict.Unknown, true), (noLimit.Amount, noLimit.CanBid));
        Assert.Equal((AmountVerdict.Unknown, true), (noPrice.Amount, noPrice.CanBid));
    }

    [Fact]
    public void 그룹_안의_여러_면허는_가장_작은_상한을_쓰고_그룹끼리는_넉넉한_쪽을_쓴다()
    {
        LicenseRow[] rows = [Row(1, "토목공사업"), Row(1, "포장공사업"), Row(2, "토목건축공사업")];
        var mine = new Dictionary<string, long?> { ["토목공사업"] = 50 * Eok, ["포장공사업"] = 10 * Eok, ["토목건축공사업"] = 20 * Eok };

        var result = LicenseEligibility.Check(rows, mine, 15 * Eok);

        Assert.Equal((2, (long?)(20 * Eok), AmountVerdict.Within), (result.GroupNo, result.AmountLimit, result.Amount));
    }

    [Fact]
    public void 대신_인정하는_면허로_충족하면_그_면허의_상한을_쓴다()
    {
        LicenseRow[] rows = [Row(1, "건축공사업", "토목건축공사업")];
        var mine = new Dictionary<string, long?> { ["토목건축공사업"] = 80 * Eok };

        Assert.Equal(80 * Eok, LicenseEligibility.Check(rows, mine, 70 * Eok).AmountLimit);
    }

    [Fact]
    public void 추정가격을_공고_목록_원문에서_읽는다()
    {
        using var doc = JsonDocument.Parse("""{"bidNtceNo":"R1","bidNtceOrd":"000","presmptPrce":"1234567890","bidNtceDt":"2026-10-20 10:00:00"}""");
        using var empty = JsonDocument.Parse("""{"bidNtceNo":"R2","bidNtceOrd":"000","presmptPrce":""}""");

        Assert.Equal(1_234_567_890, G2BCollector.Map(doc.RootElement, [], "").EstimatedPrice);
        Assert.Null(G2BCollector.Map(empty.RootElement, [], "").EstimatedPrice);
    }
}

public sealed class CompanyAmountStoreTests : IDisposable
{
    private const long Eok = 100_000_000;
    private static readonly DateTime Now = new(2026, 10, 25, 9, 0, 0);
    private readonly TestDb testDb = new();
    private readonly CompanyStore store;

    public CompanyAmountStoreTests() => store = new CompanyStore(new Factory(testDb), new TestClock(Now));

    public void Dispose() => testDb.Dispose();

    private sealed class Factory(TestDb db) : IDbContextFactory<BidNoteDbContext>
    {
        public BidNoteDbContext CreateDbContext() => db.NewContext();
    }

    private void AddNotice(string no, long? estimated, string license)
    {
        using var db = testDb.NewContext();
        db.Notices.Add(new Notice
        {
            BidNo = no,
            BidSeq = "000",
            Name = "공고 " + no,
            Agency = "○○시",
            EstimatedPrice = estimated,
            DueAt = Now.AddDays(2),
            OpenAt = Now.AddDays(3),
            PostedAt = Now.AddDays(-1),
            LastModifiedAt = Now,
            FirstCollectedAt = Now,
            LastCheckedAt = Now,
            Licenses = [new NoticeLicense { GroupNo = 1, Name = license, Alternatives = "" }],
        });
        db.SaveChanges();
    }

    [Fact]
    public async Task 면허_금액을_저장하면_상한을_계산하고_0_이하는_비운다()
    {
        await store.AddLicenseAsync("토목공사업", CancellationToken.None);

        Assert.True(await store.SaveLicenseAmountsAsync("토목공사업", new LicenseAmountsInput(30 * Eok, 120 * Eok, 3), CancellationToken.None));
        Assert.False(await store.SaveLicenseAmountsAsync("없는면허", new LicenseAmountsInput(1, 1, 1), CancellationToken.None));
        Assert.Equal(new CompanyLicenseInfo("토목공사업", 30 * Eok, 120 * Eok, 3, 30 * Eok), (await store.GetAsync(CancellationToken.None)).Licenses.Single());

        await store.SaveLicenseAmountsAsync("토목공사업", new LicenseAmountsInput(0, -5, 0), CancellationToken.None);
        Assert.Equal(new CompanyLicenseInfo("토목공사업", null, null, null, null), (await store.GetAsync(CancellationToken.None)).Licenses.Single());
    }

    [Fact]
    public async Task 금액_상한을_넘는_공고는_따로_세고_목록_필터에서_뺀다()
    {
        AddNotice("SMALL", 20 * Eok, "토목공사업");
        AddNotice("BIG", 50 * Eok, "토목공사업");
        AddNotice("NOPRICE", null, "토목공사업");
        AddNotice("OTHER", 10 * Eok, "전기공사업");
        await store.AddLicenseAsync("토목공사업", CancellationToken.None);
        await store.SaveLicenseAmountsAsync("토목공사업", new LicenseAmountsInput(30 * Eok, null, null), CancellationToken.None);

        Assert.Equal(new EligibilitySummary(2, 1, 0, 1), await store.SummaryAsync(Now, CancellationToken.None));

        using var ctx = testDb.NewContext();
        var result = await new NoticeRepository(ctx).SearchAsync(new NoticeFilter { MyLicensesOnly = true }, Now, CancellationToken.None);
        Assert.Equal(["NOPRICE", "SMALL"], result.Items.Select(x => x.BidNo).Order());
    }
}
