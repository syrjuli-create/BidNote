namespace BidNote.Application.Company;

/// <summary>
/// 나라장터 면허 제한 이름과 다르게 입력한 면허 이름("전기공사")에 가까운 이름("전기공사업")을 찾는다.
/// 이름이 한 글자라도 다르면 공고와 맞춰 볼 수 없으므로, 등록하기 전에 고르게 한다.
/// </summary>
public static class LicenseNameSuggestions
{
    public const int DefaultMax = 3;

    /// <summary>
    /// 가까운 순서로 최대 <paramref name="max"/>개. 띄어쓰기·가운뎃점(ㆍ·)·마침표 차이만 있으면 맨 앞,
    /// 그다음 입력이 들어 있는 이름(짧은 것 먼저), 그다음 두 글자 이내로 다른 이름.
    /// </summary>
    public static IReadOnlyList<string> For(string input, IEnumerable<string> knownNames, int max = DefaultMax)
    {
        ArgumentNullException.ThrowIfNull(knownNames);
        string key = Key(input);
        if (key.Length == 0) return [];

        var scored = new List<(int Score, int Length, string Name)>();
        foreach (string name in knownNames.Distinct(StringComparer.Ordinal))
        {
            string k = Key(name);
            if (k.Length == 0) continue;
            int? score = k == key ? 0
                : k.Contains(key, StringComparison.Ordinal) ? 1
                : key.Contains(k, StringComparison.Ordinal) ? 2
                : Distance(k, key) is var d && d <= 2 ? 2 + d
                : null;
            if (score is { } s) scored.Add((s, k.Length, name));
        }
        return [.. scored.OrderBy(x => x.Score).ThenBy(x => x.Length).ThenBy(x => x.Name, StringComparer.Ordinal).Take(max).Select(x => x.Name)];
    }

    /// <summary>비교용 이름: 띄어쓰기·가운뎃점·마침표·쉼표를 뺀다.</summary>
    internal static string Key(string? name) =>
        new((name ?? "").Where(c => !char.IsWhiteSpace(c) && c is not ('ㆍ' or '·' or '.' or ',' or '･')).ToArray());

    private static int Distance(string a, string b)
    {
        if (Math.Abs(a.Length - b.Length) > 2) return int.MaxValue;
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) prev[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (int j = 1; j <= b.Length; j++)
                cur[j] = Math.Min(Math.Min(cur[j - 1], prev[j]) + 1, prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }
}
