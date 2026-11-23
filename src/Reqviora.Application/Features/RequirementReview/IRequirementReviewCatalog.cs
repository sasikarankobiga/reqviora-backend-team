
namespace Reqviora.Application.Features.RequirementReview;

public interface IRequirementReviewCatalog
{
    Task<IReadOnlyList<ReviewedRequirement>> ListByProjectAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);
}
