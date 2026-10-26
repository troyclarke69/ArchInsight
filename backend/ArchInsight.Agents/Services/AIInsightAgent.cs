using System.Text.Json;
using ArchInsight.Agents.Contracts;
using ArchInsight.AI.Contracts;
using ArchInsight.Domain;

namespace ArchInsight.Agents.Services;

public sealed class AIInsightAgent(IAIInsightGenerator generator) : IAgent
{
    public string Name => "AIInsightAgent";

    public async Task<AgentResult> ExecuteAsync(AgentContext context, CancellationToken ct)
    {
        var assessment = context.Assessment ?? throw new InvalidOperationException("The AI insight agent requires a completed assessment context.");
        var generated = await generator.GenerateInsightAsync(assessment, ct);
        var output = JsonSerializer.Serialize(generated);
        return new AgentResult(Name, "Generated executive insight, risk analysis, AI recommendations, and roadmap from deterministic assessment results.", [], [], output);
    }
}
