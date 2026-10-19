using System.Threading.Channels;

namespace BidNote.Web.Services;

/// <summary>화면에서 "과거 기간 다시 받기"를 누르면 백그라운드 수집기에 바로 일을 넘긴다.</summary>
public sealed class CollectorSignal
{
    private readonly Channel<int> requests = Channel.CreateBounded<int>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest });

    /// <summary>최근 <paramref name="days"/>일치를 다시 받아 달라고 요청한다. 대기 중인 요청이 있으면 새 요청으로 바꾼다.</summary>
    public void RequestBackfill(int days) => requests.Writer.TryWrite(days);

    public bool IsPending => requests.Reader.Count > 0;

    internal ChannelReader<int> Reader => requests.Reader;
}
