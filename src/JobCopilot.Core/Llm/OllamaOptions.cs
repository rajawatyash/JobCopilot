namespace JobCopilot.Core.Llm;

public sealed class OllamaOptions
{
    public const string Section = "Ollama";

    public string BaseUrl { get; set; } = "http://localhost:11434/";

    /// <summary>Any model you have pulled, e.g. `ollama pull qwen2.5:7b`.</summary>
    public string Model { get; set; } = "qwen2.5:7b";

    /// <summary>Local models on a laptop CPU can be slow on long CVs.</summary>
    public int TimeoutSeconds { get; set; } = 300;
}
