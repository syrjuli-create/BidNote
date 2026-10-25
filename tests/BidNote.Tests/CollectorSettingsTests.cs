using BidNote.Application.Collecting;
using BidNote.Infrastructure.Persistence;
using Microsoft.Extensions.Options;

namespace BidNote.Tests;

public sealed class CollectorSettingsTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 19, 12, 0, 0);
    private readonly TestDb testDb = new();

    public void Dispose() => testDb.Dispose();

    private CollectorSettingsStore Store(BidNoteDbContext db) =>
        new(db, Options.Create(new CollectorOptions { BackfillDays = 7, IntervalMinutes = 10 }), new TestClock(Now));

    [Fact]
    public async Task 저장한_적이_없으면_설정_파일_기본값을_주고_저장하면_DB_값을_준다()
    {
        using (var db = testDb.NewContext())
        {
            Assert.Equal(new CollectorSettingsValues(7, 10, null), await Store(db).GetAsync(CancellationToken.None));
            await Store(db).SaveAsync(30, 60, CancellationToken.None);
        }
        using (var db = testDb.NewContext())
            Assert.Equal(new CollectorSettingsValues(30, 60, null), await Store(db).GetAsync(CancellationToken.None));
    }

    [Fact]
    public async Task 마지막_성공_시각은_뒤로_가지_않는다()
    {
        using var db = testDb.NewContext();
        var store = Store(db);
        await store.MarkSuccessAsync(Now, CancellationToken.None);
        await store.MarkSuccessAsync(Now.AddDays(-30), CancellationToken.None); // 다시 받기(과거 구간) 결과

        Assert.Equal(Now, (await store.GetAsync(CancellationToken.None)).LastSuccessTo);
    }

    [Fact]
    public async Task 개찰_결과_성공_시각은_공고와_따로_기록하고_뒤로_가지_않는다()
    {
        using var db = testDb.NewContext();
        var store = Store(db);
        await store.MarkSuccessAsync(Now, CancellationToken.None);
        await store.MarkResultsSuccessAsync(Now.AddHours(-2), CancellationToken.None);
        await store.MarkResultsSuccessAsync(Now.AddDays(-10), CancellationToken.None);

        var values = await store.GetAsync(CancellationToken.None);
        Assert.Equal(Now, values.LastSuccessTo);
        Assert.Equal(Now.AddHours(-2), values.ResultsLastSuccessTo);
    }

    [Fact]
    public void 개찰_결과는_실패해_멈춘_시각부터_다시_받고_처음이면_설정_기간만큼_받는다()
    {
        DateTime from = Now.AddMinutes(-10);

        Assert.Equal(from, CollectorRules.CatchUpFrom(from, Now, from, hasAny: true, 14));                     // 지난번 성공: 같은 구간
        Assert.Equal(Now.AddHours(-5), CollectorRules.CatchUpFrom(from, Now, Now.AddHours(-5), true, 14));          // 지난번 실패: 멈춘 곳부터
        Assert.Equal(Now.AddDays(-3), CollectorRules.CatchUpFrom(Now.AddDays(-3), Now, Now.AddHours(-1), true, 14)); // 다시 받기 구간이 더 앞이면 그 구간
        Assert.Equal(from, CollectorRules.CatchUpFrom(from, Now, null, hasAny: true, 14));                     // 기록 전부터 결과가 있던 DB
        Assert.Equal(Now.AddDays(-14), CollectorRules.CatchUpFrom(from, Now, null, hasAny: false, 14));       // 처음
        Assert.Equal(Now.AddDays(-60), CollectorRules.CatchUpFrom(from, Now, Now.AddDays(-200), true, 14));         // 최대 60일
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(61, 10)]
    [InlineData(7, 4)]
    [InlineData(7, 1441)]
    public async Task 범위를_벗어난_값은_저장하지_않는다(int days, int minutes)
    {
        using var db = testDb.NewContext();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Store(db).SaveAsync(days, minutes, CancellationToken.None));
    }

    [Fact]
    public void 처음이면_설정_기간만큼_이어_받기면_마지막_성공부터_너무_오래됐으면_60일까지만()
    {
        Assert.Equal(Now.AddDays(-14), CollectorRules.StartFrom(Now, null, 14));
        Assert.Equal(Now.AddHours(-3), CollectorRules.StartFrom(Now, Now.AddHours(-3), 14));
        Assert.Equal(Now.AddDays(-60), CollectorRules.StartFrom(Now, Now.AddDays(-200), 14));
    }

    [Fact]
    public void 예상_호출_수는_기간이_길수록_늘고_7일은_실측과_비슷하다()
    {
        // 2026-10-19 실측 7일: 공고 3 + 면허 10~11 + 기초금액 3 페이지
        Assert.InRange(CollectorRules.EstimateCalls(7), 14, 18);
        Assert.True(CollectorRules.EstimateCalls(60) > CollectorRules.EstimateCalls(30));
    }
}
