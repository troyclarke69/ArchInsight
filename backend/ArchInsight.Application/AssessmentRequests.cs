using FluentValidation;
using MediatR;
using ArchInsight.Domain;

namespace ArchInsight.Application;

public sealed record CreateAssessmentCommand(string Name, string OrganizationName, AssessmentType AssessmentType) : IRequest<AssessmentSummary>;
public sealed class CreateAssessmentValidator : AbstractValidator<CreateAssessmentCommand>
{
    public CreateAssessmentValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.OrganizationName).NotEmpty().MaximumLength(120);
        RuleFor(x => x.AssessmentType).IsInEnum();
    }
}
public sealed class CreateAssessmentHandler(IAssessmentRepository repository, IValidator<CreateAssessmentCommand> validator) : IRequestHandler<CreateAssessmentCommand, AssessmentSummary>
{
    public async Task<AssessmentSummary> Handle(CreateAssessmentCommand request, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(request, cancellationToken);
        var item = new Assessment { Name = request.Name.Trim(), OrganizationName = request.OrganizationName.Trim(), AssessmentType = request.AssessmentType };
        await repository.AddAsync(item, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        return AssessmentMapping.Summary(item);
    }
}

public sealed record QueueAssessmentCommand(Guid Id) : IRequest<bool>;
public sealed class QueueAssessmentHandler(IAssessmentRepository repository, IAssessmentQueue queue) : IRequestHandler<QueueAssessmentCommand, bool>
{
    public async Task<bool> Handle(QueueAssessmentCommand request, CancellationToken cancellationToken)
    {
        var assessment = await repository.FindAsync(request.Id, cancellationToken);
        if (assessment is null || assessment.Status is AssessmentStatus.Running or AssessmentStatus.Completed) return false;
        if (assessment.Artifacts.Count == 0) throw new InvalidOperationException("Upload at least one JSON artifact before running this assessment.");
        assessment.Status = AssessmentStatus.Running;
        assessment.ErrorMessage = null;
        await repository.SaveChangesAsync(cancellationToken);
        await queue.EnqueueAsync(request.Id, cancellationToken);
        return true;
    }
}

public static class AssessmentMapping
{
    public static AssessmentSummary Summary(Assessment a) => new(a.Id, a.Name, a.OrganizationName, a.AssessmentType, a.Status, a.CreatedDate, a.Result?.OverallScore, a.Result?.Recommendation, a.Result?.Findings.Count ?? 0);
    public static AssessmentDetail Detail(Assessment a) => new(a.Id, a.Name, a.OrganizationName, a.AssessmentType, a.Status, a.CreatedDate, a.Result?.CompletedDate, a.Result?.OverallScore, a.Result?.Recommendation, a.Result?.ExecutiveSummary, a.Result?.ApplicationCount ?? 0, a.ErrorMessage,
        a.Result?.ScoreCards.Select(s => new ScoreCardView(s.Dimension, s.Score)).ToArray() ?? [],
        a.Result?.Findings.Select(f => new FindingView(f.Title, f.Description, f.Category, f.Severity, f.RecommendationText)).ToArray() ?? [],
        a.Result?.Recommendations.OrderBy(r => r.Priority).Select(r => new RecommendationView(r.Priority, r.Title, r.Description, r.Timeframe)).ToArray() ?? [],
        a.Artifacts.Select(x => x.OriginalFileName).ToArray());
}
