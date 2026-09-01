using DantesRoleplay.CatalogNavigation;

namespace DantesRoleplay.Web.Hosting;

public interface IWebReadableRulesAudienceProvider
{
    ReadableRuleAudience Current();
}

internal sealed class PublicWebReadableRulesAudienceProvider : IWebReadableRulesAudienceProvider
{
    public ReadableRuleAudience Current() => ReadableRuleAudience.Public;
}
