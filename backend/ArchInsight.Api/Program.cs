using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using ArchInsight.AI.Contracts;
using ArchInsight.AI.Services;
using ArchInsight.Api;
using ArchInsight.Application;
using ArchInsight.Domain;
using ArchInsight.Infrastructure;
using MediatR;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy.WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:3000"]).AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddMediatR(config => config.RegisterServicesFromAssembly(typeof(CreateAssessmentCommand).Assembly));
builder.Services.AddScoped<FluentValidation.IValidator<CreateAssessmentCommand>, CreateAssessmentValidator>();
builder.Services.AddScoped<IAssessmentRepository, EfAssessmentRepository>();
builder.Services.AddSingleton<IAssessmentQueue, AssessmentQueue>();
builder.Services.AddSingleton<AssessmentEngine>();
builder.Services.AddSingleton<IArtifactStorage>(_ => new JsonArtifactStorage(builder.Environment.ContentRootPath));
builder.Services.AddSingleton(new AIProviderOptions(
    builder.Configuration["AI:AzureOpenAI:ChatCompletionsUrl"],
    builder.Configuration["AI:AzureOpenAI:ApiKey"],
    builder.Configuration["AI:AzureOpenAI:Deployment"],
    builder.Configuration["AI:OpenAI:ApiKey"],
    builder.Configuration["AI:OpenAI:Model"]));
builder.Services.AddHttpClient<IAIInsightGenerator, OpenAiInsightGenerator>(client => client.Timeout = TimeSpan.FromSeconds(90));
builder.Services.AddSingleton<IAIInsightQueue, AIInsightQueue>();
var provider = builder.Configuration["Database:Provider"] ?? "Sqlite";
if (provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
{
    var connection = builder.Configuration.GetConnectionString("SqlServer") ?? throw new InvalidOperationException("Configure ConnectionStrings:SqlServer to use SQL Server.");
    builder.Services.AddDbContext<AssessmentDbContext>(options => options.UseSqlServer(connection));
}
else
{
    var connection = builder.Configuration.GetConnectionString("Sqlite") ?? "Data Source=archinsight.db";
    builder.Services.AddDbContext<AssessmentDbContext>(options => options.UseSqlite(connection));
}
builder.Services.AddHostedService<AssessmentWorker>();
builder.Services.AddHostedService<AIInsightWorker>();

var app = builder.Build();
app.UseCors();
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
app.MapGet("/api/assessments", async (IAssessmentRepository repository, CancellationToken ct) => Results.Ok((await repository.ListAsync(ct)).Select(AssessmentMapping.Summary)));
app.MapGet("/api/assessments/{id:guid}", async (Guid id, IAssessmentRepository repository, CancellationToken ct) =>
{
    var item = await repository.FindAsync(id, ct);
    return item is null ? Results.NotFound() : Results.Ok(AssessmentMapping.Detail(item));
});
app.MapGet("/api/assessments/compare", async (Guid[] id, IAssessmentRepository repository, IAIInsightGenerator generator, CancellationToken ct) =>
{
    if (id.Length != 2) return Results.BadRequest(new { error = "Pass exactly two assessment IDs using repeated id query parameters." });
    var baseline = await repository.FindAsync(id[0], ct);
    var comparisonAssessment = await repository.FindAsync(id[1], ct);
    if (baseline is null || comparisonAssessment is null) return Results.NotFound(new { error = "One or both assessments were not found." });
    AssessmentComparisonView comparison;
    try { comparison = AssessmentComparisonService.Compare(baseline, comparisonAssessment); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    if (generator.IsConfigured)
    {
        try
        {
            var explanation = await generator.ExplainComparisonAsync(baseline, comparisonAssessment, JsonSerializer.Serialize(comparison), ct);
            comparison = comparison with { AIExplanation = explanation.Answer };
        }
        catch (Exception ex) { comparison = comparison with { AIExplanationError = ex.Message }; }
    }
    return Results.Ok(comparison);
});
app.MapPost("/api/assessments", async (CreateAssessmentCommand command, ISender sender, CancellationToken ct) =>
{
    try { var created = await sender.Send(command, ct); return Results.Created($"/api/assessments/{created.Id}", created); }
    catch (FluentValidation.ValidationException ex) { return Results.ValidationProblem(ex.Errors.GroupBy(e => e.PropertyName).ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray())); }
});
app.MapPost("/api/artifacts", async (Guid assessmentId, ArtifactType type, IFormFile file, IAssessmentRepository repository, IArtifactStorage storage, CancellationToken ct) =>
{
    if (file.Length is <= 0 or > 2_097_152) return Results.BadRequest(new { error = "JSON artifacts must be between 1 byte and 2 MB." });
    if (!Path.GetExtension(file.FileName).Equals(".json", StringComparison.OrdinalIgnoreCase)) return Results.BadRequest(new { error = "Only .json files are supported." });
    var assessment = await repository.FindAsync(assessmentId, ct);
    if (assessment is null) return Results.NotFound(new { error = "Assessment not found." });
    if (assessment.Status != AssessmentStatus.Pending) return Results.Conflict(new { error = "Artifacts can only be added before the assessment is queued." });
    try
    {
        await using var stream = file.OpenReadStream();
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var metadata = JsonSerializer.Serialize(document.RootElement);
        await using var storageStream = file.OpenReadStream();
        var storagePath = await storage.StoreAsync(assessment.Id, Path.GetFileName(file.FileName), storageStream, ct);
        var artifact = new AssessmentArtifact { AssessmentId = assessment.Id, ArtifactType = type, OriginalFileName = Path.GetFileName(file.FileName), StoragePath = storagePath, MetadataJson = metadata };
        assessment.Artifacts.Add(artifact);
        await repository.SaveChangesAsync(ct);
        return Results.Created($"/api/assessments/{assessment.Id}", new { artifact.Id, artifact.OriginalFileName, artifact.ArtifactType, artifact.UploadedDate });
    }
    catch (JsonException) { return Results.BadRequest(new { error = "The uploaded file is not valid JSON." }); }
});
app.MapPost("/api/assessments/{id:guid}/run", async (Guid id, ISender sender, CancellationToken ct) =>
{
    try { return await sender.Send(new QueueAssessmentCommand(id), ct) ? Results.Accepted($"/api/assessments/{id}", new { id, status = AssessmentStatus.Running }) : Results.Conflict(new { error = "Assessment was not found or has already run." }); }
    catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
});
app.MapPost("/api/assessments/{id:guid}/insights/regenerate", async (Guid id, IAssessmentRepository repository, IAIInsightGenerator generator, IAIInsightQueue queue, CancellationToken ct) =>
{
    if (!generator.IsConfigured) return Results.Problem("Configure Azure OpenAI or OpenAI credentials before generating insights.", statusCode: StatusCodes.Status503ServiceUnavailable);
    var assessment = await repository.FindAsync(id, ct);
    if (assessment is null) return Results.NotFound(new { error = "Assessment not found." });
    if (assessment.Status != AssessmentStatus.Completed || assessment.Result is null) return Results.Conflict(new { error = "Insights require a completed deterministic assessment." });
    if (assessment.Insights.Any(i => i.Status is AIInsightStatus.Pending or AIInsightStatus.Running)) return Results.Conflict(new { error = "An insight generation is already in progress." });
    var insight = new AssessmentInsight { AssessmentId = assessment.Id };
    assessment.Insights.Add(insight);
    await repository.SaveChangesAsync(ct);
    await queue.EnqueueAsync(assessment.Id, ct);
    return Results.Accepted($"/api/assessments/{id}", new { insightId = insight.Id, status = insight.Status });
});
app.MapPost("/api/assessments/{id:guid}/ask", async (Guid id, AskRequest request, IAssessmentRepository repository, IAIInsightGenerator generator, CancellationToken ct) =>
{
    var question = request.Question?.Trim();
    if (string.IsNullOrWhiteSpace(question) || question.Length > 1000) return Results.BadRequest(new { error = "Question must contain 1–1000 characters." });
    var assessment = await repository.FindAsync(id, ct);
    if (assessment is null) return Results.NotFound(new { error = "Assessment not found." });
    if (assessment.Status != AssessmentStatus.Completed || assessment.Result is null) return Results.Conflict(new { error = "Architecture questions require a completed assessment." });
    var record = new AIQuestion { AssessmentId = assessment.Id, Question = question, Status = generator.IsConfigured ? AIQuestionStatus.Pending : AIQuestionStatus.NotConfigured, ErrorMessage = generator.IsConfigured ? null : "Configure Azure OpenAI or OpenAI credentials to ask architecture questions." };
    assessment.AIQuestions.Add(record);
    await repository.SaveChangesAsync(ct);
    if (!generator.IsConfigured) return Results.Problem(record.ErrorMessage, statusCode: StatusCodes.Status503ServiceUnavailable, extensions: new Dictionary<string, object?> { ["questionId"] = record.Id });
    try
    {
        record.Status = AIQuestionStatus.Running;
        await repository.SaveChangesAsync(ct);
        var insight = assessment.Insights.OrderByDescending(i => i.CreatedDate).FirstOrDefault(i => i.Status == AIInsightStatus.Completed);
        var answer = await generator.AnswerQuestionAsync(assessment, insight, question, ct);
        record.Answer = answer.Answer; record.ModelName = answer.ModelName; record.PromptVersion = answer.PromptVersion; record.TokenUsage = answer.TokenUsage;
        record.Status = AIQuestionStatus.Completed;
        await repository.SaveChangesAsync(ct);
        return Results.Created($"/api/assessments/{id}", new AIQuestionView(record.Id, record.Question, record.Answer, record.Status, record.AskedAt, record.ModelName, record.PromptVersion, record.TokenUsage, record.ErrorMessage));
    }
    catch (Exception ex)
    {
        record.Status = AIQuestionStatus.Failed; record.ErrorMessage = ex.Message;
        await repository.SaveChangesAsync(ct);
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway, extensions: new Dictionary<string, object?> { ["questionId"] = record.Id });
    }
});

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AssessmentDbContext>();
    await db.Database.EnsureCreatedAsync();
    await Phase3SchemaUpgrade.ApplyAsync(db);
    await SeedData.InitializeAsync(scope.ServiceProvider);
    var repo = scope.ServiceProvider.GetRequiredService<IAssessmentRepository>();
    var queue = scope.ServiceProvider.GetRequiredService<IAssessmentQueue>();
    var insightQueue = scope.ServiceProvider.GetRequiredService<IAIInsightQueue>();
    var insightGenerator = scope.ServiceProvider.GetRequiredService<IAIInsightGenerator>();
    foreach (var item in await repo.ListAsync(CancellationToken.None))
    {
        if (item.Status is AssessmentStatus.Pending or AssessmentStatus.Running) await queue.EnqueueAsync(item.Id, CancellationToken.None);
        foreach (var insight in item.Insights.Where(i => i.Status is AIInsightStatus.Pending or AIInsightStatus.Running)) await insightQueue.EnqueueAsync(item.Id, CancellationToken.None);
        if (insightGenerator.IsConfigured && item.Status == AssessmentStatus.Completed && item.Result is not null && !item.Insights.Any(i => i.Status is AIInsightStatus.Pending or AIInsightStatus.Running or AIInsightStatus.Completed))
        {
            item.Insights.Add(new AssessmentInsight { AssessmentId = item.Id });
            await repo.SaveChangesAsync(CancellationToken.None);
            await insightQueue.EnqueueAsync(item.Id, CancellationToken.None);
        }
    }
}
app.Run();

public sealed class AssessmentQueue : IAssessmentQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>();
    public ValueTask EnqueueAsync(Guid id, CancellationToken ct) => _channel.Writer.WriteAsync(id, ct);
    public ValueTask<Guid> DequeueAsync(CancellationToken ct) => _channel.Reader.ReadAsync(ct);
}

public sealed class AssessmentWorker(IServiceScopeFactory scopes, IAssessmentQueue queue, IAIInsightQueue insightQueue, ILogger<AssessmentWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            Guid id;
            try { id = await queue.DequeueAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            await using var scope = scopes.CreateAsyncScope();
            var repository = scope.ServiceProvider.GetRequiredService<IAssessmentRepository>();
            var engine = scope.ServiceProvider.GetRequiredService<AssessmentEngine>();
            var assessment = await repository.FindAsync(id, stoppingToken);
            if (assessment is null) continue;
            try
            {
                assessment.Status = AssessmentStatus.Running;
                await repository.SaveChangesAsync(stoppingToken);
                assessment.Result = engine.Execute(assessment);
                assessment.Result.AssessmentId = assessment.Id;
                assessment.Status = AssessmentStatus.Completed;
                assessment.ErrorMessage = null;
            }
            catch (Exception ex)
            {
                assessment.Status = AssessmentStatus.Failed;
                assessment.ErrorMessage = ex.Message;
                logger.LogWarning(ex, "Assessment {AssessmentId} failed", id);
            }
            await repository.SaveChangesAsync(stoppingToken);
            if (assessment.Status == AssessmentStatus.Completed && assessment.Result is not null && !assessment.Insights.Any(i => i.Status is AIInsightStatus.Pending or AIInsightStatus.Running or AIInsightStatus.Completed))
            {
                assessment.Insights.Add(new AssessmentInsight { AssessmentId = assessment.Id });
                await repository.SaveChangesAsync(stoppingToken);
                await insightQueue.EnqueueAsync(assessment.Id, stoppingToken);
            }
        }
    }
}

public sealed record AskRequest(string Question);

public static class SeedData
{
    public static async Task InitializeAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<AssessmentDbContext>();
        if (await db.Assessments.AnyAsync()) return;
        var engine = services.GetRequiredService<AssessmentEngine>();
        var samples = new[]
        {
        (
            new Assessment { Name = "Northstar modernization baseline", OrganizationName = "Northstar Group", AssessmentType = AssessmentType.Modernization },
            "technology-inventory.json",
            """{"applications":[{"name":"Customer Portal","framework":".NET Framework 4.7.2","database":"SQL Server 2012","hosting":"IIS","containerized":false,"architecture":"monolith"},{"name":"Order API","framework":".NET 8","database":"SQL Server 2019","hosting":"IIS","containerized":false},{"name":"Billing Service","framework":".NET 6","database":"SQL Server 2019","hosting":"IIS","containerized":false},{"name":"Reporting Portal","framework":".NET Framework 4.8","database":"SQL Server 2012","hosting":"IIS","containerized":false}]}"""
        ),
        (
            new Assessment { Name = "Northstar data maturity baseline", OrganizationName = "Northstar Group", AssessmentType = AssessmentType.DataMaturity },
            "data-platform.json",
            """{"dataOwnersDefined":true,"catalogAvailable":false,"qualityMonitoring":false,"pipelineMonitoring":false,"warehouseAvailable":true,"selfServiceAnalytics":true,"consistentMetrics":false,"structuredDatasets":false,"featureManagement":false,"vectorSearch":false}"""
        ),
        (
            new Assessment { Name = "Northstar service consistency baseline", OrganizationName = "Northstar Group", AssessmentType = AssessmentType.ServiceConsistency },
            "service-manifests.json",
            """{"services":[{"name":"Customer API","version":"v1","errorFormat":"error","authentication":"JWT","schemas":{"Customer":{"CustomerId":{"type":"string"}}}},{"name":"Order API","version":"v2","errorFormat":"code-message","authentication":"OAuth","schemas":{"Order":{"customerID":{"type":"string"}}}},{"name":"Billing API","version":"v2025","errorFormat":"code-message","authentication":"JWT","schemas":{"Invoice":{"cust_id":{"type":"string"}}}},{"name":"Reporting API","version":"v1","errorFormat":"error","authentication":"None","schemas":{"Report":{"customerId":{"type":"string"}}}}]}"""
        )
        };
        foreach (var sample in samples)
        {
            var assessment = sample.Item1;
            assessment.Status = AssessmentStatus.Completed;
            assessment.Artifacts.Add(new AssessmentArtifact { AssessmentId = assessment.Id, OriginalFileName = sample.Item2, StoragePath = "seed/" + sample.Item2, ArtifactType = assessment.AssessmentType switch { AssessmentType.Modernization => ArtifactType.TechnologyInventory, AssessmentType.DataMaturity => ArtifactType.DataPlatformInventory, _ => ArtifactType.ServiceManifest }, MetadataJson = sample.Item3 });
            assessment.Result = engine.Execute(assessment);
            assessment.Result.AssessmentId = assessment.Id;
            db.Assessments.Add(assessment);
        }
        await db.SaveChangesAsync();
    }
}
