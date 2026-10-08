using BenefitCardsPortal.Data;
using BenefitCardsPortal.Models;
using Microsoft.EntityFrameworkCore;

namespace BenefitCardsPortal.Services;

public class CitizenSearchService(BenefitsDbContext dbContext) : ICitizenSearchService
{
    public async Task<IReadOnlyList<BenefitCardRecord>> SearchAsync(string? queryText, CancellationToken cancellationToken = default)
    {
        var queryValue = queryText?.Trim();
        if (string.IsNullOrWhiteSpace(queryValue)) return Array.Empty<BenefitCardRecord>();
        var isNumericQuery = queryValue.All(character => char.IsDigit(character) || char.IsWhiteSpace(character) || character == '-');
        var normalizedNationalNumber = isNumericQuery ? NormalizeDigits(queryValue) : string.Empty;
        if (isNumericQuery && normalizedNationalNumber.Length is 0 or > 32) return Array.Empty<BenefitCardRecord>();

        var query = dbContext.BenefitCardRecords.AsNoTracking().Include(record => record.BenefitBatch)
            .Where(record => record.BenefitBatch!.IsReviewed).AsQueryable();
        query = isNumericQuery
            ? query.Where(record => record.NationalNumber != null && record.NationalNumber.Contains(normalizedNationalNumber))
            : ApplyNormalizedNameSearch(query, ArabicNameNormalizer.Normalize(queryValue));

        return await query.OrderByDescending(record => record.BenefitBatch!.ValidityYear)
            .ThenByDescending(record => record.BenefitBatch!.ValidityMonth)
            .Take(100).ToListAsync(cancellationToken);
    }

    private static string NormalizeDigits(string value) => new(value.Where(char.IsDigit)
        .Select(character => (char)('0' + (int)char.GetNumericValue(character))).ToArray());

    private static IQueryable<BenefitCardRecord> ApplyNormalizedNameSearch(IQueryable<BenefitCardRecord> query, string normalizedName)
    {
        foreach (var word in normalizedName.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            query = query.Where(record => record.BeneficiaryNameNormalized.Contains(word));
        return query;
    }
}
