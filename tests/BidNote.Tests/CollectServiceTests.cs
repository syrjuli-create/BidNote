using BidNote.Application.Collecting;
using BidNote.Domain.Notices;
using BidNote.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NullLogger = Microsoft.Extensions.Logging.Abstractions.NullLogger;

namespace BidNote.Tests;

public sealed class CollectServiceTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 19, 9, 0, 0);
    private readonly TestDb testDb = new();
    private readonly StubCollector collector = new();

    public void Dispose() => testDb.Dispose();

    private async Task<CollectResult> RunAsync()
    {
        using var db = testDb.NewContext();
        var service = new CollectService([collector], new NoticeRepository(db), new TestClock(Now), NullLogger<CollectService>.Instance);
        return await service.CollectAsync(Now.AddDays(-1), Now, CancellationToken.None);
    }

    [Fact]
    public async Task 새_공고는_추가하고_원문도_저장한다()
    {
        collector.Items = [Item("R1", "00", "원문A")];

        var result = await RunAsync();

        Assert.Equal(new CollectResult(1, 0, 0), result);
        using var db = testDb.NewContext();
        var notice = await db.Notices.SingleAsync();
        Assert.Equal("공고 R1", notice.Name);
        Assert.Equal(Now, notice.FirstCollectedAt);
        Assert.Single(await db.ApiRawResponses.ToListAsync());
    }

    [Fact]
    public async Task 원문이_같으면_확인_시각만_바꾸고_이력도_원문도_새로_남기지_않는다()
    {
        collector.Items = [Item("R1", "00", "원문A")];
        await RunAsync();

        var result = await RunAsync();
        await RunAsync();

        Assert.Equal(new CollectResult(0, 0, 1), result);
        using var db = testDb.NewContext();
        Assert.Empty(await db.NoticeHistories.ToListAsync());
        // 같은 원문을 몇 번 받아도 원문은 처음 한 번만 저장한다 (용량 폭증 방지)
        Assert.Equal(1, await db.ApiRawResponses.CountAsync());
    }

    [Fact]
    public async Task 내용이_바뀐_공고는_바뀐_원문을_한_번_더_저장한다()
    {
        collector.Items = [Item("R1", "00", "원문A")];
        await RunAsync();
        collector.Items = [Item("R1", "01", "원문B")];
        await RunAsync();
        await RunAsync();

        using var db = testDb.NewContext();
        var raws = await db.ApiRawResponses.OrderBy(x => x.Id).Select(x => x.ResponseJson).ToListAsync();
        Assert.Equal(["원문A", "원문B"], raws);
    }

    [Fact]
    public async Task 정정_공고는_이전_상태를_이력으로_남기고_갱신한다()
    {
        collector.Items = [Item("R1", "00", "원문A", due: Now.AddDays(3))];
        await RunAsync();

        collector.Items = [Item("R1", "01", "원문B", due: Now.AddDays(5))];
        var result = await RunAsync();

        Assert.Equal(new CollectResult(0, 1, 0), result);
        using var db = testDb.NewContext();
        var notice = await db.Notices.Include(x => x.Histories).SingleAsync();
        Assert.Equal("01", notice.BidSeq);
        Assert.Equal(1, notice.CorrectionCount);
        Assert.Equal(Now.AddDays(5), notice.DueAt);
        var history = Assert.Single(notice.Histories);
        Assert.Equal(NoticeChangeType.Corrected, history.ChangeType);
        Assert.Equal("00", history.BidSeq);
        Assert.Contains(Now.AddDays(3).ToString("yyyy-MM-dd"), history.SnapshotJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 한_번에_같은_공고의_최초와_정정_차수가_함께_오면_하나로_합치고_정정_이력을_남긴다()
    {
        // 실제 7일치 수집에서 같은 공고번호가 000, 001로 함께 와 UNIQUE 오류가 났던 경우
        collector.Items = [Item("R1", "001", "정정본", due: Now.AddDays(5)), Item("R1", "000", "최초본", due: Now.AddDays(3)), Item("R2", "000", "다른 공고")];

        var result = await RunAsync();

        Assert.Equal(new CollectResult(2, 1, 0), result);
        using var db = testDb.NewContext();
        var r1 = await db.Notices.Include(x => x.Histories).SingleAsync(x => x.BidNo == "R1");
        Assert.Equal("001", r1.BidSeq);
        Assert.Equal(Now.AddDays(5), r1.DueAt);
        Assert.Equal("000", Assert.Single(r1.Histories).BidSeq);
        Assert.Equal(3, await db.ApiRawResponses.CountAsync());
    }

    [Fact]
    public async Task 이미_정정된_공고에_예전_차수가_다시_와도_되돌리지_않는다()
    {
        collector.Items = [Item("R1", "001", "정정본")];
        await RunAsync();
        collector.Items = [Item("R1", "000", "최초본")];

        var result = await RunAsync();

        Assert.Equal(new CollectResult(0, 0, 1), result);
        using var db = testDb.NewContext();
        Assert.Equal("001", (await db.Notices.SingleAsync()).BidSeq);
    }

    [Fact]
    public async Task 기초금액은_이미_받은_공고에_채우고_같은_내용이면_다시_저장하지_않는다()
    {
        collector.Items = [Item("R1", "000", "공고 원문")];
        await RunAsync();
        collector.Items = [];
        collector.BasePrices = [Price("R1", 186_400_000, a: 7_420_000), Price("NONE", 1_000_000)];

        var first = await RunAsync();
        var second = await RunAsync();

        Assert.Equal(1, first.BasePricesUpdated);   // 아직 받지 못한 공고(NONE)는 건너뜀
        Assert.Equal(1, first.RawSaved);
        Assert.Equal(0, second.BasePricesUpdated);  // 같은 기초금액 원문은 다시 저장하지 않음
        using var db = testDb.NewContext();
        var n = await db.Notices.Include(x => x.Histories).SingleAsync();
        Assert.Equal((186_400_000L, 7_420_000L, "-3% ~ +3%"), (n.BasePrice!.Value, n.AValue!.Value, n.ReservePriceRange));
        Assert.Empty(n.Histories);                  // 기초금액 공개는 정정이 아니므로 이력 없음
        Assert.Equal(2, await db.ApiRawResponses.CountAsync()); // 공고 원문 1 + 기초금액 원문 1
    }

    [Fact]
    public async Task 정정_공고가_와도_따로_받은_기초금액을_지우지_않는다()
    {
        // 나라장터 공고 목록에는 기초금액이 없다 (BasePrice = null)
        collector.Items = [Item("R1", "000", "최초본") with { BasePrice = null }];
        collector.BasePrices = [Price("R1", 186_400_000, a: 7_420_000)];
        await RunAsync();

        collector.Items = [Item("R1", "001", "정정본") with { BasePrice = null }];
        collector.BasePrices = [];
        await RunAsync();

        using var db = testDb.NewContext();
        var n = await db.Notices.SingleAsync();
        Assert.Equal("001", n.BidSeq);
        Assert.Equal(186_400_000, n.BasePrice);
        Assert.Equal(7_420_000, n.AValue);
    }

    [Fact]
    public async Task 지워진_기초금액은_저장된_기초금액_원문에서_되살린다()
    {
        collector.Items = [Item("R1", "000", "공고")];
        await RunAsync();
        using (var db = testDb.NewContext())
        {
            // 버그로 기초금액만 지워진 상태를 만든다 (원문과 Hash는 남아 있음)
            var n = await db.Notices.SingleAsync();
            n.BasePrice = null;
            n.BasePriceHash = "X";
            db.ApiRawResponses.Add(new BidNote.Domain.Collecting.ApiRawResponse
            {
                SourceSystem = SourceSystems.G2B,
                BidNo = "R1",
                ApiName = BidNote.Infrastructure.G2B.G2BCollector.BasePriceApi,
                ResponseJson = """{"bidNtceNo":"R1","bidNtceOrd":"000","bssamt":"50000000","bidPrceCalclAYn":"N"}""",
            });
            await db.SaveChangesAsync();
        }

        int restored;
        using (var db = testDb.NewContext())
            restored = await BidNote.Infrastructure.Persistence.DatabaseInitializer.RestoreWipedBasePricesAsync(db, NullLogger.Instance, CancellationToken.None);

        Assert.Equal(1, restored);
        using var check = testDb.NewContext();
        Assert.Equal(50_000_000, (await check.Notices.SingleAsync()).BasePrice);
    }

    [Fact]
    public async Task 같은_수집에서_새로_받은_공고에도_기초금액을_바로_채운다()
    {
        collector.Items = [Item("R1", "000", "공고 원문")];
        collector.BasePrices = [Price("R1", 50_000_000)];

        var result = await RunAsync();

        Assert.Equal(1, result.BasePricesUpdated);
        using var db = testDb.NewContext();
        Assert.Equal(50_000_000, (await db.Notices.SingleAsync()).BasePrice);
    }

    private static CollectedBasePrice Price(string no, long basePrice, long? a = null) => new()
    {
        SourceSystem = SourceSystems.G2B,
        BusinessType = BusinessType.Construction,
        BidNo = no,
        BidSeq = "000",
        BasePrice = basePrice,
        AValue = a,
        ReservePriceRange = "-3% ~ +3%",
        ApiName = "bsis",
        RequestParameter = "",
        RawJson = "bsis-" + no + "-" + basePrice,
    };

    [Fact]
    public async Task 취소_공고는_비활성으로_바꾼다()
    {
        collector.Items = [Item("R1", "00", "원문A")];
        await RunAsync();

        collector.Items = [Item("R1", "00", "원문-취소", cancelled: true)];
        await RunAsync();

        using var db = testDb.NewContext();
        var notice = await db.Notices.Include(x => x.Histories).SingleAsync();
        Assert.False(notice.IsActive);
        Assert.Equal(NoticeChangeType.Cancelled, Assert.Single(notice.Histories).ChangeType);
    }

    [Fact]
    public async Task 면허_제한은_바뀐_원문대로_통째로_바꾼다()
    {
        collector.Items = [Item("R1", "00", "원문A") with { Licenses = [new(1, "토목공사업"), new(2, "포장공사업")] }];
        await RunAsync();
        collector.Items = [Item("R1", "01", "원문B") with { Licenses = [new(1, "실내건축공사업")] }];
        await RunAsync();

        using var db = testDb.NewContext();
        var notice = await db.Notices.Include(x => x.Licenses).SingleAsync();
        Assert.Equal(["실내건축공사업"], notice.Licenses.Select(l => l.Name));
        Assert.Equal("실내건축공사업", notice.License); // 주공종이 비면 면허 첫 줄을 대표 업종으로
        Assert.Equal(1, await db.NoticeLicenses.CountAsync());
        Assert.Contains("토목공사업", (await db.NoticeHistories.SingleAsync()).SnapshotJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 실제_모드로_바꾸면_가상_공고와_원문만_지운다()
    {
        collector.Items = [Item("R1", "00", "실제")];
        await RunAsync();
        collector.Items = [Item("FAKE-0001", "00", "가상") with { SourceSystem = SourceSystems.Fake, Licenses = [new(1, "토목공사업")] }];
        await RunAsync();

        using (var db = testDb.NewContext())
            await BidNote.Infrastructure.Persistence.DatabaseInitializer.PurgeFakeDataAsync(db, NullLogger.Instance, CancellationToken.None);

        using var check = testDb.NewContext();
        Assert.Equal(["R1"], await check.Notices.Select(x => x.BidNo).ToListAsync());
        Assert.Equal(["실제"], await check.ApiRawResponses.Select(x => x.ResponseJson).ToListAsync());
        Assert.Equal(0, await check.NoticeLicenses.CountAsync());
    }

    private static CollectedNotice Item(string no, string seq, string raw, DateTime? due = null, bool cancelled = false) => new()
    {
        SourceSystem = SourceSystems.G2B,
        BusinessType = BusinessType.Construction,
        BidNo = no,
        BidSeq = seq,
        Name = "공고 " + no,
        Agency = "○○시",
        BasePrice = 100_000_000,
        LowerLimitRate = 87.745,
        PostedAt = Now.AddDays(-2),
        DueAt = due ?? Now.AddDays(3),
        OpenAt = (due ?? Now.AddDays(3)).AddHours(1),
        LastModifiedAt = Now.AddHours(-1),
        IsCancelled = cancelled,
        ApiName = "test",
        RequestParameter = "",
        RawJson = raw,
    };

    private sealed class StubCollector : IBidCollector
    {
        public IReadOnlyList<CollectedNotice> Items { get; set; } = [];
        public string SourceSystem => "TEST";
        public IReadOnlyList<CollectedBasePrice> BasePrices { get; set; } = [];
        public Task<IReadOnlyList<CollectedNotice>> CollectAsync(DateTime from, DateTime to, CancellationToken cancellationToken) => Task.FromResult(Items);
        public Task<IReadOnlyList<CollectedBasePrice>> CollectBasePricesAsync(DateTime from, DateTime to, CancellationToken cancellationToken) => Task.FromResult(BasePrices);
    }
}
