using System.Diagnostics;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using JobCopilot.Core.Models;

namespace JobCopilot.Api;

/// <summary>
/// One frame of the cover-letter stream. Fields are nullable because the three
/// event types share the shape: a "delta" carries text, a "done" carries the
/// run's metadata, and an "error" carries a reason.
/// </summary>
public sealed record LetterEvent(
    string? Text = null,
    int? OverallScore = null,
    string? AiModel = null,
    string? WriterModel = null,
    long? ElapsedMs = null,
    string? Error = null);

internal static class CoverLetterStream
{
    public const string Delta = "delta";
    public const string Done = "done";
    public const string Error = "error";

    /// <summary>
    /// Turns the writer's text chunks into SSE frames, ending with one "done"
    /// frame, or one "error" frame if the provider failed part-way through.
    /// </summary>
    public static async IAsyncEnumerable<SseItem<LetterEvent>> Frames(
        CoverLetterDraft draft,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var started = Stopwatch.GetTimestamp();
        string? failure = null;

        // Enumerated by hand rather than with await foreach, because C# forbids
        // yielding from inside a try that has a catch, and a mid-stream provider
        // failure is exactly what has to be caught and reported as a frame: the
        // status code was already sent as 200 with the first delta.
        await using var chunks = draft.Chunks.GetAsyncEnumerator(ct);
        while (true)
        {
            try
            {
                if (!await chunks.MoveNextAsync())
                    break;
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or IOException
                                       && !ct.IsCancellationRequested)
            {
                failure = ex.Message;
                break;
            }

            yield return new SseItem<LetterEvent>(new LetterEvent(Text: chunks.Current), Delta);
        }

        yield return failure is not null
            ? new SseItem<LetterEvent>(new LetterEvent(Error: failure), Error)
            : new SseItem<LetterEvent>(
                new LetterEvent(
                    OverallScore: draft.Match.OverallScore,
                    AiModel: draft.Match.AiModel,
                    WriterModel: draft.WriterModel,
                    ElapsedMs: (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds),
                Done);
    }
}
