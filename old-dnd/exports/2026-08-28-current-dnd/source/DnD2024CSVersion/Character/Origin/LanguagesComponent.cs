namespace DnD2024CSVersion.Character.Origin;

public sealed record LanguagesComponent
{
    public IReadOnlySet<string> LanguageIds { get; init; } = new HashSet<string>();
}
