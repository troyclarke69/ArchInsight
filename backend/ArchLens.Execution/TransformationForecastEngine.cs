using ArchInsight.Domain;

namespace ArchLens.Execution;

public sealed class TransformationForecastEngine
{
    public object Forecast(IReadOnlyList<ArchitectureSnapshot> snapshots, IReadOnlyList<TransformationInitiative> initiatives)
    {
        var latest = snapshots.OrderBy(s => s.CreatedDate).LastOrDefault();
        var first = snapshots.OrderBy(s => s.CreatedDate).FirstOrDefault();
        var months = first is null || latest is null ? 0 : Math.Max(1, (int)Math.Ceiling((latest.CreatedDate - first.CreatedDate).TotalDays / 30));
        var debtVelocity = first is null || latest is null ? 0 : (latest.TechnicalDebt - first.TechnicalDebt) / (double)months;
        var active = initiatives.Where(i => i.Status is InitiativeStatus.Approved or InitiativeStatus.InProgress).ToArray();
        var reductionAt = (int monthsOut) => active.Sum(i => i.ExpectedRiskReduction * Math.Clamp(monthsOut / Math.Max(1d, i.TargetCompletionDate is null ? 6 : (i.TargetCompletionDate.Value - DateTimeOffset.UtcNow).TotalDays / 30d), 0d, 1d));
        var debtReductionAt = (int monthsOut) => active.Sum(i => i.ExpectedDebtReduction * Math.Clamp(monthsOut / Math.Max(1d, i.TargetCompletionDate is null ? 6 : (i.TargetCompletionDate.Value - DateTimeOffset.UtcNow).TotalDays / 30d), 0d, 1d));
        var horizon = new[] { 1, 3, 12 }.Select(m => new { months = m, horizon = m == 1 ? "30 Days" : m == 3 ? "90 Days" : "12 Months", risk = Math.Clamp((latest?.RiskScore ?? 0) + (int)Math.Round(Math.Max(0, debtVelocity) * m * .25) - reductionAt(m), 0, 100), debt = Math.Clamp((latest?.TechnicalDebt ?? 0) + (int)Math.Round(debtVelocity * m) - debtReductionAt(m), 0, 100), maturity = Math.Clamp((latest?.MaturityIndex ?? 0) + (int)Math.Round((active.Sum(i => i.ExpectedRiskReduction + i.ExpectedDebtReduction) / 24d) * m), 0, 100) }).ToArray();
        return new { current = latest is null ? null : new { latest.CreatedDate, latest.RiskScore, latest.TechnicalDebt, latest.MaturityIndex, latest.ModernizationScore }, debtVelocityPerMonth = debtVelocity, debtTrend = debtVelocity > 0.5 ? "Increasing" : debtVelocity < -0.5 ? "Decreasing" : "Stable", riskOutlook = horizon.Select(x => new { x.horizon, x.risk, outlook = x.risk >= 75 ? "Risk Critical" : x.risk >= (latest?.RiskScore ?? 0) + 8 ? "Risk Increasing" : "Risk Stable" }), maturityForecast = horizon.Select(x => new { x.horizon, x.maturity }), horizons = horizon, assumptions = new[] { "Forecasts are heuristic projections from saved snapshots and active initiative estimates, not delivery guarantees.", "When no historical snapshots exist, the latest governance snapshot is used as the baseline." } };
    }

    public object Benchmark(IEnumerable<ArchitectureSnapshot> snapshots)
    {
        var data = snapshots.ToArray();
        var average = new { CloudReadiness = Mean(data.Select(x => x.CloudReadiness)), Security = Mean(data.Select(x => x.SecurityScore)), Maturity = Mean(data.Select(x => x.MaturityIndex)), Observability = Mean(data.Select(x => x.ConsistencyScore)), Governance = data.Length == 0 ? 0 : 75 };
        return new { scope = "Internal demo portfolio", dimensions = new[] { Dimension("Cloud readiness", average.CloudReadiness), Dimension("Security", average.Security), Dimension("Architecture maturity", average.Maturity), Dimension("Observability", average.Observability), Dimension("Governance", average.Governance) }, score = (average.CloudReadiness + average.Security + average.Maturity + average.Observability + average.Governance) / 5, tier = Tier((average.CloudReadiness + average.Security + average.Maturity + average.Observability + average.Governance) / 5), note = "Internal reference bands only; no external organization data is used." };
    }

    private static object Dimension(string name, int score) => new { name, score, tier = Tier(score) };
    private static string Tier(int score) => score >= 85 ? "Leading" : score >= 70 ? "Advanced" : score >= 45 ? "Developing" : "Emerging";
    private static int Mean(IEnumerable<int> values) { var array = values.ToArray(); return array.Length == 0 ? 0 : (int)Math.Round(array.Average()); }
}
