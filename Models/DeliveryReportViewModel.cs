namespace BenefitCardsPortal.Models;

public sealed class DeliveryReportViewModel
{
    public int? Month { get; init; }
    public int? Year { get; init; }
    public string Status { get; init; } = "all";
    public int TotalCount { get; init; }
    public int ReceivedCount { get; init; }
    public int PendingCount { get; init; }
    public int ReversalCount { get; init; }
    public int MatchingCount { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 50;
    public int PageCount => Math.Max(1, (int)Math.Ceiling(MatchingCount / (double)PageSize));
    public IReadOnlyList<int> Years { get; init; } = Array.Empty<int>();
    public IReadOnlyList<DeliveryReportRowViewModel> Rows { get; init; } = Array.Empty<DeliveryReportRowViewModel>();
}
