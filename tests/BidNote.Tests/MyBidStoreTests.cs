using BidNote.Application.Bidding;
using BidNote.Domain.Notices;
using BidNote.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BidNote.Tests;

public sealed class MyBidStoreTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 19, 9, 0, 0);
    private readonly TestDb testDb = new();
    private readonly MyBidStore store;

    public MyBidStoreTests() => store = new MyBidStore(new Factory(testDb), new TestClock(Now));

    public void Dispose() => testDb.Dispose();

    private sealed class Factory(TestDb db) : IDbContextFactory<BidNoteDbContext>
    {
        public BidNoteDbContext CreateDbContext() => db.NewContext();
    }

    private int AddNotice(string no, DateTime due, bool active = true, long? basePrice = 100_000_000)
    {
        using var db = testDb.NewContext();
        var n = new Notice
        {
            BidNo = no,
            BidSeq = "000",
            Name = "공고 " + no,
            Agency = "○○시",
            BasePrice = basePrice,
            AValue = 5_000_000,
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
    public async Task 저장하면_기초금액을_함께_남기고_다시_저장하면_고친다()
    {
        int id = AddNotice("A", Now.AddDays(2));

        await store.SaveAsync(new MyBidInput(id, 100.123, 87_900_000, " 메모 "), CancellationToken.None);
        await store.SaveAsync(new MyBidInput(id, 99.5, 87_300_000), CancellationToken.None);

        var bid = await store.GetAsync(id, CancellationToken.None);
        Assert.NotNull(bid);
        Assert.Equal((99.5, 87_300_000L, 100_000_000L, 5_000_000L, ""), (bid.AssessmentRate, bid.BidAmount, bid.BasePriceAtBid!.Value, bid.AValueAtBid!.Value, bid.Memo));
        using var db = testDb.NewContext();
        Assert.Equal(1, db.MyBids.Count());
    }

    [Fact]
    public async Task 사정률이나_금액이_0이면_저장하지_않고_없는_공고면_예외()
    {
        int id = AddNotice("A", Now.AddDays(2));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.SaveAsync(new MyBidInput(id, 0, 1), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.SaveAsync(new MyBidInput(id, 100, 0), CancellationToken.None));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => store.SaveAsync(new MyBidInput(999, 100, 1), CancellationToken.None));
    }

    [Fact]
    public async Task 목록은_마감_전을_먼저_마감순으로_그다음_개찰_지난_것을_보여_주고_단계를_나눈다()
    {
        int later = AddNotice("LATER", Now.AddDays(3));
        int soon = AddNotice("SOON", Now.AddHours(5));
        int waiting = AddNotice("WAIT", Now.AddMinutes(-30));   // 마감 30분 전, 개찰 30분 뒤
        int opened = AddNotice("OPEN", Now.AddDays(-2));
        int cancelled = AddNotice("CANCEL", Now.AddDays(1), active: false);
        foreach (int id in new[] { later, soon, waiting, opened, cancelled })
            await store.SaveAsync(new MyBidInput(id, 100, 1_000), CancellationToken.None);

        var list = await store.ListAsync(Now, CancellationToken.None);

        Assert.Equal(["SOON", "LATER"], list.Take(2).Select(x => x.BidNo));
        Assert.Equal(MyBidStage.WaitingOpen, list.Single(x => x.BidNo == "WAIT").StageAt(Now));
        Assert.Equal(MyBidStage.Opened, list.Single(x => x.BidNo == "OPEN").StageAt(Now));
        Assert.Equal(MyBidStage.Cancelled, list.Single(x => x.BidNo == "CANCEL").StageAt(Now));
    }

    [Fact]
    public async Task 기록한_뒤_기초금액이_바뀌면_알려_주고_지우면_사라진다()
    {
        int id = AddNotice("A", Now.AddDays(2));
        await store.SaveAsync(new MyBidInput(id, 100, 1_000), CancellationToken.None);
        using (var db = testDb.NewContext())
        {
            await db.Notices.Where(x => x.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.BasePrice, 120_000_000L));
        }

        Assert.True((await store.ListAsync(Now, CancellationToken.None)).Single().BasePriceChanged);

        await store.RemoveAsync(id, CancellationToken.None);
        Assert.Null(await store.GetAsync(id, CancellationToken.None));
    }
}
