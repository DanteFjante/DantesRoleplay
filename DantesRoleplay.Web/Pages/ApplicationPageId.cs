using DantesRoleplay.Applications;

namespace DantesRoleplay.Web.Pages;

/// <summary>Ruleset-neutral direct page convention for one registered application.</summary>
public static class ApplicationPageId
{
    private const string Suffix = "-play";

    public static string For(ApplicationIdentifier applicationId)
    {
        ArgumentNullException.ThrowIfNull(applicationId);
        return applicationId.Value + Suffix;
    }

    public static bool TryGetApplicationId(string? pageId, out ApplicationIdentifier? applicationId)
    {
        applicationId = null;
        if (!WebPageId.IsValid(pageId) || !pageId!.EndsWith(Suffix, StringComparison.Ordinal)) return false;

        var value = pageId[..^Suffix.Length];
        try
        {
            var parsed = ApplicationIdentifier.Parse(value);
            if (!string.Equals(For(parsed), pageId, StringComparison.Ordinal)) return false;
            applicationId = parsed;
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
