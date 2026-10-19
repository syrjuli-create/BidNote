using System.Globalization;

namespace BidNote.Web;

/// <summary>화면 표시용 숫자·날짜 형식.</summary>
public static class Display
{
    private static readonly CultureInfo Ko = CultureInfo.GetCultureInfo("ko-KR");

    public static string Won(long? amount) => amount is { } a ? a.ToString("N0", Ko) : "-";

    /// <summary>1억 이상은 "1.86억 원", 미만은 "9,430만 원".</summary>
    public static string Eok(long? amount)
    {
        if (amount is not { } a) return "미공개";
        if (a >= 100_000_000) return (a / 100_000_000m).ToString("0.##", Ko) + "억 원";
        return (a / 10_000).ToString("N0", Ko) + "만 원";
    }

    /// <summary>날짜 기준 남은 일수. 오늘 마감이면 D-day, 내일이면 D-1.</summary>
    public static string DDay(DateTime due, DateTime now)
    {
        int days = (due.Date - now.Date).Days;
        return days <= 0 ? "D-day" : "D-" + days;
    }

    /// <summary>D-day 배지 색 단계: 내일까지 hot, 3일 이내 warm.</summary>
    public static string DDayLevel(DateTime due, DateTime now) =>
        IsClosingSoon(due, now) ? "hot" : (due.Date - now.Date).Days <= 3 ? "warm" : "";

    public static bool IsClosingSoon(DateTime due, DateTime now) => due < BidNote.Application.Home.HomeRules.ClosingSoonLimit(now);

    public static string DateTimeText(DateTime value) => value.ToString("yyyy-MM-dd HH:mm", Ko);

    public static string ShortDateTime(DateTime value) => value.ToString("MM-dd HH:mm", Ko);

    /// <summary>API 이름을 화면용 이름으로 바꾼다. 모르는 이름은 그대로 보여 준다.</summary>
    public static string ApiLabel(string apiName) => apiName switch
    {
        "getBidPblancListInfoCnstwk" => "공사 공고 목록",
        "getBidPblancListInfoCnstwkBsisAmount" => "공사 기초금액",
        "getBidPblancListInfoLicenseLimit" => "면허 제한",
        "getBidPblancListInfoPrtcptPsblRgn" => "참가 가능 지역",
        BidNote.Infrastructure.G2B.FakeG2BCollector.ApiName => "가상 공고 (테스트)",
        _ => apiName,
    };

    public static string DateText(DateTime value) => value.ToString("yyyy년 M월 d일", Ko);
}
