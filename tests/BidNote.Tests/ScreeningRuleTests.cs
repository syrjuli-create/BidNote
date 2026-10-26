using System.Text.Json;
using BidNote.Application.Company;
using BidNote.Application.Home;
using BidNote.Domain.Bidding;
using BidNote.Domain.Company;
using BidNote.Domain.Notices;
using BidNote.Infrastructure.G2B;
using BidNote.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BidNote.Tests;

public sealed class ScreeningRuleTests
{
    private const long Eok = 100_000_000;

    [Theory]
    [InlineData(1.5, "2억 미만", 0)]
    [InlineData(2, "3억 미만 ~ 2억 이상", 5)]
    [InlineData(8, "10억 미만 ~ 3억 이상", 10)]
    [InlineData(10, "50억 미만 ~ 10억 이상", 15)]
    [InlineData(99.9, "100억 미만 ~ 50억 이상", 15)]
    public void 추정가격으로_조달청_적격심사_구간을_고른다(double eok, string band, int experiencePoints)
    {
        var b = ScreeningRules.BandFor((long)(eok * Eok), ScreeningTrade.General);

        Assert.NotNull(b);
        Assert.Equal((band, experiencePoints), (b.Name, b.ExperiencePoints));
    }

    [Fact]
    public void 백억_이상이거나_추정가격을_모르면_구간이_없다()
    {
        Assert.Null(ScreeningRules.BandFor(100 * Eok, ScreeningTrade.General));
        Assert.Null(ScreeningRules.BandFor(null, ScreeningTrade.General));
    }

    [Fact]
    public void 전기_정보통신_소방_면허만_있는_공고는_구간이_따로다()
    {
        Assert.Equal(ScreeningTrade.Special, ScreeningRules.TradeOf(["전기공사업"]));
        Assert.Equal(ScreeningTrade.Special, ScreeningRules.TradeOf(["정보통신공사업", "전문소방시설공사업"]));
        Assert.Equal(ScreeningTrade.General, ScreeningRules.TradeOf(["전기공사업", "건축공사업"]));
        Assert.Equal(ScreeningTrade.General, ScreeningRules.TradeOf([]));

        // 전기 공사 3억~10억 구간은 시공경험 15점, 8천만 원 미만은 평가 안 함
        Assert.Equal(15, ScreeningRules.BandFor(5 * Eok, ScreeningTrade.Special)!.ExperiencePoints);
        Assert.False(ScreeningRules.BandFor(70_000_000, ScreeningTrade.Special)!.EvaluatesExperience);
    }

    [Fact]
    public void 시공경험_점수는_실적이_기초금액_곱하기_배수에_못_미치면_비례해서_깎인다()
    {
        var band = ScreeningRules.BandFor(8 * Eok, ScreeningTrade.General);

        var half = ScreeningRules.EstimateExperience(band, basePrice: 8 * Eok, estimatedPrice: 8 * Eok, performance: 2 * Eok, civil: false);
        var full = ScreeningRules.EstimateExperience(band, 8 * Eok, 8 * Eok, 6 * Eok, false);

        Assert.NotNull(half);
        Assert.Equal((4 * Eok, 5.0, false), (half.RequiredPerformance, half.Points, half.Full)); // 8억 × 0.5 = 4억, 2억이면 10점 중 5점
        Assert.Equal((10.0, true), (full!.Points, full.Full));
    }

    [Fact]
    public void 오십억_이상_구간은_토목이면_배수_1배_아니면_2배()
    {
        var band = ScreeningRules.BandFor(60 * Eok, ScreeningTrade.General);

        Assert.Equal(60 * Eok, ScreeningRules.EstimateExperience(band, 60 * Eok, null, 30 * Eok, civil: true)!.RequiredPerformance);
        Assert.Equal(120 * Eok, ScreeningRules.EstimateExperience(band, 60 * Eok, null, 30 * Eok, civil: false)!.RequiredPerformance);
    }

    [Fact]
    public void 기초금액이_없으면_추정가격으로_보고_실적이나_평가가_없으면_계산하지_않는다()
    {
        var band = ScreeningRules.BandFor(8 * Eok, ScreeningTrade.General);

        Assert.Equal(4 * Eok, ScreeningRules.EstimateExperience(band, null, 8 * Eok, Eok, false)!.RequiredPerformance);
        Assert.Null(ScreeningRules.EstimateExperience(band, 8 * Eok, 8 * Eok, null, false));
        Assert.Null(ScreeningRules.EstimateExperience(ScreeningRules.BandFor(Eok, ScreeningTrade.General), Eok, Eok, Eok, false));
    }

    [Fact]
    public void 추정가격과_실적제한_여부를_공고_목록_원문에서_읽는다()
    {
        using var doc = JsonDocument.Parse("""
            {"bidNtceNo":"R1","bidNtceOrd":"000","presmptPrce":"1234567890","bidNtceDt":"2026-10-20 10:00:00",
             "arsltCmptYn":"Y","arsltApplDocRcptDt":"2026-10-26 10:00:00","arsltApplDocRcptMthdNm":"수기"}
            """);
        using var empty = JsonDocument.Parse("""{"bidNtceNo":"R2","bidNtceOrd":"000","presmptPrce":"","arsltCmptYn":"N","arsltApplDocRcptDt":""}""");

        var limited = G2BCollector.Map(doc.RootElement, [], "");
        var plain = G2BCollector.Map(empty.RootElement, [], "");

        Assert.Equal(1_234_567_890, limited.EstimatedPrice);
        Assert.Equal((true, new DateTime(2026, 10, 26, 10, 0, 0), "수기"), (limited.PerformanceLimited, limited.PerformanceDocDueAt, limited.PerformanceDocMethod));
        Assert.Null(plain.EstimatedPrice);
        Assert.Equal((false, (DateTime?)null), (plain.PerformanceLimited, plain.PerformanceDocDueAt));
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

    private void AddNotice(string no, long? estimated, string license, bool performanceLimited = false)
    {
        using var db = testDb.NewContext();
        db.Notices.Add(new Notice
        {
            BidNo = no,
            BidSeq = "000",
            Name = "공고 " + no,
            Agency = "○○시",
            EstimatedPrice = estimated,
            PerformanceLimited = performanceLimited,
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
    public async Task 면허의_실적과_시평액을_저장하고_0_이하는_비운다()
    {
        await store.AddLicenseAsync("토목공사업", CancellationToken.None);

        Assert.True(await store.SaveLicenseAmountsAsync("토목공사업", new LicenseAmountsInput(30 * Eok, 12 * Eok), CancellationToken.None));
        Assert.False(await store.SaveLicenseAmountsAsync("없는면허", new LicenseAmountsInput(1, 1), CancellationToken.None));
        Assert.Equal(new CompanyLicenseInfo("토목공사업", 30 * Eok, 12 * Eok), (await store.GetAsync(CancellationToken.None)).Licenses.Single());

        await store.SaveLicenseAmountsAsync("토목공사업", new LicenseAmountsInput(0, -5), CancellationToken.None);
        Assert.Equal(new CompanyLicenseInfo("토목공사업", null, null), (await store.GetAsync(CancellationToken.None)).Licenses.Single());
    }

    [Fact]
    public async Task 실적이나_시평액이_모자라도_참여_가능에서_빼지_않고_실적제한_공고는_따로_센다()
    {
        AddNotice("SMALL", 2 * Eok, "토목공사업");
        AddNotice("BIG", 50 * Eok, "토목공사업");
        AddNotice("LIMITED", 8 * Eok, "토목공사업", performanceLimited: true);
        AddNotice("OTHER", 10 * Eok, "전기공사업");
        await store.AddLicenseAsync("토목공사업", CancellationToken.None);
        await store.SaveLicenseAmountsAsync("토목공사업", new LicenseAmountsInput(3 * Eok, Eok), CancellationToken.None);

        Assert.Equal(new EligibilitySummary(3, 1, 0, PerformanceLimited: 1), await store.SummaryAsync(Now, CancellationToken.None));

        using var ctx = testDb.NewContext();
        var result = await new NoticeRepository(ctx).SearchAsync(new NoticeFilter { MyLicensesOnly = true }, Now, CancellationToken.None);
        Assert.Equal(["BIG", "LIMITED", "SMALL"], result.Items.Select(x => x.BidNo).Order());
    }
}
