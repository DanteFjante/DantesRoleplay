namespace DantesRoleplay.Mechanics;

/// <summary>Catalog-declared display selectors over an already bounded graph page source.</summary>
public sealed record GraphPageSelectionRequirement
{
    public string EnabledInput { get; init; } = "";
    public string ExpectedFingerprintInput { get; init; } = "";
    public string SearchInput { get; init; } = "";
    public int PageSize { get; init; }
    public IReadOnlyDictionary<string, GraphPageFieldRequirement> Fields { get; init; } =
        new Dictionary<string, GraphPageFieldRequirement>();
    public IReadOnlyList<GraphPageComparisonRequirement> Comparisons { get; init; } = [];

    internal bool Valid(IReadOnlyList<GraphSnapshotStepRequirement> steps, string pageStep) =>
        Token(EnabledInput) && Token(ExpectedFingerprintInput) && Token(SearchInput) &&
        new[] { EnabledInput, ExpectedFingerprintInput, SearchInput }.Distinct(StringComparer.Ordinal).Count() == 3 &&
        PageSize is >= 1 and <= ProjectionLimits.MaxGraphPageSize && Fields is { Count: >= 1 and <= 16 } &&
        Fields.All(pair => Token(pair.Key) && pair.Value is not null && pair.Value.Valid(steps, pageStep)) &&
        Fields.Values.Where(value => value.FilterInput is not null).Select(value => value.FilterInput)
            .Concat([EnabledInput, ExpectedFingerprintInput, SearchInput]).Distinct(StringComparer.Ordinal).Count() ==
            Fields.Values.Count(value => value.FilterInput is not null) + 3 &&
        Comparisons is { Count: <= 8 } && Comparisons.All(value => value is not null && value.Valid(steps, pageStep));

    internal static bool Token(string? value) => value is { Length: >= 1 and <= 100 } &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_');
}

public sealed record GraphPageFieldRequirement
{
    public IReadOnlyList<GraphPageValueSource> Sources { get; init; } = [];
    public IReadOnlyList<string> AllowedValues { get; init; } = [];
    public string? FilterInput { get; init; }
    public bool Required { get; init; }
    public bool Facet { get; init; }
    public bool Search { get; init; }
    public int MaxLength { get; init; } = 1000;

    internal bool Valid(IReadOnlyList<GraphSnapshotStepRequirement> steps, string pageStep) =>
        Sources is { Count: >= 1 and <= 8 } && Sources.All(value => value is not null && value.Valid(steps, pageStep)) &&
        MaxLength is >= 1 and <= 4000 && AllowedValues is { Count: <= 32 } &&
        AllowedValues.All(value => value.Length is > 0 and <= 100 && value == value.Trim()) &&
        AllowedValues.Distinct(StringComparer.Ordinal).Count() == AllowedValues.Count &&
        (FilterInput is null || GraphPageSelectionRequirement.Token(FilterInput)) &&
        (!Facet || AllowedValues.Count > 0);
}

public sealed record GraphPageValueSource
{
    public string? EntityField { get; init; }
    public string? ComponentId { get; init; }
    public string? Path { get; init; }
    public string? Constant { get; init; }
    public string? StepId { get; init; }

    internal bool Valid(IReadOnlyList<GraphSnapshotStepRequirement> steps, string pageStep)
    {
        var step = steps.FirstOrDefault(value => value.Id == (StepId ?? pageStep));
        if (step is null) return false;
        if (EntityField is not null)
            return EntityField is "id" or "name" && ComponentId is null && Path is null && Constant is null;
        return ComponentId is { Length: >= 1 and <= 200 } && step.ComponentIds.Contains(ComponentId, StringComparer.Ordinal) &&
            ((Path is { Length: >= 2 and <= 300 } && Path.StartsWith('/') && Path.Split('/').Length <= 9 && Constant is null) ||
             (Path is null && Constant is { Length: >= 1 and <= 100 } && Constant == Constant.Trim()));
    }
}

public sealed record GraphPageComparisonRequirement
{
    public GraphPageValueSource Left { get; init; } = new();
    public GraphPageValueSource Right { get; init; } = new();
    public string Operator { get; init; } = "";
    public bool AllowMissingLeft { get; init; }

    internal bool Valid(IReadOnlyList<GraphSnapshotStepRequirement> steps, string pageStep) =>
        Left is not null && Right is not null && Left.Valid(steps, pageStep) && Right.Valid(steps, pageStep) &&
        Operator is "less-than-or-equal" or "greater-than";
}
