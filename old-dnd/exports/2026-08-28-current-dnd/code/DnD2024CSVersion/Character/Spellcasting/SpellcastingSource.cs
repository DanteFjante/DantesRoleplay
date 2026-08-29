using DnD2024CSVersion.Rules.Model;

namespace DnD2024CSVersion.Character.Spellcasting;

public sealed record SpellcastingSource(
    string SourceId,
    Ability SpellcastingAbility);
