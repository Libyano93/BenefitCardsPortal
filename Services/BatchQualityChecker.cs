using BenefitCardsPortal.Models;

namespace BenefitCardsPortal.Services;

public static class BatchQualityChecker
{
    public static IReadOnlyList<BatchQualityFinding> Analyze(IEnumerable<(string BeneficiaryName, string? SerialNumber, string? CardNumber, string? NationalNumber)> source)
    {
        var rows = source.ToArray();
        var findings = new List<BatchQualityFinding>();
        AddMissing("name", "اسم المستفيد فارغ", rows.Count(row => string.IsNullOrWhiteSpace(row.BeneficiaryName)), true);
        AddMissing("serial", "رقم التسلسل فارغ", rows.Count(row => string.IsNullOrWhiteSpace(row.SerialNumber)), true);
        AddMissing("card", "جزء من رقم البطاقة فارغ", rows.Count(row => string.IsNullOrWhiteSpace(row.CardNumber)), false);
        AddMissing("national", "جزء الرقم الوطني فارغ", rows.Count(row => string.IsNullOrWhiteSpace(row.NationalNumber)), false);
        AddDuplicates("serial-duplicate", "أرقام تسلسل مكررة", rows.Select(row => row.SerialNumber));
        AddDuplicates("national-duplicate", "أجزاء أرقام وطنية مكررة", rows.Select(row => row.NationalNumber));
        AddLength("serial-length", "أرقام تسلسل لا تحتوي 7 أرقام", rows.Select(row => row.SerialNumber), 7);
        AddLength("card-length", "أجزاء أرقام بطاقات لا تحتوي 6 أرقام", rows.Select(row => row.CardNumber), 6);
        AddLength("national-length", "أجزاء أرقام وطنية لا تحتوي 9 أرقام", rows.Select(row => row.NationalNumber), 9);
        return findings;

        void AddMissing(string code, string message, int count, bool blocking)
        {
            if (count > 0) findings.Add(new(code, message, count, blocking));
        }

        void AddDuplicates(string code, string message, IEnumerable<string?> values)
        {
            var count = values.Where(value => !string.IsNullOrWhiteSpace(value))
                .GroupBy(value => new string(value!.Where(char.IsDigit).ToArray()), StringComparer.Ordinal)
                .Where(group => group.Key.Length > 0 && group.Count() > 1).Sum(group => group.Count());
            AddMissing(code, message, count, true);
        }

        void AddLength(string code, string message, IEnumerable<string?> values, int expected)
        {
            var count = values.Count(value => !string.IsNullOrWhiteSpace(value) && value!.Count(char.IsDigit) != expected);
            AddMissing(code, message, count, false);
        }
    }

    public static IReadOnlyList<BatchQualityFinding> Analyze(IEnumerable<BenefitCardRecord> records) =>
        Analyze(records.Select(record => (record.BeneficiaryName, record.SerialNumber, record.CardNumber, record.NationalNumber)));

    public static IReadOnlyList<BatchQualityFinding> Analyze(IEnumerable<AddCardViewModel> rows) =>
        Analyze(rows.Select(row => (row.BeneficiaryName, row.SerialNumber, row.CardNumber, row.NationalNumber)));
}
