using JobCopilot.Core.Llm;
using JobCopilot.Core.Matching;
using JobCopilot.Core.Models;
using JobCopilot.Core.Services;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<LlmOptions>(builder.Configuration.GetSection(LlmOptions.Section));
builder.Services.Configure<AnthropicOptions>(builder.Configuration.GetSection(AnthropicOptions.Section));
builder.Services.Configure<OllamaOptions>(builder.Configuration.GetSection(OllamaOptions.Section));

builder.Services.AddHttpClient<AnthropicAssessor>((sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<AnthropicOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(90);
});
builder.Services.AddHttpClient<OllamaAssessor>((sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<OllamaOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
});

// Frontier or local model, chosen by Llm:Provider. The rest of the app only sees ILlmAssessor.
builder.Services.AddTransient<ILlmAssessor>(sp =>
    sp.GetRequiredService<IOptions<LlmOptions>>().Value.Provider switch
    {
        LlmProvider.Ollama => sp.GetRequiredService<OllamaAssessor>(),
        _ => sp.GetRequiredService<AnthropicAssessor>(),
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
