using System.Text.Json;
using ArchInsight.AI.Contracts;
using ArchInsight.AI.Services;
using ArchInsight.Agents.Contracts;
using ArchInsight.Analysis;
using ArchInsight.Application;
using ArchInsight.Domain;

namespace ArchInsight.Api;

public sealed class AIInsightQueue : IAIInsightQueue
{
    private readonly System.Threading.Channels.Channel<Guid> _channel = System.Threading.Channels.Channel.CreateUnbounded<Guid>();
    public ValueTask EnqueueAsync(Guid assessmentId, CancellationToken ct) => _channel.Writer.WriteAsync(assessmentId, ct);
    public ValueTask<Guid> DequeueAsync(CancellationToken ct) => _channel.Reader.ReadAsync(ct);
}

public sealed class AIInsightWorker(IServiceScopeFactory scopes, IAIInsightQueue queue, ILogger<AIInsightWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            Guid assessmentId;
            try { assessmentId = await queue.DequeueAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            await using var scope = scopes.CreateAsyncScope();
            var repository = scope.ServiceProvider.GetRequiredService<IAssessmentRepository>();
            var db = scope.ServiceProvider.GetRequiredService<ArchInsight.Infrastructure.AssessmentDbContext>();
            var generator = scope.ServiceProvider.GetRequiredService<IAIInsightGenerator>();
            var agents = scope.ServiceProvider.GetRequiredService<IEnumerable<IAgent>>();
            var assessment = await repository.FindAsync(assessmentId, stoppingToken);
            var insight = assessment?.Insights.Where(i => i.Status is AIInsightStatus.Pending or AIInsightStatus.Running).OrderByDescending(i => i.CreatedDate).FirstOrDefault();
            if (assessment is null || insight is null) continue;
            if (!generator.IsConfigured)
            {
                insight.Status = AIInsightStatus.NotConfigured;
                insight.ErrorMessage = "Configure Azure OpenAI or OpenAI credentials to generate insights.";
                UpdateAgentStages(assessment!, insight, null, null);
                await repository.SaveChangesAsync(stoppingToken);
                continue;
            }
            AgentResult? aiInsightAgentResult = null;
            try
            {
                insight.Status = AIInsightStatus.Running;
                insight.ErrorMessage = null;
                await repository.SaveChangesAsync(stoppingToken);
                var (agentResult, generated) = await RunAIInsightAgentAsync(assessment, agents, stoppingToken);
                aiInsightAgentResult = agentResult;
                insight.ExecutiveSummary = generated.ExecutiveSummary;
                insight.CurrentState = generated.CurrentState;
                insight.KeyRisksJson = JsonSerializer.Serialize(generated.KeyRisks);
                insight.BusinessImpact = generated.BusinessImpact;
                insight.OperationalRisk = generated.OperationalRisk;
                insight.TechnicalDebtImpact = generated.TechnicalDebtImpact;
                insight.CloudReadiness = generated.CloudReadiness;
                insight.DeliveryVelocityImpact = generated.DeliveryVelocityImpact;
                insight.DataAndReportingImpact = generated.DataAndReportingImpact;
                insight.RiskNarrative = generated.RiskNarrative;
                insight.RecommendedStrategy = assessment.Result?.Recommendation;
                insight.RecommendedNextStepsJson = JsonSerializer.Serialize(generated.RecommendedNextSteps);
                insight.ExpectedOutcome = generated.ExpectedOutcome;
                insight.Confidence = EvidenceConfidenceClassifier.Classify(assessment);
                insight.GeneratedAt = DateTimeOffset.UtcNow;
                insight.ModelName = generated.ModelName;
                insight.PromptVersion = generated.PromptVersion;
                insight.TokenUsage = generated.TokenUsage;
                insight.Recommendations = generated.Recommendations.Select(r => new AIRecommendation { InsightId = insight.Id, Priority = r.Priority, Title = r.Title, Description = r.Description, BusinessBenefit = r.BusinessBenefit, EstimatedComplexity = r.EstimatedComplexity, ExpectedOutcome = r.ExpectedOutcome }).ToList();
                insight.Roadmap = generated.Roadmap.Select(r => new AIInsightRoadmapItem { InsightId = insight.Id, Sequence = r.Sequence, Period = r.Period, ActivityType = ParseActivityType(r.ActivityType), Title = r.Title, Description = r.Description, ExpectedOutcome = r.ExpectedOutcome }).ToList();
                db.AIRecommendations.AddRange(insight.Recommendations);
                db.AIInsightRoadmapItems.AddRange(insight.Roadmap);
                insight.Status = AIInsightStatus.Completed;
            }
            catch (Exception ex)
            {
                insight.Status = AIInsightStatus.Failed;
                insight.ErrorMessage = ex.Message;
                logger.LogWarning(ex, "AI insight generation failed for assessment {AssessmentId}", assessmentId);
            }
            AgentResult? executiveAdvisorResult = null;
            string? executiveAdvisorError = null;
            try { executiveAdvisorResult = await RunExecutiveAdvisorAsync(assessment, insight, agents, stoppingToken); }
            catch (Exception ex) { executiveAdvisorError = ex.Message; logger.LogWarning(ex, "Executive advisor stage failed for repository analysis {RepositoryAnalysisId}", assessment.RepositoryAnalysisId); }
            UpdateAgentStages(assessment, insight, aiInsightAgentResult, executiveAdvisorResult, executiveAdvisorError);
            await repository.SaveChangesAsync(stoppingToken);
        }
    }

    private async Task<(AgentResult Result, GeneratedInsight Insight)> RunAIInsightAgentAsync(Assessment assessment, IEnumerable<IAgent> agents, CancellationToken ct)
    {
        var aiAgent = agents.FirstOrDefault(a => a.Name == "AIInsightAgent") ?? throw new InvalidOperationException("The AI insight agent is not registered.");
        var emptyInventory = new RepositoryInventory(assessment.Name, "", [], [], [], [], [], [], 0, []);
        var inventory = assessment.RepositoryAnalysis is null ? emptyInventory : JsonSerializer.Deserialize<RepositoryInventory>(assessment.RepositoryAnalysis.SummaryJson, new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true }) ?? emptyInventory;
        var result = await aiAgent.ExecuteAsync(new AgentContext(inventory, assessment), ct);
        var generated = JsonSerializer.Deserialize<GeneratedInsight>(result.OutputJson, new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true }) ?? throw new InvalidOperationException("The AI insight agent returned an empty result.");
        return (result, generated);
    }

    private async Task<AgentResult?> RunExecutiveAdvisorAsync(Assessment assessment, AssessmentInsight insight, IEnumerable<IAgent> agents, CancellationToken ct)
    {
        if (insight.Status != AIInsightStatus.Completed || assessment.RepositoryAnalysis is null) return null;
        var inventory = JsonSerializer.Deserialize<RepositoryInventory>(assessment.RepositoryAnalysis.SummaryJson, new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true });
        var advisor = agents.FirstOrDefault(a => a.Name == "ExecutiveAdvisorAgent");
        if (inventory is null || advisor is null) return null;
        return await advisor.ExecuteAsync(new AgentContext(inventory, assessment, insight), ct);
    }

    private static void UpdateAgentStages(Assessment assessment, AssessmentInsight insight, AgentResult? aiInsightAgentResult, AgentResult? executiveAdvisorResult, string? executiveAdvisorError = null)
    {
        var repository = assessment.RepositoryAnalysis;
        if (repository is null) return;
        var ai = repository.AgentStages.FirstOrDefault(s => s.AgentName == "AIInsightAgent");
        var executive = repository.AgentStages.FirstOrDefault(s => s.AgentName == "ExecutiveAdvisorAgent");
        if (ai is not null)
        {
            ai.Status = insight.Status.ToString(); ai.CompletedAt = DateTimeOffset.UtcNow;
            ai.OutputJson = aiInsightAgentResult?.OutputJson ?? JsonSerializer.Serialize(new { insight.Status, insight.ModelName, insight.PromptVersion, insight.TokenUsage, insight.ErrorMessage, insight.ExecutiveSummary, insight.BusinessImpact, insight.RiskNarrative });
        }
        if (executive is not null)
        {
            executive.Status = executiveAdvisorError is not null ? "Failed" : insight.Status is AIInsightStatus.Completed ? "Completed" : insight.Status.ToString();
            executive.CompletedAt = DateTimeOffset.UtcNow;
            executive.OutputJson = executiveAdvisorResult?.OutputJson ?? JsonSerializer.Serialize(new { status = executive.Status, reason = executiveAdvisorError ?? insight.ErrorMessage });
        }
    }

    private static RoadmapActivityType ParseActivityType(string value)
    {
        var normalized = value.Replace(" ", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal);
        return Enum.TryParse<RoadmapActivityType>(normalized, true, out var result) ? result : RoadmapActivityType.TransformationActivity;
    }
}
