using BidNote.Application.Collecting;

namespace BidNote.Web.Services;

public sealed record CollectRun(DateTime At, string Trigger, CollectResult? Result, string? Error);

/// <summary>최근 수집 결과를 메모리에 보관한다 (프로그램을 끄면 지워진다). 수집 현황 화면용.</summary>
public sealed class CollectorState
{
    private const int Keep = 20;
    private readonly Lock gate = new();
    private readonly LinkedList<CollectRun> runs = new();

    public void Record(CollectRun run)
    {
        lock (gate)
        {
            runs.AddFirst(run);
            while (runs.Count > Keep) runs.RemoveLast();
        }
    }

    public IReadOnlyList<CollectRun> Recent()
    {
        lock (gate) return [.. runs];
    }
}
