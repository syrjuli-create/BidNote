namespace BidNote.Application.Home;

/// <summary>
/// 메인 화면 검색 조건(주소 쿼리)을 저장·비교할 수 있게 정리한다.
/// 아는 키만 남기고, 빈 값·기본값·페이지 번호는 빼며, 키 순서와 지역 순서를 고정한다.
/// </summary>
public static class SearchQuery
{
    public static string Normalize(string? rawQuery)
    {
        var values = Parse(rawQuery);
        var parts = new List<string>();
        void Add(string key, string? value)
        {
            if (!string.IsNullOrEmpty(value)) parts.Add(key + "=" + Uri.EscapeDataString(value));
        }

        Add("q", First(values, "q")?.Trim());
        foreach (string region in values.GetValueOrDefault("region", []).Select(x => x.Trim()).Where(x => x.Length > 0).Distinct().Order(StringComparer.Ordinal))
            Add("region", region);
        Add("license", First(values, "license")?.Trim());
        Add("contract", First(values, "contract")?.Trim());
        Add("nosu", Flag(values, "nosu"));
        Add("amount", First(values, "amount") is "s" or "m" or "l" ? First(values, "amount") : null);
        Add("due", int.TryParse(First(values, "due"), out int due) && due >= 0 ? due.ToString(System.Globalization.CultureInfo.InvariantCulture) : null);
        Add("corrected", Flag(values, "corrected"));
        Add("new", Flag(values, "new"));
        Add("sort", First(values, "sort") is "amount" or "new" ? First(values, "sort") : null);
        return string.Join("&", parts);
    }

    /// <summary>저장한 조건을 사람이 읽을 짧은 설명들로 바꾼다 (예: "경기", "1억 미만", "정정만").</summary>
    public static IReadOnlyList<string> Describe(string? query)
    {
        var values = Parse(Normalize(query));
        var items = new List<string>();
        if (First(values, "q") is { } q) items.Add($"'{q}'");
        if (values.TryGetValue("region", out var regions)) items.Add(string.Join("·", regions));
        if (First(values, "license") is { } license) items.Add(license);
        if (First(values, "contract") is { } contract) items.Add(contract);
        if (values.ContainsKey("nosu")) items.Add("수의계약 제외");
        items.AddRange(First(values, "amount") switch { "s" => ["1억 미만"], "m" => ["1억~5억"], "l" => ["5억 이상"], _ => Array.Empty<string>() });
        items.AddRange(First(values, "due") switch { "0" => ["오늘 마감"], "1" => ["내일까지"], { } d => [$"{d}일 이내"], _ => Array.Empty<string>() });
        if (values.ContainsKey("corrected")) items.Add("정정만");
        if (values.ContainsKey("new")) items.Add("새 공고만");
        items.AddRange(First(values, "sort") switch { "amount" => ["금액 높은순"], "new" => ["최신 공고순"], _ => Array.Empty<string>() });
        return items;
    }

    private static Dictionary<string, List<string>> Parse(string? rawQuery)
    {
        var values = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (string pair in (rawQuery ?? "").TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = pair.IndexOf('=');
            string key = Decode(eq < 0 ? pair : pair[..eq]);
            string value = eq < 0 ? "" : Decode(pair[(eq + 1)..]);
            if (!values.TryGetValue(key, out var list)) values[key] = list = [];
            list.Add(value);
        }
        return values;
    }

    private static string Decode(string s)
    {
        try { return Uri.UnescapeDataString(s.Replace('+', ' ')); }
        catch (UriFormatException) { return s; }
    }

    private static string? First(Dictionary<string, List<string>> values, string key) =>
        values.TryGetValue(key, out var list) ? list.Select(x => x.Trim()).FirstOrDefault(x => x.Length > 0) : null;

    private static string? Flag(Dictionary<string, List<string>> values, string key) =>
        values.TryGetValue(key, out var list) && list.Any(x => x.Equals("true", StringComparison.OrdinalIgnoreCase)) ? "true" : null;
}
