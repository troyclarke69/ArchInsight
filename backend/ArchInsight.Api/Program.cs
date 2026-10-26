using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ArchInsight.Agents.Contracts;
using ArchInsight.Agents.Services;
using ArchInsight.Analysis;
using ArchInsight.AI.Contracts;
using ArchInsight.AI.Services;
using ArchInsight.Api;
using ArchInsight.Application;
using ArchInsight.Domain;
using ArchInsight.Infrastructure;
using ArchInsight.Simulation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 27 * 1024 * 1024);
builder.Services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = 27 * 1024 * 1024);
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy.WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:3000"]).AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddMediatR(config => config.RegisterServicesFromAssembly(typeof(CreateAssessmentCommand).Assembly));
builder.Services.AddScoped<FluentValidation.IValidator<CreateAssessmentCommand>, CreateAssessmentValidator>();
builder.Services.AddScoped<IAssessmentRepository, EfAssessmentRepository>();
builder.Services.AddSingleton<IAssessmentQueue, AssessmentQueue>();
builder.Services.AddSingleton<AssessmentEngine>();
builder.Services.AddSingleton<TransformationSimulator>();
builder.Services.AddSingleton<IArtifactStorage>(_ => new JsonArtifactStorage(builder.Environment.ContentRootPath));
builder.Services.AddSingleton(new AIProviderOptions(
    builder.Configuration["AI:AzureOpenAI:ChatCompletionsUrl"],
    builder.Configuration["AI:AzureOpenAI:ApiKey"],
    builder.Configuration["AI:AzureOpenAI:Deployment"],
    builder.Configuration["AI:OpenAI:ApiKey"],
    builder.Configuration["AI:OpenAI:Model"]));
builder.Services.AddHttpClient<IAIInsightGenerator, OpenAiInsightGenerator>(client => client.Timeout = TimeSpan.FromSeconds(90));
builder.Services.AddSingleton<IAIInsightQueue, AIInsightQueue>();
builder.Services.AddSingleton<RepositoryZipAnalyzer>();
builder.Services.AddSingleton<IAgent, ArtifactDiscoveryAgent>();
builder.Services.AddSingleton<IAgent, ModernizationAgent>();
builder.Services.AddSingleton<IAgent, ServiceGovernanceAgent>();
builder.Services.AddSingleton<IAgent, TechnologyRiskAgent>();
builder.Services.AddTransient<IAgent, AIInsightAgent>();
builder.Services.AddSingleton<IAgent, ExecutiveAdvisorAgent>();
builder.Services.AddScoped<IAgentOrchestrator, AgentOrchestrator>();
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
object RepositoryView(RepositoryAnalysis repository) => new
{
    repository.Id, repository.Name, repository.ContentHash, repository.CreatedDate,
    applications = repository.Applications.Select(a => new { a.Id, a.Name, a.RepositoryPath, a.Ecosystem, a.Runtime, frameworks = JsonSerializer.Deserialize<string[]>(a.FrameworksJson) ?? [], a.Database, a.Hosting, a.Containerized, a.BusinessImpact, a.TechnicalRisk }),
    services = repository.Services.Select(s => new { s.Id, s.Name, s.RepositoryPath, s.Version, s.EndpointCount, endpoints = JsonSerializer.Deserialize<string[]>(s.EndpointsJson) ?? [], schemas = JsonSerializer.Deserialize<string[]>(s.SchemasJson) ?? [], authenticationSchemes = JsonSerializer.Deserialize<string[]>(s.AuthenticationJson) ?? [] }),
    dependencies = repository.Dependencies.Select(d => new { d.Id, d.Name, d.Version, d.Type, d.Ecosystem, d.RepositoryPath, d.RiskLevel, d.RiskReason }),
    inventory = repository.SoftwareInventory.Select(i => new { i.Category, i.Name, i.Version, i.SourcePath }),
    stages = repository.AgentStages.OrderBy(s => s.Sequence).Select(s => new { s.Sequence, s.AgentName, s.Status, output = JsonSerializer.Deserialize<JsonElement>(s.OutputJson), s.StartedAt, s.CompletedAt }),
    assessments = repository.Assessments.Select(a => new { a.Id, a.AssessmentType, a.Status, score = a.Result == null ? (int?)null : a.Result.OverallScore }),
    applicationDependencies = repository.ApplicationDependencies.Select(d => new { d.Id, d.Source, d.Target, d.DependencyType }),
    sbom = JsonSerializer.Deserialize<JsonElement>(repository.SbomJson)
};
object ScenarioView(Scenario scenario) => new
{
    scenario.Id, scenario.AssessmentId, scenario.Name, scenario.Description, scenario.CreatedDate, scenario.CreatedBy,
    status = scenario.Status.ToString(),
    changes = scenario.Changes.Select(c => new { c.Id, changeType = c.ChangeType.ToString(), c.Target, c.OldValue, c.NewValue }),
    result = scenario.Result is null ? null : new
    {
        scenario.Result.Id, scenario.Result.ScenarioId, scenario.Result.BaselineScore, scenario.Result.ProjectedScore,
        scenario.Result.RiskReduction, scenario.Result.CloudReadinessChange, scenario.Result.TechnicalDebtReduction,
        scenario.Result.ImplementationEffort, scenario.Result.BusinessImpact, scenario.Result.PriorityScore,
        scenario.Result.EffortLevel, scenario.Result.RiskLevel, scenario.Result.Summary, scenario.Result.Recommendation,
        scenario.Result.SimulatedAt
    },
    scenario.AdvisorExplanation, scenario.AdvisorModelName, scenario.AdvisorPromptVersion, scenario.AdvisorTokenUsage
};
async Task<Assessment> NewDiscoveredAssessmentAsync(AssessmentType type, RepositoryAnalysis repository, string? organizationName, GeneratedArtifact generatedArtifact, IArtifactStorage storage, CancellationToken ct)
{
    var suffix = type == AssessmentType.Modernization ? "modernization" : "service consistency";
    var assessmentName = $"{repository.Name} discovery – {suffix}";
    var assessment = new Assessment
    {
        Name = assessmentName[..Math.Min(assessmentName.Length, 120)],
        OrganizationName = string.IsNullOrWhiteSpace(organizationName) ? "Repository discovery" : organizationName.Trim()[..Math.Min(organizationName.Trim().Length, 120)],
        AssessmentType = type,
        RepositoryAnalysisId = repository.Id,
        RepositoryAnalysis = repository
    };
    await using var generatedStream = new MemoryStream(Encoding.UTF8.GetBytes(generatedArtifact.MetadataJson));
    var generatedPath = await storage.StoreAsync(assessment.Id, generatedArtifact.FileName, generatedStream, ct);
    assessment.Artifacts.Add(new AssessmentArtifact
    {
        AssessmentId = assessment.Id,
        ArtifactType = Enum.Parse<ArtifactType>(generatedArtifact.Type),
        OriginalFileName = generatedArtifact.FileName,
        StoragePath = generatedPath,
        MetadataJson = generatedArtifact.MetadataJson
    });
    return assessment;
}
app.MapGet("/api/inventory", async (AssessmentDbContext db, CancellationToken ct) =>
{
    var repositories = await db.RepositoryAnalyses.Include(r => r.Applications).Include(r => r.Services).Include(r => r.Dependencies).Include(r => r.SoftwareInventory).Include(r => r.AgentStages).Include(r => r.Assessments).ThenInclude(a => a.Result).AsSplitQuery().ToListAsync(ct);
    return Results.Ok(repositories.OrderByDescending(r => r.CreatedDate).Select(RepositoryView));
});
app.MapGet("/api/repositories/{id:guid}", async (Guid id, AssessmentDbContext db, CancellationToken ct) =>
{
    var repository = await db.RepositoryAnalyses.Include(r => r.Applications).Include(r => r.Services).Include(r => r.Dependencies).Include(r => r.SoftwareInventory).Include(r => r.AgentStages).Include(r => r.Assessments).ThenInclude(a => a.Result).AsSplitQuery().SingleOrDefaultAsync(r => r.Id == id, ct);
    return repository is null ? Results.NotFound() : Results.Ok(RepositoryView(repository));
});
app.MapGet("/api/repositories/{id:guid}/sbom", async (Guid id, AssessmentDbContext db, CancellationToken ct) =>
{
    var sbom = await db.RepositoryAnalyses.Where(r => r.Id == id).Select(r => r.SbomJson).SingleOrDefaultAsync(ct);
    return sbom is null ? Results.NotFound() : Results.File(Encoding.UTF8.GetBytes(sbom), "application/vnd.cyclonedx+json", $"archinsight-{id:N}-sbom.json");
});
app.MapGet("/api/assessments/{id:guid}/scenarios", async (Guid id, AssessmentDbContext db, CancellationToken ct) =>
{
    if (!await db.Assessments.AnyAsync(a => a.Id == id, ct)) return Results.NotFound();
    var scenarios = await db.Scenarios.Include(s => s.Changes).Include(s => s.Result).Where(s => s.AssessmentId == id).ToListAsync(ct);
    return Results.Ok(scenarios.OrderByDescending(s => s.CreatedDate).Select(ScenarioView));
});
app.MapPost("/api/assessments/{id:guid}/scenarios", async (Guid id, CreateScenarioRequest request, AssessmentDbContext db, CancellationToken ct) =>
{
    var assessment = await db.Assessments.Include(a => a.Result).SingleOrDefaultAsync(a => a.Id == id, ct);
    if (assessment is null) return Results.NotFound(new { error = "Assessment not found." });
    if (assessment.Status != AssessmentStatus.Completed || assessment.Result is null) return Results.Conflict(new { error = "Scenarios require a completed assessment baseline." });
    if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 160) return Results.BadRequest(new { error = "Scenario name must contain 1–160 characters." });
    var description = (request.Description ?? "").Trim();
    var scenario = new Scenario { AssessmentId = id, Name = request.Name.Trim(), Description = description[..Math.Min(description.Length, 2000)] };
    db.Scenarios.Add(scenario);
    await db.SaveChangesAsync(ct);
    return Results.Created($"/api/scenarios/{scenario.Id}", ScenarioView(scenario));
});
app.MapPost("/api/scenarios/{id:guid}/changes", async (Guid id, AddScenarioChangeRequest request, AssessmentDbContext db, CancellationToken ct) =>
{
    var scenario = await db.Scenarios.Include(s => s.Changes).SingleOrDefaultAsync(s => s.Id == id, ct);
    if (scenario is null) return Results.NotFound(new { error = "Scenario not found." });
    if (scenario.Status != ScenarioStatus.Draft) return Results.Conflict(new { error = "Changes can only be added to draft scenarios." });
    if (string.IsNullOrWhiteSpace(request.Target) || request.Target.Length > 300) return Results.BadRequest(new { error = "Target must contain 1–300 characters." });
    var oldValue = (request.OldValue ?? "").Trim(); var newValue = (request.NewValue ?? "").Trim();
    var change = new ScenarioChange { ScenarioId = scenario.Id, ChangeType = request.ChangeType, Target = request.Target.Trim(), OldValue = oldValue[..Math.Min(oldValue.Length, 300)], NewValue = newValue[..Math.Min(newValue.Length, 300)] };
    scenario.Changes.Add(change);
    db.ScenarioChanges.Add(change);
    await db.SaveChangesAsync(ct);
    return Results.Ok(ScenarioView(scenario));
});
app.MapPost("/api/scenarios/{id:guid}/simulate", async (Guid id, AssessmentDbContext db, TransformationSimulator simulator, CancellationToken ct) =>
{
    var scenario = await db.Scenarios.Include(s => s.Changes).Include(s => s.Result).Include(s => s.Assessment).ThenInclude(a => a!.Result)!.ThenInclude(r => r!.ScoreCards).Include(s => s.Assessment).ThenInclude(a => a!.Result)!.ThenInclude(r => r!.Findings).Include(s => s.Assessment).ThenInclude(a => a!.RepositoryAnalysis)!.ThenInclude(r => r!.Applications).Include(s => s.Assessment).ThenInclude(a => a!.RepositoryAnalysis)!.ThenInclude(r => r!.Services).SingleOrDefaultAsync(s => s.Id == id, ct);
    if (scenario is null) return Results.NotFound(new { error = "Scenario not found." });
    if (scenario.Status == ScenarioStatus.Completed && scenario.Result is not null) return Results.Ok(ScenarioView(scenario));
    try
    {
        scenario.Status = ScenarioStatus.Running;
        var result = simulator.Simulate(scenario.Assessment!, scenario);
        scenario.Result = result;
        db.ScenarioResults.Add(result);
        scenario.Status = ScenarioStatus.Completed;
        await db.SaveChangesAsync(ct);
        return Results.Ok(ScenarioView(scenario));
    }
    catch (InvalidOperationException ex) { scenario.Status = ScenarioStatus.Failed; await db.SaveChangesAsync(ct); return Results.Conflict(new { error = ex.Message }); }
});
app.MapGet("/api/scenarios/compare", async ([FromQuery] Guid[] scenarioId, AssessmentDbContext db, CancellationToken ct) =>
{
    if (scenarioId.Length is < 1 or > 2) return Results.BadRequest(new { error = "Pass one or two scenarioId query parameters." });
    var scenarios = await db.Scenarios.Include(s => s.Changes).Include(s => s.Result).Include(s => s.Assessment).ThenInclude(a => a!.Result).Where(s => scenarioId.Contains(s.Id)).ToListAsync(ct);
    if (scenarios.Count != scenarioId.Distinct().Count()) return Results.NotFound(new { error = "One or more scenarios were not found." });
    return Results.Ok(scenarios.Select(s => new { baseline = new { assessmentId = s.AssessmentId, assessmentName = s.Assessment?.Name, score = s.Result?.BaselineScore ?? s.Assessment?.Result?.OverallScore }, scenario = ScenarioView(s) }));
});
app.MapGet("/api/repositories/{id:guid}/portfolio", async (Guid id, AssessmentDbContext db, TransformationSimulator simulator, CancellationToken ct) =>
{
    var repository = await db.RepositoryAnalyses.Include(r => r.Applications).Include(r => r.Dependencies).SingleOrDefaultAsync(r => r.Id == id, ct);
    return repository is null ? Results.NotFound() : Results.Ok(simulator.Prioritize(repository));
});
app.MapGet("/api/repositories/{id:guid}/dependencies", async (Guid id, AssessmentDbContext db, CancellationToken ct) =>
{
    if (!await db.RepositoryAnalyses.AnyAsync(r => r.Id == id, ct)) return Results.NotFound();
    return Results.Ok(await db.ApplicationDependencies.Where(d => d.RepositoryAnalysisId == id).Select(d => new { d.Id, d.Source, d.Target, d.DependencyType }).ToListAsync(ct));
});
app.MapPost("/api/repositories/{id:guid}/dependencies", async (Guid id, DependencyRequest request, AssessmentDbContext db, CancellationToken ct) =>
{
    var repository = await db.RepositoryAnalyses.Include(r => r.Applications).Include(r => r.Services).SingleOrDefaultAsync(r => r.Id == id, ct);
    if (repository is null) return Results.NotFound(new { error = "Repository not found." });
    if (string.IsNullOrWhiteSpace(request.Source) || string.IsNullOrWhiteSpace(request.Target) || request.Source.Length > 300 || request.Target.Length > 300) return Results.BadRequest(new { error = "Source and target must contain 1–300 characters." });
    var known = repository.Applications.Select(a => a.Name).Concat(repository.Services.Select(s => s.Name)).ToHashSet(StringComparer.OrdinalIgnoreCase);
    if (!known.Contains(request.Source) || !known.Contains(request.Target) || request.Source.Equals(request.Target, StringComparison.OrdinalIgnoreCase)) return Results.BadRequest(new { error = "Choose two different systems from this repository inventory." });
    var kind = string.IsNullOrWhiteSpace(request.DependencyType) ? "API" : request.DependencyType.Trim();
    var edge = new ApplicationDependency { RepositoryAnalysisId = id, Source = request.Source.Trim(), Target = request.Target.Trim(), DependencyType = kind[..Math.Min(kind.Length, 80)] };
    db.ApplicationDependencies.Add(edge);
    try { await db.SaveChangesAsync(ct); } catch (DbUpdateException) { return Results.Conflict(new { error = "That dependency is already recorded." }); }
    return Results.Created($"/api/repositories/{id}/dependencies", new { edge.Id, edge.Source, edge.Target, edge.DependencyType });
});
app.MapGet("/api/repositories/{id:guid}/impact/{selected}", async (Guid id, string selected, AssessmentDbContext db, TransformationSimulator simulator, CancellationToken ct) =>
{
    var repository = await db.RepositoryAnalyses.Include(r => r.Applications).Include(r => r.Services).Include(r => r.Dependencies).Include(r => r.ApplicationDependencies).AsSplitQuery().SingleOrDefaultAsync(r => r.Id == id, ct);
    if (repository is null) return Results.NotFound();
    try { return Results.Ok(simulator.AnalyzeImpact(repository, selected)); }
    catch (KeyNotFoundException ex) { return Results.NotFound(new { error = ex.Message }); }
});
app.MapGet("/api/repositories/{id:guid}/roadmap", async (Guid id, AssessmentDbContext db, TransformationSimulator simulator, CancellationToken ct) =>
{
    var repository = await db.RepositoryAnalyses.Include(r => r.Applications).Include(r => r.Dependencies).SingleOrDefaultAsync(r => r.Id == id, ct);
    if (repository is null) return Results.NotFound();
    return Results.Ok(simulator.Prioritize(repository).Select((x, i) => new { sequence = i + 1, period = x.PriorityScore >= 75 ? "0–3 Months" : x.EffortLevel == "Low" ? "3–6 Months" : x.PriorityScore >= 45 ? "6–12 Months" : "12–24 Months", x.ApplicationName, x.PriorityScore, x.EffortLevel, x.Recommendation }));
});
app.MapPost("/api/scenarios/{id:guid}/advisor", async (Guid id, AssessmentDbContext db, IAIInsightGenerator generator, CancellationToken ct) =>
{
    if (!generator.IsConfigured) return Results.Problem("Configure Azure OpenAI or OpenAI credentials before requesting scenario advice.", statusCode: StatusCodes.Status503ServiceUnavailable);
    var scenario = await db.Scenarios.Include(s => s.Changes).Include(s => s.Result).Include(s => s.Assessment).ThenInclude(a => a!.Result).Include(s => s.Assessment).ThenInclude(a => a!.Insights).SingleOrDefaultAsync(s => s.Id == id, ct);
    if (scenario?.Result is null || scenario.Assessment?.Result is null) return Results.Conflict(new { error = "Run a scenario before requesting advice." });
    var context = JsonSerializer.Serialize(new { current = new { scenario.Result.BaselineScore, scorecards = scenario.Assessment.Result.ScoreCards.Select(x => new { x.Dimension, x.Score }) }, scenario.Name, changes = scenario.Changes.Select(x => new { type = x.ChangeType.ToString(), x.Target, x.OldValue, x.NewValue }), outcome = scenario.Result, request = "Explain benefits, risks, and recommended sequencing. State that projections are heuristic." });
    var insight = scenario.Assessment.Insights.OrderByDescending(x => x.CreatedDate).FirstOrDefault(x => x.Status == AIInsightStatus.Completed);
    try
    {
        var answer = await generator.AnswerQuestionAsync(scenario.Assessment, insight, $"Explain this simulated scenario to executives. Include benefits, risks, and sequencing: {context}", ct);
        scenario.AdvisorExplanation = answer.Answer; scenario.AdvisorModelName = answer.ModelName; scenario.AdvisorPromptVersion = answer.PromptVersion; scenario.AdvisorTokenUsage = answer.TokenUsage;
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { explanation = answer.Answer, answer.ModelName, answer.PromptVersion, answer.TokenUsage });
    }
    catch (Exception ex) when (ex is not OperationCanceledException) { return Results.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway); }
});
app.MapPost("/api/repositories/analyze", async (
    [FromForm] IFormFile file, [FromForm] string? name, [FromForm] string? organizationName,
    RepositoryZipAnalyzer analyzer, IAgentOrchestrator orchestrator, IArtifactStorage storage,
    IAssessmentQueue assessmentQueue, AssessmentDbContext db, CancellationToken ct) =>
{
    if (file.Length is <= 0 or > RepositoryZipAnalyzer.MaximumArchiveBytes) return Results.BadRequest(new { error = "Repository ZIP files must be between 1 byte and 25 MB." });
    if (!Path.GetExtension(file.FileName).Equals(".zip", StringComparison.OrdinalIgnoreCase)) return Results.BadRequest(new { error = "Upload a repository archive with a .zip extension." });
    try
    {
        RepositoryInventory inventory;
        string archivePath;
        var repositoryId = Guid.NewGuid();
        await using (var analysisInput = file.OpenReadStream()) inventory = await analyzer.AnalyzeAsync(analysisInput, file.FileName, ct);
        await using (var archiveInput = file.OpenReadStream()) archivePath = await storage.StoreAsync(repositoryId, Path.GetFileName(file.FileName), archiveInput, ct);
        var agentResults = await orchestrator.ExecuteAsync(new AgentContext(inventory), ct);
        var riskFindings = agentResults.SelectMany(a => a.Findings).ToArray();
        var repositoryDisplayName = string.IsNullOrWhiteSpace(name) ? inventory.Name : name.Trim();
        repositoryDisplayName = repositoryDisplayName[..Math.Min(repositoryDisplayName.Length, 300)];
        var repository = new RepositoryAnalysis
        {
            Id = repositoryId,
            Name = repositoryDisplayName,
            ArchivePath = archivePath,
            ContentHash = inventory.ContentHash,
            SummaryJson = JsonSerializer.Serialize(inventory),
            SbomJson = inventory.ToSbomJson(),
            Applications = inventory.Applications.Select(a => new DiscoveredApplication
            {
                RepositoryAnalysisId = repositoryId, Name = a.Name, RepositoryPath = a.Path, Ecosystem = a.Ecosystem, Runtime = a.Runtime,
                FrameworksJson = JsonSerializer.Serialize(a.Frameworks), Database = a.Database, Hosting = a.Hosting, Containerized = a.Containerized,
                BusinessImpact = Regex.IsMatch(a.Name, "billing|payment|customer|order|identity|auth", RegexOptions.IgnoreCase) ? 5 : 3,
                TechnicalRisk = Math.Min(100, riskFindings.Where(f => f.Title.Contains(a.Name, StringComparison.OrdinalIgnoreCase) || f.Category.Contains("Framework", StringComparison.OrdinalIgnoreCase) || f.Category.Contains("Database", StringComparison.OrdinalIgnoreCase)).Sum(f => f.ScorePenalty) + (a.Containerized ? 0 : 12) + (inventory.BuildWorkflows.Count == 0 ? 8 : 0))
            }).ToList(),
            Services = inventory.Services.Select(s => new DiscoveredService { RepositoryAnalysisId = repositoryId, Name = s.Name, RepositoryPath = s.Path, Version = s.Version, EndpointCount = s.EndpointCount, EndpointsJson = JsonSerializer.Serialize(s.Endpoints), SchemasJson = JsonSerializer.Serialize(s.Schemas), AuthenticationJson = JsonSerializer.Serialize(s.AuthenticationSchemes) }).ToList(),
            Dependencies = inventory.Dependencies.Select(d =>
            {
                var risk = riskFindings.FirstOrDefault(f => f.Title.Contains(d.Name, StringComparison.OrdinalIgnoreCase));
                return new TechnologyDependency { RepositoryAnalysisId = repositoryId, Name = d.Name, Version = d.Version, Type = d.Type, Ecosystem = d.Ecosystem, RepositoryPath = d.Path, RiskLevel = risk?.Severity ?? "Unknown", RiskReason = risk?.Description ?? "No configured discovery rule identified a specific risk." };
            }).ToList(),
            SoftwareInventory = inventory.Inventory.Select(i => new SoftwareInventory { RepositoryAnalysisId = repositoryId, Category = i.Category, Name = i.Name, Version = i.Version, SourcePath = i.Path }).ToList(),
            AgentStages = agentResults.Select((result, index) => new RepositoryAgentStage { RepositoryAnalysisId = repositoryId, Sequence = index + 1, AgentName = result.AgentName, Status = "Completed", OutputJson = result.OutputJson, CompletedAt = DateTimeOffset.UtcNow }).ToList()
        };
        var generated = agentResults.SelectMany(r => r.Artifacts).ToArray();
        var modernization = generated.FirstOrDefault(a => a.Type == nameof(ArtifactType.TechnologyInventory));
        if (modernization is not null && repository.Applications.Count > 0)
        {
            var assessment = await NewDiscoveredAssessmentAsync(AssessmentType.Modernization, repository, organizationName, modernization, storage, ct);
            repository.Assessments.Add(assessment);
        }
        var serviceManifest = generated.FirstOrDefault(a => a.Type == nameof(ArtifactType.ServiceManifest));
        if (serviceManifest is not null && repository.Services.Count >= 2)
        {
            var assessment = await NewDiscoveredAssessmentAsync(AssessmentType.ServiceConsistency, repository, organizationName, serviceManifest, storage, ct);
            repository.Assessments.Add(assessment);
        }
        if (repository.Services.Count == 1) inventory = inventory with { Warnings = inventory.Warnings.Append("A service consistency assessment requires at least two OpenAPI documents; the discovered API was retained in inventory.").ToArray() };
        repository.SummaryJson = JsonSerializer.Serialize(inventory);
        repository.SbomJson = inventory.ToSbomJson();
        repository.AgentStages.Add(new RepositoryAgentStage { RepositoryAnalysisId = repositoryId, Sequence = 5, AgentName = "AIInsightAgent", Status = repository.Assessments.Count > 0 ? "Pending" : "Skipped", OutputJson = "{}" });
        repository.AgentStages.Add(new RepositoryAgentStage { RepositoryAnalysisId = repositoryId, Sequence = 6, AgentName = "ExecutiveAdvisorAgent", Status = repository.Assessments.Count > 0 ? "WaitingForInsight" : "Skipped", OutputJson = "{}" });
        db.RepositoryAnalyses.Add(repository);
        await db.SaveChangesAsync(ct);
        foreach (var assessment in repository.Assessments) await assessmentQueue.EnqueueAsync(assessment.Id, ct);
        return Results.Created($"/api/repositories/{repository.Id}", new { repositoryId = repository.Id, repository.Name, repository.ContentHash, applications = repository.Applications.Count, services = repository.Services.Count, dependencies = repository.Dependencies.Count, workflowCount = inventory.BuildWorkflows.Count + inventory.DeploymentWorkflows.Count, assessments = repository.Assessments.Select(a => new { a.Id, a.AssessmentType, a.Status }), warnings = inventory.Warnings, detailUrl = $"/api/repositories/{repository.Id}", sbomUrl = $"/api/repositories/{repository.Id}/sbom" });
    }
    catch (InvalidDataException ex) { return Results.BadRequest(new { error = ex.Message }); }
    catch (OperationCanceledException) when (ct.IsCancellationRequested) { return Results.StatusCode(StatusCodes.Status499ClientClosedRequest); }
});
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
    await Phase4SchemaUpgrade.ApplyAsync(db);
    await Phase5SchemaUpgrade.ApplyAsync(db);
    await SeedData.InitializeAsync(scope.ServiceProvider);
    var repo = scope.ServiceProvider.GetRequiredService<IAssessmentRepository>();
    var queue = scope.ServiceProvider.GetRequiredService<IAssessmentQueue>();
    var insightQueue = scope.ServiceProvider.GetRequiredService<IAIInsightQueue>();
    var insightGenerator = scope.ServiceProvider.GetRequiredService<IAIInsightGenerator>();
    foreach (var item in await repo.ListAsync(CancellationToken.None))
    {
        if (item.Status is AssessmentStatus.Pending or AssessmentStatus.Running) await queue.EnqueueAsync(item.Id, CancellationToken.None);
        if (item.Status == AssessmentStatus.Completed && item.Result is not null && item.Versions.Count == 0)
        {
            var version = RepositoryAssessmentEnricher.CreateVersion(item, 1);
            item.Versions.Add(version);
            repo.AddVersion(version);
            await repo.SaveChangesAsync(CancellationToken.None);
        }
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
                RepositoryAssessmentEnricher.ApplyDiscoveryFindings(assessment);
                assessment.Status = AssessmentStatus.Completed;
                assessment.ErrorMessage = null;
            }
            catch (Exception ex)
            {
                assessment.Status = AssessmentStatus.Failed;
                assessment.ErrorMessage = ex.Message;
                logger.LogWarning(ex, "Assessment {AssessmentId} failed", id);
            }
            if (assessment.Status == AssessmentStatus.Failed && assessment.RepositoryAnalysis is not null)
            {
                foreach (var stage in assessment.RepositoryAnalysis.AgentStages.Where(s => s.AgentName is "AIInsightAgent" or "ExecutiveAdvisorAgent"))
                {
                    stage.Status = "Skipped";
                    stage.CompletedAt = DateTimeOffset.UtcNow;
                    stage.OutputJson = JsonSerializer.Serialize(new { reason = "Deterministic assessment did not complete.", assessment.ErrorMessage });
                }
            }
            if (assessment.Status == AssessmentStatus.Completed && assessment.Result is not null)
            {
                var version = RepositoryAssessmentEnricher.CreateVersion(assessment, assessment.Versions.Count == 0 ? 1 : assessment.Versions.Max(v => v.Version) + 1);
                assessment.Versions.Add(version);
                repository.AddVersion(version);
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
public sealed record CreateScenarioRequest(string Name, string? Description);
public sealed record AddScenarioChangeRequest(ScenarioChangeType ChangeType, string Target, string? OldValue, string? NewValue);
public sealed record DependencyRequest(string Source, string Target, string? DependencyType);

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
