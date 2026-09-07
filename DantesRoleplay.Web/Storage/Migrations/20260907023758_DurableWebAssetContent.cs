using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DantesRoleplay.Web.Storage.Migrations;

public partial class DurableWebAssetContent : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(
        """
        CREATE TABLE "web_page_asset_content" (
            "ContentHash" TEXT NOT NULL CONSTRAINT "PK_web_page_asset_content" PRIMARY KEY,
            "Content" BLOB NOT NULL,
            CONSTRAINT "CK_web_page_asset_content_hash"
                CHECK (length("ContentHash") = 64 AND "ContentHash" NOT GLOB '*[^0-9A-F]*')
        );

        INSERT INTO "web_page_asset_content" ("ContentHash", "Content")
        SELECT "ContentHash", min("Content")
        FROM "web_page_asset"
        GROUP BY "ContentHash"
        HAVING min(hex("Content")) = max(hex("Content"));

        CREATE TABLE "web_page_asset_compact" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_web_page_asset" PRIMARY KEY AUTOINCREMENT,
            "PageRevisionId" INTEGER NOT NULL,
            "Path" TEXT NOT NULL,
            "ContentType" TEXT NOT NULL,
            "ContentHash" TEXT NOT NULL,
            CONSTRAINT "FK_web_page_asset_web_page_revision_PageRevisionId"
                FOREIGN KEY ("PageRevisionId") REFERENCES "web_page_revision" ("Id") ON DELETE CASCADE,
            CONSTRAINT "FK_web_page_asset_web_page_asset_content_ContentHash"
                FOREIGN KEY ("ContentHash") REFERENCES "web_page_asset_content" ("ContentHash") ON DELETE RESTRICT
        );

        INSERT INTO "web_page_asset_compact" ("Id", "PageRevisionId", "Path", "ContentType", "ContentHash")
        SELECT "Id", "PageRevisionId", "Path", "ContentType", "ContentHash"
        FROM "web_page_asset";

        DROP TABLE "web_page_asset";
        ALTER TABLE "web_page_asset_compact" RENAME TO "web_page_asset";
        CREATE UNIQUE INDEX "IX_web_page_asset_PageRevisionId_Path"
            ON "web_page_asset" ("PageRevisionId", "Path");
        CREATE INDEX "IX_web_page_asset_ContentHash"
            ON "web_page_asset" ("ContentHash");
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(
        """
        CREATE TABLE "web_page_asset_expanded" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_web_page_asset" PRIMARY KEY AUTOINCREMENT,
            "PageRevisionId" INTEGER NOT NULL,
            "Path" TEXT NOT NULL,
            "ContentType" TEXT NOT NULL,
            "ContentHash" TEXT NOT NULL,
            "Content" BLOB NOT NULL,
            CONSTRAINT "FK_web_page_asset_web_page_revision_PageRevisionId"
                FOREIGN KEY ("PageRevisionId") REFERENCES "web_page_revision" ("Id") ON DELETE CASCADE
        );

        INSERT INTO "web_page_asset_expanded"
            ("Id", "PageRevisionId", "Path", "ContentType", "ContentHash", "Content")
        SELECT asset."Id", asset."PageRevisionId", asset."Path", asset."ContentType",
               asset."ContentHash", payload."Content"
        FROM "web_page_asset" AS asset
        JOIN "web_page_asset_content" AS payload
          ON payload."ContentHash" = asset."ContentHash";

        DROP TABLE "web_page_asset";
        DROP TABLE "web_page_asset_content";
        ALTER TABLE "web_page_asset_expanded" RENAME TO "web_page_asset";
        CREATE UNIQUE INDEX "IX_web_page_asset_PageRevisionId_Path"
            ON "web_page_asset" ("PageRevisionId", "Path");
        """);
}
