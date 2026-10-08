using System.Globalization;
using System.Text;

namespace BenefitCardsPortal.Services;

public static class ArabicNameNormalizer
{
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var result = new StringBuilder(decomposed.Length);
        var previousWasSpace = true;
        foreach (var character in decomposed)
        {
            if (char.GetUnicodeCategory(character) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark || character == '\u0640')
                continue;
            var mapped = character switch
            {
                'أ' or 'إ' or 'آ' or 'ٱ' => 'ا',
                'ى' or 'ئ' => 'ي',
                'ؤ' => 'و',
                _ => character
            };
            if (char.IsWhiteSpace(mapped))
            {
                if (!previousWasSpace) result.Append(' ');
                previousWasSpace = true;
                continue;
            }
            result.Append(char.ToLowerInvariant(mapped));
            previousWasSpace = false;
        }
        return result.ToString().Trim();
    }
}
