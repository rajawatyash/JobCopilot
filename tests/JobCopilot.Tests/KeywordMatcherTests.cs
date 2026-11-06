using JobCopilot.Core.Matching;

namespace JobCopilot.Tests;

public class KeywordMatcherTests
{
    private readonly KeywordMatcher _matcher = new();

    [Fact]
    public void Finds_matched_and_missing_skills()
    {
        var result = _matcher.Analyse(
            cvText: "Built APIs in C# on .NET 8 with Azure Service Bus.",
            jobDescription: "Looking for C#, .NET, Kafka and Kubernetes experience.");

        Assert.Equal([".NET", "C#"], result.Matched);
        Assert.Equal(["Kafka", "Kubernetes"], result.Missing);
        Assert.Equal(50, result.CoveragePercent);
    }

    [Theory]
    [InlineData("I know C#", "C#")]
    [InlineData("worked with dotnet", ".NET")]
    [InlineData("deployed to AKS", "Kubernetes")]
    public void Recognises_aliases(string cv, string skill)
    {
        var result = _matcher.Analyse(cv, $"Must have {skill}");
        Assert.Contains(skill, result.Matched);
    }

    [Fact]
    public void Does_not_match_inside_other_words()
    {
        // "react" must not match "reactive", and "sql" must not match "nosql".
        var result = _matcher.Analyse("reactive streams and nosql stores", "React and SQL required");
        Assert.Empty(result.Matched);
    }

    [Fact]
    public void Empty_job_description_gives_zero()
    {
        var result = _matcher.Analyse("C# developer", "We like nice people.");
        Assert.Equal(0, result.CoveragePercent);
    }
}
