using System.ComponentModel.DataAnnotations;

namespace EVChargeSwap.Common.Pagination;

public sealed class PaginationRequest : IValidatableObject
{
    [Range(1, int.MaxValue)]
    public int Page { get; init; } = 1;

    [Range(1, 100)]
    public int PageSize { get; init; } = 20;

    public int GetOffset() => checked((Page - 1) * PageSize);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Page > 0 && PageSize > 0 && (long)(Page - 1) * PageSize > int.MaxValue)
        {
            yield return new ValidationResult("The requested page is too large.", [nameof(Page)]);
        }
    }
}
