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
    public async Task 빼면_휴지통으로_가고_메모와_폴더를_지키며_복원된다()
    {
        int id = AddNotice("R1", Now.AddDays(2));
        await store.SaveMemoAsync(id, "현장 확인", CancellationToken.None);
        await store.MoveToFolderAsync(id, FavoriteFolder.Planned, CancellationToken.None);

        await store.TrashAsync(id, CancellationToken.None);

        var trashed = await store.ListAsync(Now, CancellationToken.None);
        Assert.Empty(trashed.Open);
        Assert.Equal(["R1"], trashed.Trash.Select(x => x.BidNo));
        Assert.Empty(await store.FavoriteIdsAsync([id], CancellationToken.None));
        Assert.Equal(0, await store.CountOpenAsync(Now, CancellationToken.None));

        await store.RestoreAsync(id, CancellationToken.None);

        var restored = (await store.ListAsync(Now, CancellationToken.None)).Open.Single();
        Assert.Equal(FavoriteFolder.Planned, restored.Folder);
        Assert.Equal("현장 확인", restored.Memo);
        Assert.Empty((await store.ListAsync(Now, CancellationToken.None)).Trash);
    }

    [Fact]
    public async Task 휴지통에_있는_공고의_별을_누르면_다시_담긴다()
    {
        int id = AddNotice("R1", Now.AddDays(2));
        await store.ToggleAsync(id, CancellationToken.None);
        await store.ToggleAsync(id, CancellationToken.None);

        Assert.True(await store.ToggleAsync(id, CancellationToken.None));
        Assert.Equal([id], await store.FavoriteIdsAsync([id], CancellationToken.None));
        using var db = testDb.NewContext();
        Assert.Equal(1, await db.FavoriteNotices.CountAsync());
    }

    [Fact]
    public async Task 폴더_이동은_휴지통에_있는_공고에는_적용되지_않는다()
    {
        int id = AddNotice("R1", Now.AddDays(2));
        await store.ToggleAsync(id, CancellationToken.None);
        await store.TrashAsync(id, CancellationToken.None);

        await store.MoveToFolderAsync(id, FavoriteFolder.Submitted, CancellationToken.None);

        Assert.Equal(FavoriteFolder.Reviewing, (await store.ListAsync(Now, CancellationToken.None)).Trash.Single().Folder);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => store.MoveToFolderAsync(id, (FavoriteFolder)99, CancellationToken.None));
    }

    [Fact]
    public async Task 완전_삭제와_비우기는_휴지통에_있는_것만_지운다()
    {
        int kept = AddNotice("KEEP", Now.AddDays(2));
        int one = AddNotice("ONE", Now.AddDays(2));
        int two = AddNotice("TWO", Now.AddDays(2));
        int three = AddNotice("THREE", Now.AddDays(2));
        foreach (int id in new[] { kept, one, two, three }) await store.ToggleAsync(id, CancellationToken.None);
        foreach (int id in new[] { one, two }) await store.TrashAsync(id, CancellationToken.None);

        await store.DeleteForeverAsync(kept, CancellationToken.None);
        await store.DeleteForeverAsync(one, CancellationToken.None);
        Assert.Equal(["KEEP", "THREE"], (await store.ListAsync(Now, CancellationToken.None)).Open.Select(x => x.BidNo).Order());
        Assert.Equal(["TWO"], (await store.ListAsync(Now, CancellationToken.None)).Trash.Select(x => x.BidNo));

        Assert.Equal(1, await store.EmptyTrashAsync(CancellationToken.None));
        using var db = testDb.NewContext();
        Assert.Equal(2, await db.FavoriteNotices.CountAsync());
    }

    [Fact]
    public async Task 휴지통에_있는_공고에_메모를_저장하면_꺼내진다()
    {
        int id = AddNotice("R1", Now.AddDays(2));
        await store.ToggleAsync(id, CancellationToken.None);
        await store.TrashAsync(id, CancellationToken.None);

        await store.SaveMemoAsync(id, "다시 검토", CancellationToken.None);

        Assert.Equal("다시 검토", (await store.ListAsync(Now, CancellationToken.None)).Open.Single().Memo);
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
