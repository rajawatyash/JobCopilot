namespace JobCopilot.Core.Llm;

public enum LlmProvider
{
    Anthropic,
    Ollama,
}

public sealed class LlmOptions
{
    public const string Section = "Llm";

    /// <summary>Which model backs the AI assessment. Switch in appsettings or with LLM__PROVIDER=Ollama.</summary>
    public LlmProvider Provider { get; set; } = LlmProvider.Anthropic;
}
