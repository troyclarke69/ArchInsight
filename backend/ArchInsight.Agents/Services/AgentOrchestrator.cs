using ArchInsight.Agents.Contracts;

namespace ArchInsight.Agents.Services;

public sealed class AgentOrchestrator(IEnumerable<IAgent> agents) : IAgentOrchestrator
{
    public async Task<IReadOnlyList<AgentResult>> ExecuteAsync(AgentContext context, CancellationToken ct)
    {
        var results = new List<AgentResult>();
        foreach (var agent in agents.Where(a => a.Name is not "AIInsightAgent" and not "ExecutiveAdvisorAgent").OrderBy(a => Order(a.Name)))
        {
            ct.ThrowIfCancellationRequested();
            results.Add(await agent.ExecuteAsync(context, ct));
        }
        return results;
    }
    private static int Order(string name) => name switch { "ArtifactDiscoveryAgent" => 0, "ModernizationAgent" => 1, "ServiceGovernanceAgent" => 2, "TechnologyRiskAgent" => 3, "ExecutiveAdvisorAgent" => 4, _ => 100 };
}
