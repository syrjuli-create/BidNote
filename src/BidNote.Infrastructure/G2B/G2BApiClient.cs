using System.Globalization;
using System.Text.Json;
using BidNote.Application.Collecting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BidNote.Infrastructure.G2B;

/// <summary>한 오퍼레이션을 기간으로 조회한 결과. 한도나 최대 페이지에 걸려 다 받지 못하면 Complete가 false다.</summary>
public sealed record G2BFetch(IReadOnlyList<JsonElement> Items, int TotalCount, bool Complete, string RequestParameter);

public sealed class G2BApiException(string message) : Exception(message);

/// <summary>
/// 나라장터 입찰공고정보서비스·낙찰정보서비스 호출. 요청마다 사용량을 기록하고 하루 한도에 가까우면 멈춘다.
/// 서비스 키는 로그·예외·저장 원문 어디에도 남기지 않는다.
/// </summary>
public sealed class G2BApiClient(HttpClient http, IOptions<G2BOptions> options, IApiUsageTracker usage, ILogger<G2BApiClient> logger)
{
    /// <summary>"inqryDiv=1&amp;inqryBgnDt=…&amp;inqryEndDt=…" 기간 조회 조건.</summary>
    public static string Window(DateTime from, DateTime to) =>
        $"inqryDiv=1&inqryBgnDt={from.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture)}&inqryEndDt={to.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture)}";

    /// <summary>입찰공고정보서비스를 기간으로 조회한다.</summary>
    public Task<G2BFetch> FetchAllAsync(string operation, DateTime from, DateTime to, CancellationToken cancellationToken) =>
        FetchPagesAsync(new Uri(operation, UriKind.Relative), operation, Window(from, to), cancellationToken);

    /// <summary>낙찰정보서비스를 조회한다. <paramref name="parameters"/>는 조회 조건(inqryDiv 등)이다.</summary>
    public Task<G2BFetch> FetchAwardAsync(string operation, string parameters, CancellationToken cancellationToken) =>
        FetchPagesAsync(new Uri(new Uri(options.Value.AwardBaseUrl), operation), operation, parameters, cancellationToken);

    private async Task<G2BFetch> FetchPagesAsync(Uri endpoint, string operation, string parameters, CancellationToken cancellationToken)
    {
        var o = options.Value;
        var items = new List<JsonElement>();
        int total = 0;

        for (int page = 1; ; page++)
        {
            if (page > o.MaxPagesPerRun)
            {
                logger.LogWarning("{Operation}: 최대 {Max}페이지까지만 받았습니다. 나머지는 다음 수집에서 받습니다.", operation, o.MaxPagesPerRun);
                return new G2BFetch(items, total, false, parameters);
            }
            if (!await usage.CanCallAsync(operation, cancellationToken).ConfigureAwait(false))
                return new G2BFetch(items, total, false, parameters);

            await usage.RecordAsync(operation, cancellationToken).ConfigureAwait(false);
            string query = $"serviceKey={Uri.EscapeDataString(o.ServiceKey)}&pageNo={page}&numOfRows={o.PageSize}&type=json&{parameters}";
            using var response = await http.GetAsync(new Uri(endpoint.OriginalString + "?" + query, endpoint.IsAbsoluteUri ? UriKind.Absolute : UriKind.Relative), cancellationToken).ConfigureAwait(false);
            string body = (await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)).Replace(o.ServiceKey, "***", StringComparison.Ordinal);

            var (pageItems, pageTotal) = Parse(operation, (int)response.StatusCode, body);
            items.AddRange(pageItems);
            total = pageTotal;
            if (pageItems.Count < o.PageSize || page * o.PageSize >= total)
                return new G2BFetch(items, total, true, parameters);
        }
    }

    internal static (List<JsonElement> Items, int Total) Parse(string operation, int status, string body)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            // 키 오류 등은 type=json이어도 XML로 올 때가 있다.
            throw new G2BApiException($"{operation}: 응답을 해석할 수 없습니다 (HTTP {status}). {Shorten(body)}");
        }

        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("response", out var res))
                throw new G2BApiException($"{operation}: 예상과 다른 응답입니다 (HTTP {status}). {Shorten(body)}");

            var header = res.GetProperty("header");
            string code = header.TryGetProperty("resultCode", out var c) ? c.GetString() ?? "" : "";
            if (code != "00")
            {
                string msg = header.TryGetProperty("resultMsg", out var m) ? m.GetString() ?? "" : "";
                throw new G2BApiException($"{operation}: {msg} (결과코드 {code})");
            }

            var body2 = res.GetProperty("body");
            int total = body2.TryGetProperty("totalCount", out var t) && t.ValueKind == JsonValueKind.Number ? t.GetInt32() : 0;
            var list = new List<JsonElement>();
            if (body2.TryGetProperty("items", out var items))
            {
                // items는 배열, {item: 배열|객체}, 또는 빈 문자열로 온다.
                var node = items.ValueKind == JsonValueKind.Object && items.TryGetProperty("item", out var inner) ? inner : items;
                if (node.ValueKind == JsonValueKind.Array) list.AddRange(node.EnumerateArray().Select(e => e.Clone()));
                else if (node.ValueKind == JsonValueKind.Object) list.Add(node.Clone());
            }
            return (list, total);
        }
    }

    private static string Shorten(string s) => s.Length <= 200 ? s : s[..200];
}
