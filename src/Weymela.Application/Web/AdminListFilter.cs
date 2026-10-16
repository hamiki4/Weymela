namespace Weymela.Application.Web;

public sealed record AdminListFilter(string? Search = null, string? Status = null, string? Type = null,
    DateTime? From = null, DateTime? To = null, int Page = 1, int PageSize = 50)
{
    public string Term => Search?.Trim().ToLowerInvariant() ?? "";
    public int Offset => (Page - 1) * PageSize;
    public void Validate()
    {
        if (Page < 1 || Page > 100000 || PageSize < 1 || PageSize > 100 || Search?.Length > 200
            || From is not null && To is not null && From > To)
            throw new ApplicationFailure(FailureKind.Validation, "Choose valid filters and a page size from 1 to 100.");
    }
}
