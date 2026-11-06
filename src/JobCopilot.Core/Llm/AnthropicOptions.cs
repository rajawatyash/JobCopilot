namespace JobCopilot.Core.Llm;

public sealed class AnthropicOptions
{
    public const string Section = "Anthropic";

    /// <summary>Set via user-secrets or the ANTHROPIC__APIKEY env var. Never commit it.</summary>
    public string? ApiKey { get; set; }
    public string Model { get; set; } = "claude-sonnet-5-5";
    public int MaxTokens { get; set; } = 2000;
    public string BaseUrl { get; set; } = "https://api.anthropic.com/";
}
