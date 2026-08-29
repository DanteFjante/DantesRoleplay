using System.Text.RegularExpressions;

namespace DantesRoleplay.Web.Pages;

public static partial class WebPageId
{
    public const int MaximumLength = 120;

    public static bool IsValid(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= MaximumLength &&
        ValidPattern().IsMatch(value);

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex ValidPattern();
}
