using System.Globalization;
using System.Text;

namespace SuperRecruiter.Shared.Helpers;

public static class RealmSlugHelper
{
    /// <summary>
    /// Converts a realm display name into the slug used by Raider.IO, WarcraftLogs and Armory URLs.
    /// Apostrophes are dropped ("Blade's Edge" -> "blades-edge"), diacritics are folded
    /// ("Chants éternels" -> "chants-eternels") and any other non-alphanumeric run becomes a dash.
    /// </summary>
    public static string ToSlug(string realm)
    {
        if (string.IsNullOrWhiteSpace(realm))
            return string.Empty;

        var withoutApostrophes = realm.Replace("'", string.Empty).Replace("\u2019", string.Empty);
        var normalized = withoutApostrophes.Normalize(NormalizationForm.FormD);

        var builder = new StringBuilder(normalized.Length);
        var pendingDash = false;

        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;

            if (char.IsLetterOrDigit(c))
            {
                if (pendingDash && builder.Length > 0)
                    builder.Append('-');

                pendingDash = false;
                builder.Append(char.ToLowerInvariant(c));
            }
            else
            {
                pendingDash = true;
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
