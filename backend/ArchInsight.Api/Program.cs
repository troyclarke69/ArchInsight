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
using ArchLens.Governance;
using ArchLens.Execution;
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
builder.Services.AddSingleton<ArchitectureGovernanceEngine>();
builder.Services.AddSingleton<TransformationForecastEngine>();
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
async Task<(ArchitectureSnapshot Snapshot, IReadOnlyList<DriftAlert> Alerts, IReadOnlyList<PolicyEvaluation> Evaluations)> SaveGovernanceSnapshotAsync(AssessmentDbContext db, ArchitectureGovernanceEngine engine, Guid repositoryId, RepositoryInventory inventory, CancellationToken ct)
{
    var previous = (await db.ArchitectureSnapshots.Where(s => s.RepositoryAnalysisId == repositoryId).ToListAsync(ct)).OrderByDescending(s => s.CreatedDate).FirstOrDefault();
    var snapshot = engine.CreateSnapshot(repositoryId, inventory);
    var alerts = previous is null ? [] : engine.Compare(previous, snapshot).ToArray();
    var policies = await db.ArchitecturePolicies.ToListAsync(ct);
    var evaluations = engine.Evaluate(snapshot, inventory, policies).ToArray();
    db.ArchitectureSnapshots.Add(snapshot);
    db.DriftAlerts.AddRange(alerts);
    db.PolicyEvaluations.AddRange(evaluations);
    await db.SaveChangesAsync(ct);
    return (snapshot, alerts, evaluations);
}
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
app.MapGet("/api/governance/overview", async (AssessmentDbContext db, CancellationToken ct) =>
{
    var repositories = await db.RepositoryAnalyses.Select(r => new { r.Id, r.Name }).ToListAsync(ct);
    var snapshots = await db.ArchitectureSnapshots.ToListAsync(ct);
    var latest = snapshots.GroupBy(s => s.RepositoryAnalysisId).Select(g => g.OrderByDescending(s => s.CreatedDate).First()).ToArray();
    var latestIds = latest.Select(s => s.Id).ToArray();
    var evaluations = latestIds.Length == 0 ? [] : await db.PolicyEvaluations.Where(e => latestIds.Contains(e.ArchitectureSnapshotId)).ToListAsync(ct);
    var alerts = await db.DriftAlerts.Where(a => a.Status == "Open").ToListAsync(ct);
    var policies = await db.ArchitecturePolicies.CountAsync(p => p.Enabled, ct);
    var failed = evaluations.Count(e => e.Status == GovernanceStatus.Fail);
    var warning = evaluations.Count(e => e.Status == GovernanceStatus.Warning);
    return Results.Ok(new
    {
        repositories = repositories.Count, monitoredRepositories = latest.Length,
        architectureHealth = latest.Length == 0 ? 0 : (int)Math.Round(latest.Average(s => s.MaturityIndex)),
        technologyRisk = latest.Length == 0 ? 0 : (int)Math.Round(latest.Average(s => s.RiskScore)),
        modernizationProgress = latest.Length == 0 ? 0 : (int)Math.Round(latest.Average(s => s.ModernizationScore)),
        policyCompliance = evaluations.Count == 0 ? 100 : (int)Math.Round(100d * evaluations.Count(e => e.Status == GovernanceStatus.Pass) / evaluations.Count),
        policyCount = policies, failedPolicies = failed, warningPolicies = warning, openAlerts = alerts.Count,
        criticalAlerts = alerts.Count(a => a.Severity == "Critical"),
        biggestRisks = alerts.OrderByDescending(a => a.Severity == "Critical" ? 4 : a.Severity == "High" ? 3 : a.Severity == "Medium" ? 2 : 1).Take(6).Select(a => new { a.Id, a.RepositoryAnalysisId, repositoryName = repositories.FirstOrDefault(r => r.Id == a.RepositoryAnalysisId)?.Name, a.Category, a.Severity, a.Title, a.Description, a.AffectedAsset, a.CreatedDate }),
        trends = snapshots.OrderBy(s => s.CreatedDate).TakeLast(60).Select(s => new { s.RepositoryAnalysisId, repositoryName = repositories.FirstOrDefault(r => r.Id == s.RepositoryAnalysisId)?.Name, s.CreatedDate, s.RiskScore, s.ModernizationScore, s.TechnicalDebt, s.CloudReadiness, s.SecurityScore, s.ConsistencyScore, s.MaturityIndex }),
        latestSnapshots = latest.Select(s => new { s.RepositoryAnalysisId, repositoryName = repositories.FirstOrDefault(r => r.Id == s.RepositoryAnalysisId)?.Name, s.CreatedDate, s.Hash, s.ApplicationCount, s.ServiceCount, s.DependencyCount, s.RiskScore, s.MaturityIndex })
    });
});
app.MapGet("/api/governance/repositories/{id:guid}", async (Guid id, AssessmentDbContext db, CancellationToken ct) =>
{
    var repository = await db.RepositoryAnalyses.SingleOrDefaultAsync(r => r.Id == id, ct);
    if (repository is null) return Results.NotFound();
    var snapshots = await db.ArchitectureSnapshots.Where(s => s.RepositoryAnalysisId == id).ToListAsync(ct);
    var alerts = await db.DriftAlerts.Where(a => a.RepositoryAnalysisId == id).ToListAsync(ct);
    var evaluations = await db.PolicyEvaluations.Where(e => snapshots.Select(s => s.Id).Contains(e.ArchitectureSnapshotId)).ToListAsync(ct);
    var policies = await db.ArchitecturePolicies.ToDictionaryAsync(p => p.Id, ct);
    var decisions = await db.ArchitectureDecisions.Where(d => d.RepositoryAnalysisId == id).ToListAsync(ct);
    var pullRequests = await db.PullRequestReviews.Where(p => p.RepositoryAnalysisId == id).ToListAsync(ct);
    return Results.Ok(new
    {
        repository = new { repository.Id, repository.Name },
        snapshots = snapshots.OrderByDescending(s => s.CreatedDate),
        alerts = alerts.OrderByDescending(a => a.CreatedDate),
        evaluations = evaluations.Select(e => new { e.Id, e.ArchitectureSnapshotId, e.ArchitecturePolicyId, policyName = policies.GetValueOrDefault(e.ArchitecturePolicyId)?.Name, e.Asset, status = e.Status.ToString(), e.Evidence, e.EvaluatedAt }),
        decisions = decisions.OrderByDescending(d => d.CreatedDate),
        pullRequests = pullRequests.OrderByDescending(p => p.ReviewedAt)
    });
});
app.MapPost("/api/repositories/{id:guid}/monitor", async (Guid id, [FromForm] IFormFile file, RepositoryZipAnalyzer analyzer, AssessmentDbContext db, ArchitectureGovernanceEngine governance, CancellationToken ct) =>
{
    if (file.Length is <= 0 or > RepositoryZipAnalyzer.MaximumArchiveBytes) return Results.BadRequest(new { error = "Monitoring scans require a ZIP file between 1 byte and 25 MB." });
    if (!Path.GetExtension(file.FileName).Equals(".zip", StringComparison.OrdinalIgnoreCase)) return Results.BadRequest(new { error = "Upload a .zip repository snapshot." });
    var repository = await db.RepositoryAnalyses.SingleOrDefaultAsync(r => r.Id == id, ct);
    if (repository is null) return Results.NotFound(new { error = "Repository not found." });
    try
    {
        await using var input = file.OpenReadStream();
        var inventory = await analyzer.AnalyzeAsync(input, file.FileName, ct);
        var result = await SaveGovernanceSnapshotAsync(db, governance, id, inventory, ct);
        return Results.Created($"/api/governance/repositories/{id}", new { repositoryId = id, repository.Name, snapshot = new { result.Snapshot.Id, result.Snapshot.CreatedDate, result.Snapshot.Hash, result.Snapshot.RiskScore, result.Snapshot.ModernizationScore, result.Snapshot.MaturityIndex }, newAlerts = result.Alerts, policyResults = result.Evaluations.Count, passed = result.Evaluations.Count(e => e.Status == GovernanceStatus.Pass), warnings = result.Evaluations.Count(e => e.Status == GovernanceStatus.Warning), failed = result.Evaluations.Count(e => e.Status == GovernanceStatus.Fail) });
    }
    catch (InvalidDataException ex) { return Results.BadRequest(new { error = ex.Message }); }
}).DisableAntiforgery();
app.MapGet("/api/governance/policies", async (AssessmentDbContext db, CancellationToken ct) => Results.Ok(await db.ArchitecturePolicies.OrderBy(p => p.Category).ThenBy(p => p.Name).ToListAsync(ct)));
app.MapPut("/api/governance/policies/{id:guid}", async (Guid id, UpdatePolicyRequest request, AssessmentDbContext db, CancellationToken ct) =>
{
    var policy = await db.ArchitecturePolicies.SingleOrDefaultAsync(p => p.Id == id, ct);
    if (policy is null) return Results.NotFound();
    policy.Enabled = request.Enabled;
    await db.SaveChangesAsync(ct);
    return Results.Ok(policy);
});
app.MapGet("/api/governance/standards", async (AssessmentDbContext db, CancellationToken ct) => Results.Ok(await db.TechnologyStandards.OrderBy(s => s.Lifecycle).ThenBy(s => s.Technology).ToListAsync(ct)));
app.MapPost("/api/governance/alerts/{id:guid}/status", async (Guid id, UpdateAlertStatusRequest request, AssessmentDbContext db, CancellationToken ct) =>
{
    if (request.Status is not ("Open" or "Acknowledged" or "Resolved")) return Results.BadRequest(new { error = "Status must be Open, Acknowledged, or Resolved." });
    var alert = await db.DriftAlerts.SingleOrDefaultAsync(a => a.Id == id, ct);
    if (alert is null) return Results.NotFound();
    alert.Status = request.Status;
    await db.SaveChangesAsync(ct);
    return Results.Ok(new { alert.Id, alert.Status });
});
app.MapGet("/api/governance/decisions", async (Guid repositoryId, AssessmentDbContext db, CancellationToken ct) => Results.Ok(await db.ArchitectureDecisions.Where(d => d.RepositoryAnalysisId == repositoryId).ToListAsync(ct)));
app.MapPost("/api/governance/decisions", async (CreateDecisionRequest request, AssessmentDbContext db, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Decision)) return Results.BadRequest(new { error = "Decision title and decision text are required." });
    if (request.Title.Length > 300 || request.Context.Length > 10000 || request.Decision.Length > 10000 || request.Consequences.Length > 10000) return Results.BadRequest(new { error = "Decision text exceeds the allowed length." });
    if (!await db.RepositoryAnalyses.AnyAsync(r => r.Id == request.RepositoryAnalysisId, ct)) return Results.NotFound(new { error = "Repository not found." });
    var decision = new ArchitectureDecision { RepositoryAnalysisId = request.RepositoryAnalysisId, Title = request.Title.Trim(), Context = request.Context.Trim(), Decision = request.Decision.Trim(), Consequences = request.Consequences.Trim(), Status = request.Status is "Accepted" or "Superseded" or "Proposed" ? request.Status : "Proposed" };
    db.ArchitectureDecisions.Add(decision);
    await db.SaveChangesAsync(ct);
    return Results.Created($"/api/governance/decisions/{decision.Id}", decision);
});
app.MapPost("/api/repositories/{id:guid}/pull-requests/review", async (Guid id, ReviewPullRequestRequest request, AssessmentDbContext db, ArchitectureGovernanceEngine governance, CancellationToken ct) =>
{
    if (request.PullRequestNumber < 1 || string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Diff) || request.Diff.Length > 300_000) return Results.BadRequest(new { error = "Provide a PR number, title, and diff no larger than 300 KB." });
    if (!await db.RepositoryAnalyses.AnyAsync(r => r.Id == id, ct)) return Results.NotFound(new { error = "Repository not found." });
    var result = governance.AnalyzePullRequest(request.Diff, request.Title.Trim());
    var head = (request.HeadRevision ?? "unknown").Trim();
    var review = new PullRequestReview { RepositoryAnalysisId = id, PullRequestNumber = request.PullRequestNumber, Title = request.Title.Trim()[..Math.Min(request.Title.Trim().Length, 300)], HeadRevision = head[..Math.Min(head.Length, 120)], DiffHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.Diff))).ToLowerInvariant(), RiskScore = result.RiskScore, RiskLevel = result.RiskLevel, Summary = result.Summary, FindingsJson = JsonSerializer.Serialize(result.Findings) };
    db.PullRequestReviews.Add(review);
    await db.SaveChangesAsync(ct);
    return Results.Created($"/api/repositories/{id}/pull-requests", new { review.Id, review.PullRequestNumber, review.Title, review.HeadRevision, review.RiskScore, review.RiskLevel, review.Summary, findings = result.Findings, review.ReviewedAt });
});
app.MapGet("/api/repositories/{id:guid}/pull-requests", async (Guid id, AssessmentDbContext db, CancellationToken ct) => Results.Ok(await db.PullRequestReviews.Where(r => r.RepositoryAnalysisId == id).ToListAsync(ct)));
app.MapPost("/api/governance/advice", async (GovernanceAdviceRequest request, AssessmentDbContext db, IAIInsightGenerator generator, TransformationSimulator simulator, CancellationToken ct) =>
{
    if (!generator.IsConfigured) return Results.Problem("Configure an AI provider to request governance advice.", statusCode: StatusCodes.Status503ServiceUnavailable);
    var selected = await db.RepositoryAnalyses.Include(r => r.Applications).Include(r => r.Dependencies).SingleOrDefaultAsync(r => r.Id == request.RepositoryAnalysisId, ct);
    if (selected is null) return Results.NotFound(new { error = "Repository not found." });
    var assessmentCandidates = await db.Assessments.Include(a => a.Result)!.ThenInclude(r => r!.Findings).Include(a => a.Result)!.ThenInclude(r => r!.ScoreCards).Include(a => a.Insights).Where(a => a.RepositoryAnalysisId == selected.Id && a.Status == AssessmentStatus.Completed).ToListAsync(ct);
    var assessment = assessmentCandidates.OrderByDescending(a => a.CreatedDate).FirstOrDefault();
    if (assessment is null) return Results.Conflict(new { error = "Governance advice requires a completed assessment linked to this repository." });
    var latest = (await db.ArchitectureSnapshots.Where(s => s.RepositoryAnalysisId == selected.Id).ToListAsync(ct)).OrderByDescending(s => s.CreatedDate).FirstOrDefault();
    var openAlerts = await db.DriftAlerts.Where(a => a.RepositoryAnalysisId == selected.Id && a.Status == "Open").ToListAsync(ct);
    var adviceContext = JsonSerializer.Serialize(new { latestSnapshot = latest is null ? null : new { latest.RiskScore, latest.ModernizationScore, latest.TechnicalDebt, latest.CloudReadiness, latest.SecurityScore, latest.ConsistencyScore, latest.MaturityIndex }, alerts = openAlerts.Select(a => new { a.Category, a.Severity, a.Title, a.Description }), portfolio = simulator.Prioritize(selected).Take(10), question = request.Question });
    var insight = assessment.Insights.FirstOrDefault(i => i.Status == AIInsightStatus.Completed);
    try
    {
        var response = await generator.AnswerQuestionAsync(assessment, insight, $"Answer as an architecture governance advisor using current snapshot, drift alerts, and portfolio facts. Distinguish evidence from inference. Context: {adviceContext}", ct);
        return Results.Ok(new { answer = response.Answer, response.ModelName, response.PromptVersion, response.TokenUsage });
    }
    catch (Exception ex) when (ex is not OperationCanceledException) { return Results.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway); }
});
app.MapGet("/api/transformation/recommendations", async (AssessmentDbContext db, CancellationToken ct) =>
{
    var recommendations = await db.Recommendations.Include(r => r.AssessmentResult).ThenInclude(r => r!.Assessment).OrderBy(r => r.Priority).ToListAsync(ct);
    return Results.Ok(recommendations.Select(r => new { r.Id, r.Title, r.Description, r.Timeframe, r.Priority, assessmentId = r.AssessmentResult?.AssessmentId, assessmentName = r.AssessmentResult?.Assessment?.Name, repositoryAnalysisId = r.AssessmentResult?.Assessment?.RepositoryAnalysisId }));
});
app.MapGet("/api/transformation/portfolio", async (AssessmentDbContext db, TransformationForecastEngine forecastEngine, CancellationToken ct) =>
{
    var initiatives = await db.TransformationInitiatives.ToListAsync(ct);
    var scenarios = await db.Scenarios.Include(s => s.Result).ToListAsync(ct);
    var snapshots = await db.ArchitectureSnapshots.ToListAsync(ct);
    var metrics = await db.EngineeringMetrics.ToListAsync(ct);
    var forecast = forecastEngine.Forecast(snapshots, initiatives);
    var benchmark = forecastEngine.Benchmark(snapshots);
    var completed = initiatives.Where(i => i.Status == InitiativeStatus.Completed).ToArray();
    var total = initiatives.Count;
    var top = snapshots.GroupBy(s => s.RepositoryAnalysisId).Select(g => g.OrderByDescending(s => s.CreatedDate).First()).ToArray();
    var latestMetrics = metrics.GroupBy(m => m.RepositoryAnalysisId).Select(g => g.OrderByDescending(m => m.RecordedAt).First()).ToDictionary(m => m.RepositoryAnalysisId);
    var matched = top.Where(s => latestMetrics.ContainsKey(s.RepositoryAnalysisId)).Select(s => (Snapshot: s, Metric: latestMetrics[s.RepositoryAnalysisId])).ToArray();
    static double Correlation(IEnumerable<(double X, double Y)> source)
    {
        var points = source.ToArray(); if (points.Length < 2) return 0;
        var meanX = points.Average(x => x.X); var meanY = points.Average(x => x.Y);
        var numerator = points.Sum(x => (x.X - meanX) * (x.Y - meanY));
        var denom = Math.Sqrt(points.Sum(x => Math.Pow(x.X - meanX, 2)) * points.Sum(x => Math.Pow(x.Y - meanY, 2)));
        return denom == 0 ? 0 : Math.Round(numerator / denom, 2);
    }
    return Results.Ok(new { initiatives = initiatives.OrderByDescending(i => i.Priority).ThenBy(i => i.TargetCompletionDate), scenarios = scenarios.Select(s => new { s.Id, s.AssessmentId, s.Name, status = s.Status.ToString(), riskReduction = s.Result?.RiskReduction, debtReduction = s.Result?.TechnicalDebtReduction }), metrics = metrics.OrderByDescending(m => m.RecordedAt).Take(40), forecast, benchmark, kpis = new { totalInitiatives = total, completionPercent = total == 0 ? 0 : (int)Math.Round(100d * completed.Length / total), completed = completed.Length, blocked = initiatives.Count(i => i.Status == InitiativeStatus.Blocked), active = initiatives.Count(i => i.Status == InitiativeStatus.InProgress), riskReduced = completed.Sum(i => i.ActualRiskReduction), debtReduced = completed.Sum(i => i.ActualDebtReduction), investmentPoints = initiatives.Sum(i => i.InvestmentPoints), maturityChange = top.Length == 0 ? 0 : top.Average(x => x.MaturityIndex) }, deliveryCorrelation = new { sampleSize = matched.Length, leadTimeVsRisk = Correlation(matched.Select(x => (x.Metric.LeadTimeDays, (double)x.Snapshot.RiskScore))), changeFailureVsConsistency = Correlation(matched.Select(x => (x.Metric.ChangeFailureRatePercent, (double)(100 - x.Snapshot.ConsistencyScore)))), deploymentFrequencyVsMaturity = Correlation(matched.Select(x => (x.Metric.DeploymentFrequencyPerMonth, (double)x.Snapshot.MaturityIndex))), message = matched.Length < 2 ? "Record metrics for multiple repositories to estimate directional delivery and architecture correlations." : "Pearson correlations are descriptive and do not establish causality." }, quarterlyReport = new { generatedAt = DateTimeOffset.UtcNow, currentState = new { repositories = top.Length, architectureHealth = top.Length == 0 ? 0 : top.Average(s => s.MaturityIndex), technologyRisk = top.Length == 0 ? 0 : top.Average(s => s.RiskScore) }, investment = initiatives.Count, completedImprovements = completed.Select(i => new { i.Name, i.ActualRiskReduction, i.ActualDebtReduction }), roadmap = initiatives.Where(i => i.Status is InitiativeStatus.Planned or InitiativeStatus.Approved or InitiativeStatus.InProgress or InitiativeStatus.Blocked).OrderByDescending(i => i.Priority).Take(8).Select(i => new { i.Name, status = i.Status.ToString(), i.TargetCompletionDate }) } });
});
app.MapPost("/api/transformation/initiatives", async (CreateInitiativeRequest request, AssessmentDbContext db, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 200) return Results.BadRequest(new { error = "Initiative name must contain 1–200 characters." });
    if (request.Priority is < 1 or > 5) return Results.BadRequest(new { error = "Priority must be between 1 (highest) and 5." });
    Recommendation? recommendation = null;
    if (request.RecommendationId is not null) { recommendation = await db.Recommendations.Include(r => r.AssessmentResult).SingleOrDefaultAsync(r => r.Id == request.RecommendationId, ct); if (recommendation is null) return Results.NotFound(new { error = "Recommendation not found." }); }
    Scenario? scenario = null;
    if (request.ScenarioId is not null) { scenario = await db.Scenarios.Include(s => s.Result).Include(s => s.Assessment).SingleOrDefaultAsync(s => s.Id == request.ScenarioId, ct); if (scenario is null) return Results.NotFound(new { error = "Scenario not found." }); }
    if (recommendation?.AssessmentResult is not null && scenario is not null && recommendation.AssessmentResult.AssessmentId != scenario.AssessmentId) return Results.BadRequest(new { error = "The selected recommendation and scenario must belong to the same assessment." });
    var name = recommendation?.Title ?? request.Name.Trim();
    var item = new TransformationInitiative { Name = name[..Math.Min(name.Length, 200)], Description = (recommendation?.Description ?? request.Description ?? "").Trim(), Priority = request.Priority, Owner = string.IsNullOrWhiteSpace(request.Owner) ? "Unassigned" : request.Owner.Trim()[..Math.Min(request.Owner.Trim().Length, 160)], TargetCompletionDate = request.TargetCompletionDate, AssessmentId = recommendation?.AssessmentResult?.AssessmentId ?? scenario?.AssessmentId ?? request.AssessmentId, RepositoryAnalysisId = request.RepositoryAnalysisId ?? scenario?.Assessment?.RepositoryAnalysisId, RecommendationId = recommendation?.Id, ScenarioId = scenario?.Id, ExpectedRiskReduction = Math.Clamp(request.ExpectedRiskReduction ?? scenario?.Result?.RiskReduction ?? 0, 0, 100), ExpectedDebtReduction = Math.Clamp(request.ExpectedDebtReduction ?? scenario?.Result?.TechnicalDebtReduction ?? 0, 0, 100), InvestmentPoints = Math.Clamp(request.InvestmentPoints ?? scenario?.Result?.ImplementationEffort ?? 1, 1, 100) };
    db.TransformationInitiatives.Add(item); await db.SaveChangesAsync(ct); return Results.Created($"/api/transformation/initiatives/{item.Id}", item);
});
app.MapPut("/api/transformation/initiatives/{id:guid}", async (Guid id, UpdateInitiativeRequest request, AssessmentDbContext db, CancellationToken ct) =>
{
    var item = await db.TransformationInitiatives.SingleOrDefaultAsync(i => i.Id == id, ct); if (item is null) return Results.NotFound();
    if (!Enum.TryParse<InitiativeStatus>(request.Status, true, out var status)) return Results.BadRequest(new { error = "Status must be Planned, Approved, InProgress, Blocked, Completed, or Cancelled." });
    item.Status = status; if (request.ActualRiskReduction is not null) item.ActualRiskReduction = Math.Clamp(request.ActualRiskReduction.Value, 0, 100); if (request.ActualDebtReduction is not null) item.ActualDebtReduction = Math.Clamp(request.ActualDebtReduction.Value, 0, 100);
    await db.SaveChangesAsync(ct); return Results.Ok(item);
});
app.MapPost("/api/transformation/repositories/{id:guid}/engineering-metrics", async (Guid id, CreateEngineeringMetricsRequest request, AssessmentDbContext db, CancellationToken ct) =>
{
    if (!await db.RepositoryAnalyses.AnyAsync(r => r.Id == id, ct)) return Results.NotFound(new { error = "Repository not found." });
    if (request.DeploymentFrequencyPerMonth < 0 || request.LeadTimeDays < 0 || request.ChangeFailureRatePercent is < 0 or > 100 || request.MttrHours < 0) return Results.BadRequest(new { error = "Metrics must be non-negative; change failure rate must be 0–100%." });
    var metric = new EngineeringMetrics { RepositoryAnalysisId = id, DeploymentFrequencyPerMonth = request.DeploymentFrequencyPerMonth, LeadTimeDays = request.LeadTimeDays, ChangeFailureRatePercent = request.ChangeFailureRatePercent, MttrHours = request.MttrHours, Source = string.IsNullOrWhiteSpace(request.Source) ? "Manual entry" : request.Source.Trim()[..Math.Min(request.Source.Trim().Length, 160)] };
    db.EngineeringMetrics.Add(metric); await db.SaveChangesAsync(ct); return Results.Created($"/api/transformation/repositories/{id}/engineering-metrics", metric);
});
app.MapGet("/api/transformation/knowledge-graph/{id:guid}", async (Guid id, AssessmentDbContext db, CancellationToken ct) =>
{
    var repository = await db.RepositoryAnalyses.Include(r => r.Applications).Include(r => r.Services).Include(r => r.Dependencies).Include(r => r.ApplicationDependencies).SingleOrDefaultAsync(r => r.Id == id, ct);
    if (repository is null) return Results.NotFound();
    var nodes = new List<KnowledgeNode>(); var edges = new List<(string Source, string Target, string Relationship)>(); var ids = new Dictionary<string, KnowledgeNode>(StringComparer.OrdinalIgnoreCase);
    var storedNodes = await db.KnowledgeNodes.Where(n => n.RepositoryAnalysisId == id).ToListAsync(ct);
    void Node(string key, string type, string label) { if (!ids.TryGetValue(key, out var node)) { node = storedNodes.FirstOrDefault(n => n.NodeType == type && n.ExternalId == key) ?? new KnowledgeNode { RepositoryAnalysisId = id, NodeType = type[..Math.Min(type.Length, 40)], ExternalId = key[..Math.Min(key.Length, 200)] }; if (!storedNodes.Contains(node)) { storedNodes.Add(node); nodes.Add(node); } ids[key] = node; } node.Label = label[..Math.Min(label.Length, 300)]; }
    void Edge(string source, string target, string relation) { if (ids.ContainsKey(source) && ids.ContainsKey(target)) edges.Add((source, target, relation)); }
    Node("repo", "Repository", repository.Name);
    foreach (var appItem in repository.Applications) { var key = "app:" + appItem.Name; Node(key, "Application", appItem.Name); Edge("repo", key, "CONTAINS"); foreach (var framework in JsonSerializer.Deserialize<string[]>(appItem.FrameworksJson) ?? []) { var tech = "tech:" + framework; Node(tech, "Technology", framework); Edge(key, tech, "USES"); } }
    foreach (var service in repository.Services) { var key = "service:" + service.Name; Node(key, "Service", service.Name); Edge("repo", key, "CONTAINS"); }
    foreach (var dependency in repository.Dependencies.Take(200)) { var key = "technology:" + dependency.Name + "@" + dependency.Version; Node(key, "Dependency", dependency.Name + " " + dependency.Version); foreach (var appItem in repository.Applications.Take(50)) Edge("app:" + appItem.Name, key, "USES"); }
    foreach (var relationship in repository.ApplicationDependencies) { Node("app:" + relationship.Source, "Application", relationship.Source); Node("app:" + relationship.Target, "Application", relationship.Target); Edge("app:" + relationship.Source, "app:" + relationship.Target, "DEPENDS_ON"); }
    var linkedAssessments = await db.Assessments.Where(a => a.RepositoryAnalysisId == id).Include(a => a.Result).ThenInclude(r => r!.Findings).Include(a => a.Result).ThenInclude(r => r!.Recommendations).ToListAsync(ct);
    foreach (var assessment in linkedAssessments)
    {
        foreach (var finding in assessment.Result?.Findings ?? []) { var key = "finding:" + finding.Id; Node(key, "Finding", finding.Title); Edge("repo", key, "HAS_FINDING"); foreach (var appItem in repository.Applications.Where(a => (finding.Title + " " + finding.Description).Contains(a.Name, StringComparison.OrdinalIgnoreCase))) Edge(key, "app:" + appItem.Name, "AFFECTS"); }
        foreach (var recommendation in assessment.Result?.Recommendations ?? []) { var key = "recommendation:" + recommendation.Id; Node(key, "Recommendation", recommendation.Title); Edge("repo", key, "HAS_RECOMMENDATION"); foreach (var appItem in repository.Applications.Where(a => (recommendation.Title + " " + recommendation.Description).Contains(a.Name, StringComparison.OrdinalIgnoreCase))) Edge(key, "app:" + appItem.Name, "TARGETS"); }
    }
    var initiatives = await db.TransformationInitiatives.Where(i => i.RepositoryAnalysisId == id).ToListAsync(ct);
    foreach (var initiative in initiatives) { var key = "initiative:" + initiative.Id; Node(key, "Initiative", initiative.Name); if (initiative.RecommendationId is not null) Edge(key, "recommendation:" + initiative.RecommendationId, "IMPLEMENTS"); foreach (var appItem in repository.Applications.Where(a => (initiative.Name + " " + initiative.Description).Contains(a.Name, StringComparison.OrdinalIgnoreCase))) Edge(key, "app:" + appItem.Name, "ADDRESSES"); }
    var currentIds = ids.Values.Select(n => n.Id).ToHashSet();
    var staleNodes = storedNodes.Where(n => !currentIds.Contains(n.Id)).ToArray();
    if (staleNodes.Length > 0) { var staleIds = staleNodes.Select(n => n.Id).ToArray(); var staleEdges = await db.KnowledgeRelationships.Where(r => staleIds.Contains(r.FromNodeId) || staleIds.Contains(r.ToNodeId)).ToListAsync(ct); db.KnowledgeRelationships.RemoveRange(staleEdges); db.KnowledgeNodes.RemoveRange(staleNodes); }
    await db.SaveChangesAsync(ct);
    var persistedIds = ids.Values.Select(n => n.Id).ToArray();
    var existingEdges = await db.KnowledgeRelationships.Where(r => persistedIds.Contains(r.FromNodeId) && persistedIds.Contains(r.ToNodeId)).ToListAsync(ct);
    var edgeKeys = existingEdges.Select(r => $"{r.FromNodeId}:{r.ToNodeId}:{r.RelationshipType}").ToHashSet(StringComparer.OrdinalIgnoreCase);
    foreach (var edge in edges) { var from = ids[edge.Source].Id; var to = ids[edge.Target].Id; if (edgeKeys.Add($"{from}:{to}:{edge.Relationship}")) db.KnowledgeRelationships.Add(new KnowledgeRelationship { FromNodeId = from, ToNodeId = to, RelationshipType = edge.Relationship }); }
    await db.SaveChangesAsync(ct);
    return Results.Ok(new { repositoryAnalysisId = id, nodes = ids.Values.Select(n => new { id = n.Id, type = n.NodeType, label = n.Label }), relationships = existingEdges.Concat(await db.KnowledgeRelationships.Where(r => persistedIds.Contains(r.FromNodeId) && persistedIds.Contains(r.ToNodeId)).ToListAsync(ct)).DistinctBy(r => r.Id).Select(r => new { source = r.FromNodeId, target = r.ToNodeId, relationship = r.RelationshipType }), limits = new { dependencies = 200, applications = 50 } });
});
app.MapPost("/api/transformation/boardroom", async (BoardroomRequest request, AssessmentDbContext db, IAIInsightGenerator generator, TransformationForecastEngine forecastEngine, CancellationToken ct) =>
{
    if (!generator.IsConfigured) return Results.Problem("Configure Azure OpenAI or OpenAI credentials to generate an AI boardroom briefing.", statusCode: StatusCodes.Status503ServiceUnavailable);
    var snapshots = await db.ArchitectureSnapshots.ToListAsync(ct); var initiatives = await db.TransformationInitiatives.ToListAsync(ct); var assessments = await db.Assessments.Include(a => a.Result).OrderByDescending(a => a.CreatedDate).Take(20).ToListAsync(ct);
    if (assessments.Count == 0) return Results.Conflict(new { error = "Create an assessment before requesting a briefing." });
    var audience = request.Audience is "CTO" or "CIO" or "Architecture Review Board" or "Engineering Leadership" ? request.Audience : "CTO";
    var context = JsonSerializer.Serialize(new { audience, forecast = forecastEngine.Forecast(snapshots, initiatives), initiatives = initiatives.Select(i => new { i.Name, status = i.Status.ToString(), i.Priority, i.ExpectedRiskReduction, i.ExpectedDebtReduction, i.ActualRiskReduction, i.ActualDebtReduction }), assessments = assessments.Select(a => new { a.Name, a.Status, score = a.Result?.OverallScore, recommendation = a.Result?.Recommendation }), snapshots = snapshots.OrderByDescending(s => s.CreatedDate).Take(12).Select(s => new { s.RiskScore, s.MaturityIndex, s.TechnicalDebt, s.CreatedDate }), instruction = audience == "Strategy Advisor" && !string.IsNullOrWhiteSpace(request.Question) ? request.Question : "Produce a concise quarterly architecture review: current state, major risks, trend analysis, investment/value, completed improvements, roadmap, and forecast. Distinguish evidence from heuristic projection." });
    var assessment = assessments.First(); try { var answer = await generator.AnswerQuestionAsync(assessment, null, context, ct); return Results.Ok(new { audience, answer = answer.Answer, generatedAt = DateTimeOffset.UtcNow, answer.ModelName, answer.PromptVersion }); } catch (Exception ex) when (ex is not OperationCanceledException) { return Results.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway); }
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
    IAssessmentQueue assessmentQueue, AssessmentDbContext db, ArchitectureGovernanceEngine governance, CancellationToken ct) =>
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
        var monitoring = await SaveGovernanceSnapshotAsync(db, governance, repository.Id, inventory, ct);
        foreach (var assessment in repository.Assessments) await assessmentQueue.EnqueueAsync(assessment.Id, ct);
        return Results.Created($"/api/repositories/{repository.Id}", new { repositoryId = repository.Id, repository.Name, repository.ContentHash, applications = repository.Applications.Count, services = repository.Services.Count, dependencies = repository.Dependencies.Count, workflowCount = inventory.BuildWorkflows.Count + inventory.DeploymentWorkflows.Count, assessments = repository.Assessments.Select(a => new { a.Id, a.AssessmentType, a.Status }), warnings = inventory.Warnings, monitoringSnapshotId = monitoring.Snapshot.Id, policies = monitoring.Evaluations.Count, detailUrl = $"/api/repositories/{repository.Id}", sbomUrl = $"/api/repositories/{repository.Id}/sbom" });
    }
    catch (InvalidDataException ex) { return Results.BadRequest(new { error = ex.Message }); }
    catch (OperationCanceledException) when (ct.IsCancellationRequested) { return Results.StatusCode(StatusCodes.Status499ClientClosedRequest); }
}).DisableAntiforgery();
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
    repository.AddArtifact(artifact);
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
    repository.AddInsight(insight);
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
    repository.AddQuestion(record);
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
    await Phase6SchemaUpgrade.ApplyAsync(db);
    await Phase7SchemaUpgrade.ApplyAsync(db);
    var governance = scope.ServiceProvider.GetRequiredService<ArchitectureGovernanceEngine>();
    if (!await db.ArchitecturePolicies.AnyAsync())
    {
        db.ArchitecturePolicies.AddRange(governance.DefaultPolicies());
        await db.SaveChangesAsync();
    }
    if (!await db.TechnologyStandards.AnyAsync())
    {
        db.TechnologyStandards.AddRange(governance.DefaultStandards());
        await db.SaveChangesAsync();
    }
    var untrackedRepositories = await db.RepositoryAnalyses.Select(r => new { r.Id, r.SummaryJson }).ToListAsync();
    foreach (var stored in untrackedRepositories)
    {
        if (await db.ArchitectureSnapshots.AnyAsync(s => s.RepositoryAnalysisId == stored.Id)) continue;
        var inventory = JsonSerializer.Deserialize<RepositoryInventory>(stored.SummaryJson, new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true });
        if (inventory is not null) await SaveGovernanceSnapshotAsync(db, governance, stored.Id, inventory, CancellationToken.None);
    }
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
            var insight = new AssessmentInsight { AssessmentId = item.Id };
            item.Insights.Add(insight);
            db.AssessmentInsights.Add(insight);
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
                repository.AddResult(assessment.Result);
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
                var insight = new AssessmentInsight { AssessmentId = assessment.Id };
                assessment.Insights.Add(insight);
                repository.AddInsight(insight);
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
public sealed record UpdatePolicyRequest(bool Enabled);
public sealed record UpdateAlertStatusRequest(string Status);
public sealed record CreateDecisionRequest(Guid RepositoryAnalysisId, string Title, string Context, string Decision, string Consequences, string Status);
public sealed record ReviewPullRequestRequest(int PullRequestNumber, string Title, string? HeadRevision, string Diff);
public sealed record GovernanceAdviceRequest(Guid RepositoryAnalysisId, string Question);
public sealed record CreateInitiativeRequest(string Name, string? Description, int Priority = 3, string? Owner = null, DateTimeOffset? TargetCompletionDate = null, Guid? AssessmentId = null, Guid? ScenarioId = null, Guid? RecommendationId = null, Guid? RepositoryAnalysisId = null, int? ExpectedRiskReduction = null, int? ExpectedDebtReduction = null, int? InvestmentPoints = null);
public sealed record UpdateInitiativeRequest(string Status, int? ActualRiskReduction = null, int? ActualDebtReduction = null);
public sealed record CreateEngineeringMetricsRequest(double DeploymentFrequencyPerMonth, double LeadTimeDays, double ChangeFailureRatePercent, double MttrHours, string? Source = null);
public sealed record BoardroomRequest(string Audience, string? Question = null);

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
