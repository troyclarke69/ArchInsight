using System.Text.Json;
using ArchInsight.AI.Contracts;
using ArchInsight.AI.Services;
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
            var generator = scope.ServiceProvider.GetRequiredService<IAIInsightGenerator>();
            var assessment = await repository.FindAsync(assessmentId, stoppingToken);
            var insight = assessment?.Insights.Where(i => i.Status is AIInsightStatus.Pending or AIInsightStatus.Running).OrderByDescending(i => i.CreatedDate).FirstOrDefault();
            if (assessment is null || insight is null) continue;
            if (!generator.IsConfigured)
            {
                insight.Status = AIInsightStatus.NotConfigured;
                insight.ErrorMessage = "Configure Azure OpenAI or OpenAI credentials to generate insights.";
                await repository.SaveChangesAsync(stoppingToken);
                continue;
            }
            try
            {
                insight.Status = AIInsightStatus.Running;
                insight.ErrorMessage = null;
                await repository.SaveChangesAsync(stoppingToken);
                var generated = await generator.GenerateInsightAsync(assessment, stoppingToken);
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
                insight.Status = AIInsightStatus.Completed;
            }
            catch (Exception ex)
            {
                insight.Status = AIInsightStatus.Failed;
                insight.ErrorMessage = ex.Message;
                logger.LogWarning(ex, "AI insight generation failed for assessment {AssessmentId}", assessmentId);
            }
            await repository.SaveChangesAsync(stoppingToken);
        }
    }

    private static RoadmapActivityType ParseActivityType(string value)
    {
        var normalized = value.Replace(" ", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal);
        return Enum.TryParse<RoadmapActivityType>(normalized, true, out var result) ? result : RoadmapActivityType.TransformationActivity;
    }
}
