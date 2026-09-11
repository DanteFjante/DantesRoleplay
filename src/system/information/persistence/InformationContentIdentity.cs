using DantesRoleplay.Content;
using DantesRoleplay.Ecs;
using DantesRoleplay.Information;

namespace DantesRoleplay.DataAccess;

internal static class InformationContentIdentity
{
    internal static string SourceHash(string id, string scopeId, string name, string description,
        string schemaJson, EcsComponentReference? reference) => reference is null
        ? ContentHash.Of(id, scopeId, name, description, schemaJson)
        : ContentHash.Of(id, scopeId, name, description, schemaJson,
            reference.QualifiedTypeId, reference.TypeVersion.ToString(), reference.SchemaHash);

    internal static string RecordHash(string id, string sourceId, string title, string content,
        string metadataJson, int schemaSourceRevision) => ContentHash.Of(id, sourceId, title, content, metadataJson);

    internal static EcsComponentReference? Reference(InformationSource source) =>
        source.MetadataSchemaQualifiedId is null ? null : new(source.MetadataSchemaQualifiedId,
            source.MetadataSchemaVersion!.Value, source.MetadataSchemaHash!);

    internal static bool ReferenceMatches(InformationSource source, EcsComponentReference? reference) =>
        source.MetadataSchemaQualifiedId == reference?.QualifiedTypeId
        && source.MetadataSchemaVersion == reference?.TypeVersion
        && source.MetadataSchemaHash == reference?.SchemaHash;
}
