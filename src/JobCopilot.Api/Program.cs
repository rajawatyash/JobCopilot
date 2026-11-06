using System.Text.Json;
using System.Text.Json.Serialization;
using JobCopilot.Api;
using Microsoft.AspNetCore.Diagnostics;
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

// The writers get no overall timeout: HttpClient.Timeout covers the whole
// response, which for a stream means the whole letter, so a sensible value for
// a request is a wrong value for a stream. The caller's cancellation token ends
// these instead.
builder.Services.AddHttpClient<AnthropicWriter>((sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<AnthropicOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
    client.Timeout = Timeout.InfiniteTimeSpan;
});
builder.Services.AddHttpClient<OllamaWriter>((sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<OllamaOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
    client.Timeout = Timeout.InfiniteTimeSpan;
});

// Frontier or local model, chosen by Llm:Provider. The rest of the app only
// sees ILlmAssessor and ILlmWriter.
builder.Services.AddTransient<ILlmAssessor>(sp =>
    sp.GetRequiredService<IOptions<LlmOptions>>().Value.Provider switch
    {
        LlmProvider.Ollama => sp.GetRequiredService<OllamaAssessor>(),
        _ => sp.GetRequiredService<AnthropicAssessor>(),
    });
builder.Services.AddTransient<ILlmWriter>(sp =>
    sp.GetRequiredService<IOptions<LlmOptions>>().Value.Provider switch
    {
        LlmProvider.Ollama => sp.GetRequiredService<OllamaWriter>(),
        _ => sp.GetRequiredService<AnthropicWriter>(),
    });
builder.Services.AddSingleton<IKeywordMatcher, KeywordMatcher>();
builder.Services.AddScoped<IMatchService, MatchService>();
builder.Services.AddScoped<ICoverLetterService, CoverLetterService>();
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

// Tone arrives as a name ("Warm"), not a number, and null frame fields stay out
// of the SSE payloads.
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

var app = builder.Build();

// A malformed body - bad JSON, or a tone that is not one of the four names - is
// the caller's mistake. Without this the JsonException thrown during model
// binding comes back as a 500.
app.UseExceptionHandler(new ExceptionHandlerOptions
{
    StatusCodeSelector = ex => ex switch
    {
        // Minimal APIs wrap a binding failure, so the inner exception is where
        // the JSON error actually is.
        BadHttpRequestException bad => bad.StatusCode,
        JsonException => StatusCodes.Status400BadRequest,
        { InnerException: JsonException } => StatusCodes.Status400BadRequest,
        _ => StatusCodes.Status500InternalServerError,
    },
});
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

app.MapPost("/api/cover-letter", async Task<IResult> (
    CoverLetterRequest request,
    ICoverLetterService letters,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.CvText) || string.IsNullOrWhiteSpace(request.JobDescription))
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["request"] = ["Both cvText and jobDescription are required."],
        });

    // Validation and the grounding pass both happen before the response starts,
    // because they are the last point where a failure can still be a status
    // code. Once the first frame is written the response is a 200 for good, so
    // everything after this is reported inside the stream.
    CoverLetterDraft draft;
    try
    {
        draft = await letters.StartAsync(request, ct);
    }
    catch (InvalidOperationException ex)
    {
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    return TypedResults.ServerSentEvents(CoverLetterStream.Frames(draft, ct));
})
.WithName("WriteCoverLetter");

app.Run();

// Exposed so WebApplicationFactory<Program> can host the API in tests.
public partial class Program;
