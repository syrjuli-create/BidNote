using System.Net;
using System.Text;
using System.Text.Json;
using BidNote.Application.Collecting;
using BidNote.Infrastructure.G2B;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BidNote.Tests;

public class G2BCollectorTests
{
    private const string Key = "SECRET-KEY-1234";
    private static readonly DateTime From = new(2026, 10, 18, 9, 0, 0), To = new(2026, 10, 19, 9, 0, 0);

    private static string Notice(string no, string kind = "등록공고") => JsonSerializer.Serialize(new Dictionary<string, string>
    {
        ["bidNtceNo"] = no,
        ["bidNtceOrd"] = "000",
        ["reNtceYn"] = "N",
        ["ntceKindNm"] = kind,
        ["bidNtceDt"] = "2026-10-18 11:29:47",
        ["bidNtceNm"] = "공고 " + no,
        ["ntceInsttNm"] = "○○시",
        ["dminsttNm"] = "○○시",
        ["bidClseDt"] = "2026-10-22 14:00:00",
        ["opengDt"] = "2026-10-22 16:00:00",
        ["sucsfbidLwltRate"] = "87.745",
        ["mainCnsttyNm"] = "",
        ["cnstrtsiteRgnNm"] = "대전광역시 서구",
        ["chgDt"] = "",
        ["bidNtceDtlUrl"] = "https://www.g2b.go.kr/x",
    });

    private static string License(string no, int grp, string name) =>
        $$"""{"bidNtceNo":"{{no}}","bidNtceOrd":"000","lmtGrpNo":"{{grp}}","lmtSno":"1","lcnsLmtNm":"{{name}}"}""";

    private static string Page(int total, params string[] items) =>
        "{\"response\":{\"header\":{\"resultCode\":\"00\",\"resultMsg\":\"정상\"},\"body\":{\"items\":[" + string.Join(",", items) + "],\"numOfRows\":2,\"pageNo\":1,\"totalCount\":" + total + "}}}";

    private sealed class StubHandler(Func<HttpRequestMessage, string> respond) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(respond(request), Encoding.UTF8, "application/json") });
        }
    }

    private sealed class LimitedUsage(int allowed) : IApiUsageTracker
    {
        public int Recorded { get; private set; }
        public Task<bool> CanCallAsync(string apiName, CancellationToken cancellationToken) => Task.FromResult(Recorded < allowed);
        public Task RecordAsync(string apiName, CancellationToken cancellationToken) { Recorded++; return Task.CompletedTask; }
        public Task<IReadOnlyList<ApiUsageToday>> GetTodayAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ApiUsageToday>>([]);
    }

    private static (G2BCollector Collector, StubHandler Handler) Create(Func<HttpRequestMessage, string> respond, IApiUsageTracker usage)
    {
        var handler = new StubHandler(respond);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/svc/") };
        var options = Options.Create(new G2BOptions { ServiceKey = Key, PageSize = 2, MaxPagesPerRun = 10 });
        var client = new G2BApiClient(http, options, usage, NullLogger<G2BApiClient>.Instance);
        return (new G2BCollector(client, NullLogger<G2BCollector>.Instance), handler);
    }

    [Fact]
    public async Task 페이지를_넘겨_모두_받고_면허를_공고에_붙이며_호출마다_사용량을_기록한다()
    {
        var usage = new LimitedUsage(100);
        var (collector, handler) = Create(req =>
        {
            string q = req.RequestUri!.Query;
            if (req.RequestUri.AbsolutePath.EndsWith(G2BCollector.LicenseApi, StringComparison.Ordinal))
                return Page(2, License("R2", 1, "실내건축공사업/4993"), License("R2", 2, "전기공사업/0037"));
            return q.Contains("pageNo=1&", StringComparison.Ordinal) ? Page(3, Notice("R1"), Notice("R2")) : Page(3, Notice("R3", "취소공고"));
        }, usage);

        var items = await collector.CollectAsync(From, To, CancellationToken.None);

        Assert.Equal(["R1", "R2", "R3"], items.Select(x => x.BidNo));
        Assert.Equal(3, usage.Recorded); // 목록 2페이지 + 면허 1페이지
        Assert.Equal(3, handler.Requests.Count);
        var r2 = items.Single(x => x.BidNo == "R2");
        Assert.Equal(["실내건축공사업", "전기공사업"], r2.Licenses.Select(l => l.Name));
        Assert.Equal("대전광역시", r2.Region);
        Assert.Equal("대전광역시 서구", r2.SiteRegionName);
        Assert.Equal(new DateTime(2026, 10, 22, 14, 0, 0), r2.DueAt);
        Assert.Equal(87.745, r2.LowerLimitRate);
        Assert.True(items.Single(x => x.BidNo == "R3").IsCancelled);
        Assert.All(items, x => Assert.DoesNotContain(Key, x.RequestParameter + x.RawJson, StringComparison.Ordinal));
    }

    [Fact]
    public async Task 하루_한도에_닿으면_호출하지_않는다()
    {
        var usage = new LimitedUsage(0);
        var (collector, handler) = Create(_ => Page(1, Notice("R1")), usage);

        var items = await collector.CollectAsync(From, To, CancellationToken.None);

        Assert.Empty(items);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task 결과코드가_정상이_아니면_키를_뺀_메시지로_예외를_낸다()
    {
        var (collector, _) = Create(_ => "{\"response\":{\"header\":{\"resultCode\":\"30\",\"resultMsg\":\"SERVICE KEY IS NOT REGISTERED " + Key + "\"}}}", new LimitedUsage(100));

        var ex = await Assert.ThrowsAsync<G2BApiException>(() => collector.CollectAsync(From, To, CancellationToken.None));

        Assert.Contains("결과코드 30", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Key, ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Y", 5_429_379L)]   // 2026-10-19 실제 응답(R26BK01774715) 값으로 계산
    [InlineData("N", null)]
    public void 기초금액_응답에서_A값을_더하고_A값_미적용_공고는_비운다(string applyA, long? expectedA)
    {
        var row = JsonDocument.Parse("""
            {"bidNtceNo":"R26BK01774715","bidNtceOrd":"000","bssamt":"104720000","bssamtOpenDt":"2026-10-16 10:26:26",
             "rsrvtnPrceRngBgnRate":"-3","rsrvtnPrceRngEndRate":"+3","sftyMngcst":"2261899","sftyChckMngcst":"2193972",
             "rtrfundNon":"201402","mrfnHealthInsrprm":"415940","npnInsrprm":"314801","odsnLngtrmrcprInsrprm":"41365",
             "envCnsrvcst":"0","qltyMngcst":"0","qltyMngcstAObjYn":"Y","bssAmtPurcnstcst":"","bidPrceCalclAYn":"APPLY"}
            """.Replace("APPLY", applyA, StringComparison.Ordinal)).RootElement;

        var p = G2BCollector.MapBasePrice(row, "q")!;

        Assert.Equal(104_720_000, p.BasePrice);
        Assert.Equal(expectedA, p.AValue);
        Assert.Null(p.NetCost);
        Assert.Equal("-3% ~ +3%", p.ReservePriceRange);
        Assert.Equal(new DateTime(2026, 10, 16, 10, 26, 26), p.OpenedAt);
    }

    [Fact]
    public void 기초금액이_0이면_건너뛴다()
    {
        var row = JsonDocument.Parse("""{"bidNtceNo":"R1","bidNtceOrd":"000","bssamt":"0"}""").RootElement;
        Assert.Null(G2BCollector.MapBasePrice(row, "q"));
    }

    [Fact]
    public void 서비스_키는_URL_인코딩해서_보낸다()
    {
        // 공공데이터포털 Decoding 키에는 +, /, = 가 들어갈 수 있다.
        Assert.Equal("a%2Bb%2Fc%3D", Uri.EscapeDataString("a+b/c="));
    }
}
