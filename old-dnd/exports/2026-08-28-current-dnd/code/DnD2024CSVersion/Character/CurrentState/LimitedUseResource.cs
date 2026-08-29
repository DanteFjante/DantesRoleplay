namespace DnD2024CSVersion.Character.CurrentState;

public sealed record LimitedUseResource(
    string Id,
    string Name,
    int Maximum,
    int Current,
    string RechargeRuleId);
