using JobCopilot.Core.Llm;

namespace JobCopilot.Tests;

public class AnthropicAssessorTests
{
    [Fact]
    public void Parses_structured_output_and_clamps_score()
    {
        const string response = """
        {
          "stop_reason": "end_turn",
          "content": [
            { "type": "text", "text": "{ \"score\": 140, \"summary\": \"Strong backend fit.\", \"strengths\": [\"Azure\"], \"gaps\": [\"Kafka\"], \"suggestedBullets\": [\"Built event-driven services\"] }" }
          ]
        }
        """;

        var result = AnthropicAssessor.ParseJsonOutput(response);

        Assert.Equal(100, result.Score);
        Assert.Equal(["Kafka"], result.Gaps);
    }

    [Fact]
    public void Throws_when_no_text_block()
    {
        const string response = """{ "stop_reason": "end_turn", "content": [] }""";
        Assert.Throws<InvalidOperationException>(() => AnthropicAssessor.ParseJsonOutput(response));
    }

    [Fact]
    public void Throws_when_text_is_not_json()
    {
        const string response = """{ "stop_reason": "end_turn", "content": [ { "type": "text", "text": "I'd be happy to help!" } ] }""";
        Assert.Throws<InvalidOperationException>(() => AnthropicAssessor.ParseJsonOutput(response));
    }

    // A refused or truncated response is valid JSON from the API but not a valid
    // assessment, so it must fail rather than be parsed into a zero score.
    [Theory]
    [InlineData("refusal")]
    [InlineData("max_tokens")]
    public void Throws_when_model_did_not_finish(string stopReason)
    {
        var response = $$"""{ "stop_reason": "{{stopReason}}", "content": [] }""";
        Assert.Throws<InvalidOperationException>(() => AnthropicAssessor.ParseJsonOutput(response));
    }
}
