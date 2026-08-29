namespace DantesRoleplay.SchemaValidation;

public static class SystemJsonSchemaProfile
{
    public const string Version1Id = "system-json-schema-2020-12/v1";
    public const string Version2Id = "system-json-schema-2020-12/v2";
    public const string Id = Version2Id;
    public const string MetaSchemaUri = "https://json-schema.org/draft/2020-12/schema";
    public const int MaximumSchemaBytes = 64 * 1024;
    public const int MaximumSchemaDepth = 32;
    public const int MaximumSchemaNodes = 2_000;
    public const int MaximumDefinitions = 128;
    public const int MaximumReferences = 256;
    public const int MaximumPatterns = 128;
    public const int MaximumPatternLength = 256;
    public const int MaximumPatternRepetition = 256;
    public const int MaximumUnboundedPatternValueLength = 1_000;
    public const int MaximumValueBytes = 1024 * 1024;
    public const int MaximumValueDepth = 64;
    public const int MaximumValueNodes = 10_000;
    public const int MaximumDiagnostics = 32;
}

public sealed record SchemaDiagnostic(string Code, string Pointer, string Message);

public sealed record SchemaCompilationResult(
    bool IsAccepted,
    string ProfileId,
    string NormalizedSchema,
    string SchemaHash,
    IReadOnlyList<SchemaDiagnostic> Diagnostics);

public enum SchemaValueStatus
{
    Valid,
    Invalid,
    Rejected
}

public sealed record SchemaValueValidationResult(
    SchemaValueStatus Status,
    IReadOnlyList<SchemaDiagnostic> Diagnostics);

public interface IBoundedJsonSchemaValidator
{
    SchemaCompilationResult Compile(string schemaJson);
    SchemaValueValidationResult Validate(string normalizedSchema, string valueJson);
    SchemaValueValidationResult Validate(string profileId, string normalizedSchema, string valueJson);
}
