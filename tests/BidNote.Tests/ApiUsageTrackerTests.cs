using BidNote.Application.Collecting;
using BidNote.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BidNote.Tests;

public sealed class ApiUsageTrackerTests : IDisposable
{
    private readonly TestDb testDb = new();
    private readonly TestClock clock = new(new DateTime(2026, 10, 19, 9, 0, 0));
    private readonly ApiLimitOptions limits = new() { DefaultDaily = 10, StopAtRatio = 0.9, PerApi = { ["면허"] = 3 } };

    public void Dispose() => testDb.Dispose();

    private ApiUsageTracker NewTracker(BidNoteDbContext db) => new(db, Options.Create(limits), clock, NullLogger<ApiUsageTracker>.Instance);

    [Fact]
    public async Task 호출을_세고_날짜가_바뀌면_0부터_다시_센다()
    {
        using (var db = testDb.NewContext())
        {
            var t = NewTracker(db);
            await t.RecordAsync("공고", CancellationToken.None);
            await t.RecordAsync("공고", CancellationToken.None);
            await t.RecordAsync("공고", CancellationToken.None);
        }
        using (var db = testDb.NewContext())
        {
            var today = await NewTracker(db).GetTodayAsync(CancellationToken.None);
            var row = Assert.Single(today, x => x.ApiName == "공고");
            Assert.Equal(3, row.Count);
            Assert.Equal(10, row.DailyLimit);
        }

        clock.Now = clock.Now.AddDays(1);
        using (var db = testDb.NewContext())
        {
            var today = await NewTracker(db).GetTodayAsync(CancellationToken.None);
            Assert.DoesNotContain(today, x => x.ApiName == "공고");
        }
    }

    [Fact]
    public async Task 한도의_정해진_비율에_닿으면_더_호출하지_않는다()
    {
        using var db = testDb.NewContext();
        var t = NewTracker(db);

        for (int i = 0; i < 8; i++) await t.RecordAsync("공고", CancellationToken.None);
        Assert.True(await t.CanCallAsync("공고", CancellationToken.None)); // 8 < 10 × 0.9

        await t.RecordAsync("공고", CancellationToken.None);
        Assert.False(await t.CanCallAsync("공고", CancellationToken.None)); // 9 ≥ 9
    }

    [Fact]
    public async Task 설정에_한도가_있는_API는_호출이_없어도_0건으로_보여준다()
    {
        using var db = testDb.NewContext();

        var today = await NewTracker(db).GetTodayAsync(CancellationToken.None);

        var row = Assert.Single(today);
        Assert.Equal(("면허", 0, 3), (row.ApiName, row.Count, row.DailyLimit));
        Assert.Null(row.LastCalledAt);
    }
}
