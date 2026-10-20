using BidNote.Application.Home;
using BidNote.Domain.Notices;
using BidNote.Infrastructure.Persistence;

namespace BidNote.Tests;

public sealed class HomeQueriesTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 19, 9, 0, 0);
    private readonly TestDb testDb = new();

    public void Dispose() => testDb.Dispose();

    [Fact]
    public async Task 마감이_지나거나_취소된_공고는_세지_않고_내일까지_마감만_임박으로_본다()
    {
        using (var db = testDb.NewContext())
        {
            db.Notices.AddRange(
                Notice("A", due: Now.AddHours(20), posted: Now.AddDays(-1)),             // 임박, 새 공고
                Notice("B", due: Now.AddHours(38), posted: Now.AddDays(-10), seq: 2),    // 내일 23시 마감: 임박, 정정
                Notice("F", due: Now.AddHours(40), posted: Now.AddDays(-10)),             // 모레 01시 마감: 임박 아님
                Notice("C", due: Now.AddDays(5), posted: Now.AddDays(-3)),                // 새 공고
                Notice("D", due: Now.AddHours(-1), posted: Now.AddDays(-6)),              // 마감 지남
                Notice("E", due: Now.AddDays(2), posted: Now.AddDays(-1), active: false)); // 취소
            await db.SaveChangesAsync();
        }

        using var ctx = testDb.NewContext();
        var s = await new NoticeRepository(ctx).GetSummaryAsync(Now, CancellationToken.None);

        Assert.Equal(4, s.ActiveCount);
        Assert.Equal(2, s.ClosingSoonCount);
        Assert.Equal(["A", "B"], s.ClosingSoon.Select(x => x.Name));
        Assert.Equal(2, s.NewCount);
        Assert.Equal(1, s.CorrectedCount);
        Assert.Equal("B", Assert.Single(s.RecentlyCorrected).Name);
    }

    [Fact]
    public async Task 검색어_지역_업종_금액으로_거르고_지역_목록은_전체_기준으로_준다()
    {
        using (var db = testDb.NewContext())
        {
            db.Notices.AddRange(
                Notice("○○초 화장실 개선공사", due: Now.AddDays(3), posted: Now.AddDays(-1), region: "대전", license: "실내건축", price: 186_400_000),
                Notice("○○로 포장공사", due: Now.AddDays(1), posted: Now.AddDays(-1), region: "대전", license: "포장", price: 94_300_000),
                Notice("○○천 정비공사", due: Now.AddDays(5), posted: Now.AddDays(-1), region: "충남", license: "토목", price: 1_248_000_000));
            await db.SaveChangesAsync();
        }
        using var ctx = testDb.NewContext();
        var repo = new NoticeRepository(ctx);

        var byKeyword = await repo.SearchAsync(new NoticeFilter { Keyword = "화장실" }, Now, CancellationToken.None);
        var byRegion = await repo.SearchAsync(new NoticeFilter { Regions = ["대전"], Sort = NoticeSort.AmountHigh }, Now, CancellationToken.None);
        var byAmount = await repo.SearchAsync(new NoticeFilter { Amount = AmountRange.Over500M }, Now, CancellationToken.None);
        var byDue = await repo.SearchAsync(new NoticeFilter { DueWithinDays = 1 }, Now, CancellationToken.None);

        Assert.Equal(["○○초 화장실 개선공사"], byKeyword.Items.Select(x => x.Name));
        Assert.Equal(["○○초 화장실 개선공사", "○○로 포장공사"], byRegion.Items.Select(x => x.Name));
        Assert.Equal(["○○천 정비공사"], byAmount.Items.Select(x => x.Name));
        Assert.Equal(["○○로 포장공사"], byDue.Items.Select(x => x.Name));
        Assert.Equal([new FacetCount("대전", 2), new FacetCount("충남", 1)], byKeyword.Regions);
    }

    [Fact]
    public async Task 업종_필터는_면허_제한_중_하나만_맞아도_찾고_업종_목록도_면허_기준으로_센다()
    {
        using (var db = testDb.NewContext())
        {
            var a = Notice("A", due: Now.AddDays(3), posted: Now.AddDays(-1), license: "토목");
            a.Licenses = [new() { GroupNo = 1, Name = "토목공사업" }, new() { GroupNo = 2, Name = "포장공사업" }];
            var b = Notice("B", due: Now.AddDays(3), posted: Now.AddDays(-1), license: "포장");
            b.Licenses = [new() { GroupNo = 1, Name = "포장공사업" }];
            db.Notices.AddRange(a, b, Notice("C", due: Now.AddDays(3), posted: Now.AddDays(-1), license: "전기"));
            await db.SaveChangesAsync();
        }
        using var ctx = testDb.NewContext();
        var repo = new NoticeRepository(ctx);

        var paving = await repo.SearchAsync(new NoticeFilter { License = "포장공사업" }, Now, CancellationToken.None);

        Assert.Equal(["A", "B"], paving.Items.Select(x => x.Name).Order());
        Assert.Equal([new FacetCount("포장공사업", 2), new FacetCount("전기", 1), new FacetCount("토목공사업", 1)], paving.Licenses);
    }

    [Fact]
    public async Task 계약방법으로_거르고_수의계약은_뺄_수_있다()
    {
        using (var db = testDb.NewContext())
        {
            db.Notices.AddRange(
                Notice("A", due: Now.AddDays(3), posted: Now.AddDays(-1), contract: "제한경쟁"),
                Notice("B", due: Now.AddDays(3), posted: Now.AddDays(-1), contract: "수의계약"),
                Notice("C", due: Now.AddDays(3), posted: Now.AddDays(-1), contract: "수의계약"),
                Notice("D", due: Now.AddDays(3), posted: Now.AddDays(-1), contract: "일반경쟁"));
            await db.SaveChangesAsync();
        }
        using var ctx = testDb.NewContext();
        var repo = new NoticeRepository(ctx);

        var limited = await repo.SearchAsync(new NoticeFilter { ContractMethod = "제한경쟁" }, Now, CancellationToken.None);
        var noNegotiated = await repo.SearchAsync(new NoticeFilter { ExcludeNegotiated = true }, Now, CancellationToken.None);

        Assert.Equal(["A"], limited.Items.Select(x => x.Name));
        Assert.Equal(["A", "D"], noNegotiated.Items.Select(x => x.Name).Order());
        Assert.Equal([new FacetCount("수의계약", 2), new FacetCount("일반경쟁", 1), new FacetCount("제한경쟁", 1)], limited.ContractMethods);
    }

    [Theory]
    [InlineData("제한경쟁(1등급)", "제한경쟁")]
    [InlineData(" 수의계약 ", "수의계약")]
    [InlineData("일반경쟁", "일반경쟁")]
    [InlineData("", "")]
    public void 계약방법은_괄호_설명을_떼고_묶는다(string raw, string expected) =>
        Assert.Equal(expected, ContractMethods.Group(raw));

    [Theory]
    [InlineData("서울특별시", "서울")]
    [InlineData("경상남도", "경남")]
    [InlineData("전북특별자치도", "전북")]
    [InlineData("전남광주통합특별시", "전남·광주")]
    [InlineData("전국", "전국")]
    public void 시도_이름은_짧게_바꾸고_모르는_이름은_그대로_둔다(string name, string expected) =>
        Assert.Equal(expected, RegionNames.ToShort(name));

    [Fact]
    public async Task 예전에_긴_이름으로_저장한_지역을_시작할_때_짧게_고친다()
    {
        using (var db = testDb.NewContext())
        {
            db.Notices.AddRange(
                Notice("A", due: Now.AddDays(3), posted: Now.AddDays(-1), region: "충청남도"),
                Notice("B", due: Now.AddDays(3), posted: Now.AddDays(-1), region: "충남"),
                Notice("C", due: Now.AddDays(3), posted: Now.AddDays(-1), region: "전국"));
            await db.SaveChangesAsync();
        }
        using (var db = testDb.NewContext())
            Assert.Equal(1, await DatabaseInitializer.ShortenRegionNamesAsync(db, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, CancellationToken.None));

        using var ctx = testDb.NewContext();
        Assert.Equal(["충남", "충남", "전국"], ctx.Notices.OrderBy(x => x.BidNo).Select(x => x.Region).ToList());
    }

    [Fact]
    public async Task 페이지를_나눠_주고_전체_건수를_알려준다_넘치는_페이지는_마지막_페이지로()
    {
        using (var db = testDb.NewContext())
        {
            for (int i = 1; i <= 7; i++)
                db.Notices.Add(Notice("N" + i, due: Now.AddDays(i), posted: Now.AddDays(-1)));
            await db.SaveChangesAsync();
        }
        using var ctx = testDb.NewContext();
        var repo = new NoticeRepository(ctx);

        var p2 = await repo.SearchAsync(new NoticeFilter { Page = 2, PageSize = 3 }, Now, CancellationToken.None);
        var tooFar = await repo.SearchAsync(new NoticeFilter { Page = 99, PageSize = 3 }, Now, CancellationToken.None);

        Assert.Equal((7, 2, 3), (p2.TotalCount, p2.Page, p2.PageCount));
        Assert.Equal(["N4", "N5", "N6"], p2.Items.Select(x => x.Name));
        Assert.Equal(3, tooFar.Page);
        Assert.Equal(["N7"], tooFar.Items.Select(x => x.Name));
    }

    [Theory]
    [InlineData(1, 1, new[] { 1 })]
    [InlineData(1, 5, new[] { 1, 2, 3, 4, 5 })]
    [InlineData(6, 40, new[] { 1, 0, 4, 5, 6, 7, 8, 0, 40 })]
    [InlineData(40, 40, new[] { 1, 0, 38, 39, 40 })]
    public void 페이지_번호는_앞뒤_2개와_처음_끝만_보여준다(int current, int count, int[] expected)
    {
        Assert.Equal(expected, BidNote.Web.Components.Pages.Home.PageNumbers(current, count));
    }

    [Fact]
    public async Task 공고가_없으면_0건과_빈_목록을_준다()
    {
        using var ctx = testDb.NewContext();
        var s = await new NoticeRepository(ctx).GetSummaryAsync(Now, CancellationToken.None);

        Assert.Equal(0, s.ActiveCount);
        Assert.Empty(s.ClosingSoon);
        Assert.Null(s.LastCollectedAt);
    }

    private static Notice Notice(string name, DateTime due, DateTime posted, int seq = 0, bool active = true,
        string region = "", string license = "", long? price = null, string contract = "") => new()
        {
            BidNo = name,
            BidSeq = seq.ToString("00", System.Globalization.CultureInfo.InvariantCulture),
            Name = name,
            Agency = "○○시",
            Region = region,
            License = license,
            ContractMethod = contract,
            BasePrice = price,
            DueAt = due,
            OpenAt = due.AddHours(1),
            PostedAt = posted,
            CorrectionCount = seq,
            IsActive = active,
            LastModifiedAt = posted,
            FirstCollectedAt = Now,
            LastCheckedAt = Now,
        };
}
