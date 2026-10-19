using BidNote.Domain.Favorites;
using BidNote.Domain.Notices;
using BidNote.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BidNote.Tests;

public sealed class FavoriteStoreTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 19, 9, 0, 0);
    private readonly TestDb testDb = new();
    private readonly FavoriteStore store;

    public FavoriteStoreTests() => store = new FavoriteStore(new Factory(testDb), new TestClock(Now));

    public void Dispose() => testDb.Dispose();

    private sealed class Factory(TestDb db) : IDbContextFactory<BidNoteDbContext>
    {
        public BidNoteDbContext CreateDbContext() => db.NewContext();
    }

    private int AddNotice(string no, DateTime due, bool active = true)
    {
        using var db = testDb.NewContext();
        var n = new Notice
        {
            BidNo = no,
            BidSeq = "000",
            Name = "공고 " + no,
            Agency = "○○시",
            DueAt = due,
            OpenAt = due.AddHours(1),
            PostedAt = Now.AddDays(-1),
            IsActive = active,
            LastModifiedAt = Now,
            FirstCollectedAt = Now,
            LastCheckedAt = Now,
        };
        db.Notices.Add(n);
        db.SaveChanges();
        return n.Id;
    }

    [Fact]
    public async Task 별을_누르면_담기고_다시_누르면_빠진다()
    {
        int id = AddNotice("R1", Now.AddDays(2));

        Assert.True(await store.ToggleAsync(id, CancellationToken.None));
        Assert.Equal([id], await store.FavoriteIdsAsync([id], CancellationToken.None));
        Assert.False(await store.ToggleAsync(id, CancellationToken.None));
        Assert.Empty(await store.FavoriteIdsAsync([id], CancellationToken.None));
    }

    [Fact]
    public async Task 없는_공고는_담을_수_없다()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() => store.ToggleAsync(999, CancellationToken.None));
    }

    [Fact]
    public async Task 메모를_저장하면_담기고_너무_길면_잘라_저장한다()
    {
        int id = AddNotice("R1", Now.AddDays(2));

        await store.SaveMemoAsync(id, "  현장 설명 없음  ", CancellationToken.None);
        Assert.Equal("현장 설명 없음", await store.MemoAsync(id, CancellationToken.None));

        await store.SaveMemoAsync(id, new string('가', FavoriteNotice.MaxMemoLength + 50), CancellationToken.None);
        Assert.Equal(FavoriteNotice.MaxMemoLength, (await store.MemoAsync(id, CancellationToken.None))!.Length);
        using var db = testDb.NewContext();
        Assert.Equal(1, await db.FavoriteNotices.CountAsync());
    }

    [Fact]
    public async Task 목록은_진행_중을_마감_임박순으로_마감과_취소는_따로_준다()
    {
        int later = AddNotice("LATER", Now.AddDays(5));
        int soon = AddNotice("SOON", Now.AddHours(5));
        int past = AddNotice("PAST", Now.AddDays(-1));
        int cancelled = AddNotice("CANCEL", Now.AddDays(3), active: false);
        foreach (int id in new[] { later, soon, past, cancelled }) await store.ToggleAsync(id, CancellationToken.None);

        var list = await store.ListAsync(Now, CancellationToken.None);

        Assert.Equal(["SOON", "LATER"], list.Open.Select(x => x.BidNo));
        Assert.Equal(["CANCEL", "PAST"], list.Closed.Select(x => x.BidNo));
        Assert.Equal(2, await store.CountOpenAsync(Now, CancellationToken.None));
    }

    [Fact]
    public async Task 공고가_지워지면_관심_공고도_함께_지워진다()
    {
        int id = AddNotice("FAKE-1", Now.AddDays(2));
        await store.ToggleAsync(id, CancellationToken.None);

        using (var db = testDb.NewContext())
            await db.Notices.Where(x => x.Id == id).ExecuteDeleteAsync();

        using var check = testDb.NewContext();
        Assert.Equal(0, await check.FavoriteNotices.CountAsync());
    }
}
