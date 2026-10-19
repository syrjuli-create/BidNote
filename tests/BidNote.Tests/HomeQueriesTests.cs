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
    public async Task 공고가_없으면_0건과_빈_목록을_준다()
    {
        using var ctx = testDb.NewContext();
        var s = await new NoticeRepository(ctx).GetSummaryAsync(Now, CancellationToken.None);

        Assert.Equal(0, s.ActiveCount);
        Assert.Empty(s.ClosingSoon);
        Assert.Null(s.LastCollectedAt);
    }

    private static Notice Notice(string name, DateTime due, DateTime posted, int seq = 0, bool active = true,
        string region = "", string license = "", long? price = null) => new()
        {
            BidNo = name,
            BidSeq = seq.ToString("00", System.Globalization.CultureInfo.InvariantCulture),
            Name = name,
            Agency = "○○시",
            Region = region,
            License = license,
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
