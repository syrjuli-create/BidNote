using BidNote.Application.Home;
using BidNote.Domain.Searching;
using BidNote.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BidNote.Tests;

public sealed class SearchQueryTests
{
    [Fact]
    public void 아는_키만_고정된_순서로_남기고_빈_값과_페이지는_뺀다()
    {
        string q = SearchQuery.Normalize("?page=3&sort=due&new=true&region=%EA%B2%BD%EA%B8%B0&q=+%ED%8F%AC%EC%9E%A5+&license=&foo=1&region=%EB%8C%80%EC%A0%84&amount=x&due=1");

        Assert.Equal("q=%ED%8F%AC%EC%9E%A5&region=%EA%B2%BD%EA%B8%B0&region=%EB%8C%80%EC%A0%84&due=1&new=true", q);
    }

    [Fact]
    public void 순서만_다른_조건은_같은_값이_된다()
    {
        Assert.Equal(
            SearchQuery.Normalize("region=대전&region=경기&nosu=true&sort=amount"),
            SearchQuery.Normalize("sort=amount&nosu=true&region=경기&region=대전&region=경기"));
    }

    [Fact]
    public void 필터가_없으면_빈_문자열이다()
    {
        Assert.Equal("", SearchQuery.Normalize(""));
        Assert.Equal("", SearchQuery.Normalize("?q=&license=&amount=&due=&page=2"));
        Assert.Equal("", SearchQuery.Normalize("corrected=false&sort=due"));
    }

    [Fact]
    public void 조건을_짧은_설명으로_바꾼다()
    {
        var text = SearchQuery.Describe("q=포장&region=경기&amount=s&due=3&corrected=true&sort=new");

        Assert.Equal(["'포장'", "경기", "1억 미만", "3일 이내", "정정만", "최신 공고순"], text);
    }
}

public sealed class SavedSearchStoreTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 25, 9, 0, 0);
    private readonly TestDb testDb = new();
    private readonly SavedSearchStore store;

    public SavedSearchStoreTests() => store = new SavedSearchStore(new Factory(testDb), new TestClock(Now));

    public void Dispose() => testDb.Dispose();

    private sealed class Factory(TestDb db) : IDbContextFactory<BidNoteDbContext>
    {
        public BidNoteDbContext CreateDbContext() => db.NewContext();
    }

    [Fact]
    public async Task 이름을_붙여_저장하고_같은_이름이면_덮어쓴다()
    {
        await store.SaveAsync("  경기 포장  ", "region=경기&page=2", CancellationToken.None);
        await store.SaveAsync("대전", "region=대전", CancellationToken.None);
        await store.SaveAsync("경기 포장", "region=경기&amount=s", CancellationToken.None);

        var list = await store.ListAsync(CancellationToken.None);

        Assert.Equal(["경기 포장", "대전"], list.Select(x => x.Name));
        Assert.Equal(SearchQuery.Normalize("region=경기&amount=s"), list[0].Query);
    }

    [Fact]
    public async Task 이름이나_조건이_비면_저장하지_않고_긴_이름은_자른다()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync("  ", "region=경기", CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync("빈 조건", "page=2&sort=due", CancellationToken.None));

        var saved = await store.SaveAsync(new string('가', SavedSearch.MaxNameLength + 10), "new=true", CancellationToken.None);

        Assert.Equal(SavedSearch.MaxNameLength, saved.Name.Length);
        Assert.Single(await store.ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task 지우면_목록에서_빠진다()
    {
        var a = await store.SaveAsync("A", "new=true", CancellationToken.None);
        await store.SaveAsync("B", "corrected=true", CancellationToken.None);

        await store.DeleteAsync(a.Id, CancellationToken.None);

        Assert.Equal(["B"], (await store.ListAsync(CancellationToken.None)).Select(x => x.Name));
    }

    [Fact]
    public async Task 마지막_조건을_기억하고_빈_조건이면_지운다()
    {
        Assert.Equal("", await store.GetLastAsync(CancellationToken.None));

        await store.SetLastAsync("?region=경기&page=4", CancellationToken.None);
        Assert.Equal(SearchQuery.Normalize("region=경기"), await store.GetLastAsync(CancellationToken.None));

        await store.SetLastAsync("due=1", CancellationToken.None);
        Assert.Equal("due=1", await store.GetLastAsync(CancellationToken.None));

        await store.SetLastAsync("page=2", CancellationToken.None);
        Assert.Equal("", await store.GetLastAsync(CancellationToken.None));
        using var db = testDb.NewContext();
        Assert.Equal(0, await db.LastSearches.CountAsync());
    }
}
