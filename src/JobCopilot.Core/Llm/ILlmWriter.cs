using JobCopilot.Core.Models;

namespace JobCopilot.Core.Llm;

/// <summary>
/// Separate from <see cref="ILlmAssessor"/> on purpose: assessing wants one
/// schema-shaped answer, writing wants a token stream. One interface carrying
/// both would force every provider to implement a mode it may not support.
/// </summary>
public interface ILlmWriter
{
    /// <summary>Provider and model, e.g. "ollama:qwen2.5:7b".</summary>
    string Name { get; }

    bool IsConfigured { get; }

    /// <summary>Text chunks in order. Chunk boundaries carry no meaning: concatenate them.</summary>
    IAsyncEnumerable<string> StreamAsync(CoverLetterContext context, CancellationToken ct);
}
