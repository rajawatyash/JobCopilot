using System.Text.RegularExpressions;
using JobCopilot.Core.Models;

namespace JobCopilot.Core.Matching;

public interface IKeywordMatcher
{
    KeywordAnalysis Analyse(string cvText, string jobDescription);
}

public sealed class KeywordMatcher : IKeywordMatcher
{
    private readonly IReadOnlyDictionary<string, string[]> _skills;

    public KeywordMatcher() : this(SkillCatalog.Skills) { }

    public KeywordMatcher(IReadOnlyDictionary<string, string[]> skills) => _skills = skills;

    public KeywordAnalysis Analyse(string cvText, string jobDescription)
    {
        var required = FindSkills(jobDescription);
        if (required.Count == 0)
            return new KeywordAnalysis(0, [], []);

        var present = FindSkills(cvText);
        var matched = required.Where(present.Contains).Order().ToList();
        var missing = required.Except(present).Order().ToList();
        var coverage = (int)Math.Round(100.0 * matched.Count / required.Count);

        return new KeywordAnalysis(coverage, matched, missing);
    }

    private HashSet<string> FindSkills(string text)
    {
        var found = new HashSet<string>();
        foreach (var (skill, aliases) in _skills)
        {
            if (aliases.Any(alias => ContainsTerm(text, alias)))
                found.Add(skill);
        }
        return found;
    }

    // Word-boundary match that still works for terms like "c#" and ".net",
    // where \b would fail because the term starts or ends with a symbol.
    private static bool ContainsTerm(string text, string term) =>
        Regex.IsMatch(text, $@"(?<![\w]){Regex.Escape(term)}(?![\w])", RegexOptions.IgnoreCase);
}
