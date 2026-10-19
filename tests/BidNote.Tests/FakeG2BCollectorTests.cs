using BidNote.Domain.Notices;
using BidNote.Infrastructure.G2B;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BidNote.Tests;

public class FakeG2BCollectorTests
{
    private static readonly DateTime Now = new(2026, 10, 19, 9, 0, 0);

    [Fact]
    public async Task 가상_공고는_켠_날을_기준으로_아직_마감되지_않은_공사_공고다()
    {
        var collector = new FakeG2BCollector(Options.Create(new G2BOptions { ServiceKey = "FAKE-KEY" }), new TestClock(Now), new AlwaysAllowUsage(), NullLogger<FakeG2BCollector>.Instance);

        var items = await collector.CollectAsync(Now.AddDays(-7), Now, CancellationToken.None);

        Assert.Equal(12, items.Count);
        Assert.All(items, x => Assert.Equal(BusinessType.Construction, x.BusinessType));
        Assert.All(items, x => Assert.True(x.DueAt > Now));
        Assert.All(items, x => Assert.Contains("\"fake\":true", x.RawJson, StringComparison.Ordinal));
        Assert.Equal(items.Count, items.Select(x => x.BidNo).Distinct().Count());
    }

    [Fact]
    public void 서비스_키가_비어_있으면_알기_쉬운_예외를_낸다()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new FakeG2BCollector(Options.Create(new G2BOptions { ServiceKey = "" }), new TestClock(Now), new AlwaysAllowUsage(), NullLogger<FakeG2BCollector>.Instance));
        Assert.Contains("G2B:ServiceKey", ex.Message, StringComparison.Ordinal);
    }
}
