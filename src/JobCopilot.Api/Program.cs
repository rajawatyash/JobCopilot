using JobCopilot.Core.Llm;
using JobCopilot.Core.Matching;
using JobCopilot.Core.Models;
using JobCopilot.Core.Services;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<AnthropicOptions>(builder.Configuration.GetSection(AnthropicOptions.Section));
builder.Services.AddHttpClient<ILlmAssessor, AnthropicAssessor>((sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<AnthropicOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(90);
});
builder.Services.AddSingleton<IKeywordMatcher, KeywordMatcher>();
builder.Services.AddScoped<IMatchService, MatchService>();
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();
if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/api/match", async (MatchRequest request, IMatchService matcher, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.CvText) || string.IsNullOrWhiteSpace(request.JobDescription))
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["request"] = ["Both cvText and jobDescription are required."],
        });

    return Results.Ok(await matcher.MatchAsync(request, ct));
})
.WithName("MatchCvToJob");

app.Run();

// Exposed so WebApplicationFactory<Program> can host the API in tests.
public partial class Program;
