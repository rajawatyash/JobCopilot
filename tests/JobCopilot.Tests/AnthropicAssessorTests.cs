using JobCopilot.Core.Llm;

namespace JobCopilot.Tests;

public class AnthropicAssessorTests
{
    [Fact]
    public void Parses_tool_use_block_and_clamps_score()
    {
        const string response = """
        {
          "content": [
            { "type": "text", "text": "Here you go" },
            { "type": "tool_use", "name": "record_assessment", "input": {
                "score": 140, "summary": "Strong backend fit.",
                "strengths": ["Azure"], "gaps": ["Kafka"], "suggestedBullets": ["Built event-driven services"] } }
          ]
        }
        """;

        var result = AnthropicAssessor.ParseToolInput(response);

        Assert.Equal(100, result.Score);
        Assert.Equal(["Kafka"], result.Gaps);
    }

    [Fact]
    public void Throws_when_no_tool_use_block()
    {
        const string response = """{ "content": [ { "type": "text", "text": "no tool" } ] }""";
        Assert.Throws<InvalidOperationException>(() => AnthropicAssessor.ParseToolInput(response));
    }
}
