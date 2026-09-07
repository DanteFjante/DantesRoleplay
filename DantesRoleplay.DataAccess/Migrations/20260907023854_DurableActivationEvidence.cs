using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DantesRoleplay.DataAccess.Migrations;

public partial class DurableActivationEvidence : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(
        """
        CREATE TABLE "system_application_activation_document_identity" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_system_application_activation_document_identity" PRIMARY KEY AUTOINCREMENT,
            "ApplicationId" TEXT NOT NULL,
            "LogicalIdentity" TEXT NOT NULL,
            CONSTRAINT "AK_system_application_activation_document_identity_ApplicationId_Id"
                UNIQUE ("ApplicationId", "Id"),
            CONSTRAINT "FK_system_application_activation_document_identity_system_application_ApplicationId"
                FOREIGN KEY ("ApplicationId") REFERENCES "system_application" ("Id") ON DELETE RESTRICT
        );

        INSERT INTO "system_application_activation_document_identity" ("Id", "ApplicationId", "LogicalIdentity")
        SELECT row_number() OVER (ORDER BY "ApplicationId", "LogicalIdentity"),
               "ApplicationId", "LogicalIdentity"
        FROM "system_application_activation_document"
        GROUP BY "ApplicationId", "LogicalIdentity";

        CREATE UNIQUE INDEX "IX_system_application_activation_document_identity_ApplicationId_LogicalIdentity"
            ON "system_application_activation_document_identity" ("ApplicationId", "LogicalIdentity");

        CREATE TABLE "system_application_activation_document_evidence" (
            "IdentityId" INTEGER NOT NULL,
            "EvidenceVersion" INTEGER NOT NULL,
            "SourceId" TEXT NOT NULL,
            "Trust" INTEGER NOT NULL,
            "Precedence" INTEGER NOT NULL,
            "RelativePath" TEXT NOT NULL,
            "MediaType" TEXT NOT NULL,
            "ContentFingerprint" TEXT NOT NULL,
            "Length" INTEGER NOT NULL,
            "IsText" INTEGER NOT NULL,
            CONSTRAINT "PK_system_application_activation_document_evidence"
                PRIMARY KEY ("IdentityId", "EvidenceVersion"),
            CONSTRAINT "CK_system_application_activation_document_evidence_hash"
                CHECK (length("ContentFingerprint") = 64 AND "ContentFingerprint" NOT GLOB '*[^0-9A-F]*'),
            CONSTRAINT "CK_system_application_activation_document_evidence_values"
                CHECK ("EvidenceVersion" > 0 AND "Trust" IN (0, 1) AND "Length" >= 0),
            CONSTRAINT "FK_system_application_activation_document_evidence_system_application_activation_document_identity_IdentityId"
                FOREIGN KEY ("IdentityId") REFERENCES "system_application_activation_document_identity" ("Id") ON DELETE RESTRICT
        );

        INSERT INTO "system_application_activation_document_evidence"
            ("IdentityId", "EvidenceVersion", "SourceId", "Trust", "Precedence", "RelativePath",
             "MediaType", "ContentFingerprint", "Length", "IsText")
        SELECT evidence."IdentityId",
               row_number() OVER (
                   PARTITION BY evidence."IdentityId"
                   ORDER BY evidence."SourceId", evidence."Trust", evidence."Precedence",
                            evidence."RelativePath", evidence."MediaType", evidence."ContentFingerprint",
                            evidence."Length", evidence."IsText"),
               evidence."SourceId", evidence."Trust", evidence."Precedence", evidence."RelativePath",
               evidence."MediaType", evidence."ContentFingerprint", evidence."Length", evidence."IsText"
        FROM (
            SELECT DISTINCT identity."Id" AS "IdentityId", document."SourceId", document."Trust",
                   document."Precedence", document."RelativePath", document."MediaType",
                   document."ContentFingerprint", document."Length", document."IsText"
            FROM "system_application_activation_document" AS document
            JOIN "system_application_activation_document_identity" AS identity
              ON identity."ApplicationId" = document."ApplicationId"
             AND identity."LogicalIdentity" = document."LogicalIdentity"
        ) AS evidence;

        CREATE UNIQUE INDEX "IX_system_application_activation_document_evidence_IdentityId_SourceId_Trust_Precedence_RelativePath_MediaType_ContentFingerprint_Length_IsText"
            ON "system_application_activation_document_evidence"
               ("IdentityId", "SourceId", "Trust", "Precedence", "RelativePath", "MediaType",
                "ContentFingerprint", "Length", "IsText");

        CREATE TABLE "system_application_activation_document_compact" (
            "ApplicationId" TEXT NOT NULL,
            "ActivationRevision" INTEGER NOT NULL,
            "Ordinal" INTEGER NOT NULL,
            "IdentityId" INTEGER NOT NULL,
            "EvidenceVersion" INTEGER NOT NULL,
            CONSTRAINT "PK_system_application_activation_document"
                PRIMARY KEY ("ApplicationId", "ActivationRevision", "Ordinal"),
            CONSTRAINT "CK_system_application_activation_document_values"
                CHECK ("Ordinal" >= 0 AND "EvidenceVersion" > 0),
            CONSTRAINT "FK_system_application_activation_document_system_application_activation_revision_ApplicationId_ActivationRevision"
                FOREIGN KEY ("ApplicationId", "ActivationRevision")
                REFERENCES "system_application_activation_revision" ("ApplicationId", "ActivationRevision") ON DELETE CASCADE,
            CONSTRAINT "FK_system_application_activation_document_system_application_activation_document_identity_ApplicationId_IdentityId"
                FOREIGN KEY ("ApplicationId", "IdentityId")
                REFERENCES "system_application_activation_document_identity" ("ApplicationId", "Id") ON DELETE RESTRICT,
            CONSTRAINT "FK_system_application_activation_document_system_application_activation_document_evidence_IdentityId_EvidenceVersion"
                FOREIGN KEY ("IdentityId", "EvidenceVersion")
                REFERENCES "system_application_activation_document_evidence" ("IdentityId", "EvidenceVersion") ON DELETE RESTRICT
        );

        INSERT INTO "system_application_activation_document_compact"
            ("ApplicationId", "ActivationRevision", "Ordinal", "IdentityId", "EvidenceVersion")
        SELECT document."ApplicationId", document."ActivationRevision", document."Ordinal",
               identity."Id", evidence."EvidenceVersion"
        FROM "system_application_activation_document" AS document
        JOIN "system_application_activation_document_identity" AS identity
          ON identity."ApplicationId" = document."ApplicationId"
         AND identity."LogicalIdentity" = document."LogicalIdentity"
        JOIN "system_application_activation_document_evidence" AS evidence
          ON evidence."IdentityId" = identity."Id"
         AND evidence."SourceId" = document."SourceId"
         AND evidence."Trust" = document."Trust"
         AND evidence."Precedence" = document."Precedence"
         AND evidence."RelativePath" = document."RelativePath"
         AND evidence."MediaType" = document."MediaType"
         AND evidence."ContentFingerprint" = document."ContentFingerprint"
         AND evidence."Length" = document."Length"
         AND evidence."IsText" = document."IsText";

        DROP TABLE "system_application_activation_document";
        ALTER TABLE "system_application_activation_document_compact"
            RENAME TO "system_application_activation_document";
        CREATE UNIQUE INDEX "IX_system_application_activation_document_ApplicationId_ActivationRevision_IdentityId"
            ON "system_application_activation_document" ("ApplicationId", "ActivationRevision", "IdentityId");
        CREATE INDEX "IX_system_application_activation_document_ApplicationId_IdentityId"
            ON "system_application_activation_document" ("ApplicationId", "IdentityId");
        CREATE INDEX "IX_system_application_activation_document_IdentityId_EvidenceVersion"
            ON "system_application_activation_document" ("IdentityId", "EvidenceVersion");
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(
        """
        CREATE TABLE "system_application_activation_document_expanded" (
            "ApplicationId" TEXT NOT NULL,
            "ActivationRevision" INTEGER NOT NULL,
            "Ordinal" INTEGER NOT NULL,
            "LogicalIdentity" TEXT NOT NULL,
            "SourceId" TEXT NOT NULL,
            "Trust" INTEGER NOT NULL,
            "Precedence" INTEGER NOT NULL,
            "RelativePath" TEXT NOT NULL,
            "MediaType" TEXT NOT NULL,
            "ContentFingerprint" TEXT NOT NULL,
            "Length" INTEGER NOT NULL,
            "IsText" INTEGER NOT NULL,
            CONSTRAINT "PK_system_application_activation_document"
                PRIMARY KEY ("ApplicationId", "ActivationRevision", "Ordinal"),
            CONSTRAINT "CK_system_application_activation_document_hash"
                CHECK (length("ContentFingerprint") = 64 AND "ContentFingerprint" NOT GLOB '*[^0-9A-F]*'),
            CONSTRAINT "CK_system_application_activation_document_values"
                CHECK ("Ordinal" >= 0 AND "Trust" IN (0, 1) AND "Length" >= 0),
            CONSTRAINT "FK_system_application_activation_document_system_application_activation_revision_ApplicationId_ActivationRevision"
                FOREIGN KEY ("ApplicationId", "ActivationRevision")
                REFERENCES "system_application_activation_revision" ("ApplicationId", "ActivationRevision") ON DELETE CASCADE
        );

        INSERT INTO "system_application_activation_document_expanded"
            ("ApplicationId", "ActivationRevision", "Ordinal", "LogicalIdentity", "SourceId", "Trust",
             "Precedence", "RelativePath", "MediaType", "ContentFingerprint", "Length", "IsText")
        SELECT link."ApplicationId", link."ActivationRevision", link."Ordinal", identity."LogicalIdentity",
               evidence."SourceId", evidence."Trust", evidence."Precedence", evidence."RelativePath",
               evidence."MediaType", evidence."ContentFingerprint", evidence."Length", evidence."IsText"
        FROM "system_application_activation_document" AS link
        JOIN "system_application_activation_document_identity" AS identity
          ON identity."ApplicationId" = link."ApplicationId" AND identity."Id" = link."IdentityId"
        JOIN "system_application_activation_document_evidence" AS evidence
          ON evidence."IdentityId" = link."IdentityId"
         AND evidence."EvidenceVersion" = link."EvidenceVersion";

        DROP TABLE "system_application_activation_document";
        DROP TABLE "system_application_activation_document_evidence";
        DROP TABLE "system_application_activation_document_identity";
        ALTER TABLE "system_application_activation_document_expanded"
            RENAME TO "system_application_activation_document";
        CREATE UNIQUE INDEX "IX_system_application_activation_document_ApplicationId_ActivationRevision_LogicalIdentity"
            ON "system_application_activation_document" ("ApplicationId", "ActivationRevision", "LogicalIdentity");
        """);
}
