using System.Globalization;
using System.Text.Json;
using BidNote.Domain.Notices;

namespace BidNote.Application.Notices;

/// <summary>
/// 변경 이력 스냅샷끼리 비교해 "무엇이 어떻게 바뀌었는지"를 만든다.
/// 기초금액은 공고 뒤에 따로 공개되어 정정과 상관없이 바뀌므로 비교하지 않는다.
/// </summary>
public static class NoticeHistoryDiff
{
    private static readonly (string Field, string Label)[] Fields =
    [
        ("Name", "공고명"),
        ("DueAt", "입찰 마감"),
        ("OpenAt", "개찰 일시"),
        ("LowerLimitRate", "낙찰하한율"),
        ("Agency", "발주기관"),
        ("SiteRegionName", "현장 지역"),
        ("Licenses", "면허 제한"),
        ("IsActive", "상태"),
    ];

    /// <param name="histories">오래된 것부터. 각 스냅샷은 바뀌기 직전 상태다.</param>
    /// <param name="currentSnapshot">지금 상태의 스냅샷.</param>
    /// <returns>최근 변경이 앞에 오는 목록.</returns>
    public static IReadOnlyList<NoticeChange> Build(IReadOnlyList<(DateTime DetectedAt, NoticeChangeType Type, string Snapshot)> histories, string currentSnapshot)
    {
        ArgumentNullException.ThrowIfNull(histories);
        var result = new List<NoticeChange>(histories.Count);
        for (int i = 0; i < histories.Count; i++)
        {
            string after = i + 1 < histories.Count ? histories[i + 1].Snapshot : currentSnapshot;
            using var before = JsonDocument.Parse(histories[i].Snapshot);
            using var next = JsonDocument.Parse(after);
            var changes = Fields
                .Select(f => (f.Label, Before: Text(before.RootElement, f.Field), After: Text(next.RootElement, f.Field)))
                .Where(x => x.Before != x.After)
                .Select(x => new FieldChange(x.Label, x.Before, x.After))
                .ToList();
            result.Add(new NoticeChange(histories[i].DetectedAt, histories[i].Type, Text(before.RootElement, "BidSeq"), Text(next.RootElement, "BidSeq"), changes));
        }
        result.Reverse();
        return result;
    }

    private static string Text(JsonElement root, string field)
    {
        if (!root.TryGetProperty(field, out var v)) return "";
        return (field, v.ValueKind) switch
        {
            (_, JsonValueKind.Null) => "없음",
            ("IsActive", JsonValueKind.True) => "진행",
            ("IsActive", JsonValueKind.False) => "취소",
            ("Licenses", JsonValueKind.Array) => string.Join(", ", v.EnumerateArray().Select(e => e.TryGetProperty("Name", out var n) ? n.GetString() : "")),
            ("LowerLimitRate", JsonValueKind.Number) => v.GetDouble().ToString("0.###", CultureInfo.InvariantCulture) + "%",
            (_, JsonValueKind.String) when DateTime.TryParse(v.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                && field.EndsWith("At", StringComparison.Ordinal) => d.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            _ => v.ToString(),
        };
    }
}
