using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DantesRoleplay.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class ContentAddressedBlobStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "blob_asset",
                columns: table => new
                {
                    Sha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    MediaType = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ByteLength = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_blob_asset", x => x.Sha256);
                    table.CheckConstraint("CK_blob_asset_byte_length", "\"ByteLength\" BETWEEN 1 AND 10485760");
                    table.CheckConstraint("CK_blob_asset_media_type", "\"MediaType\" IN ('image/png', 'image/jpeg', 'image/webp')");
                    table.CheckConstraint("CK_blob_asset_sha256", "length(\"Sha256\") = 64 AND \"Sha256\" NOT GLOB '*[^0-9a-f]*'");
                });

            migrationBuilder.CreateTable(
                name: "blob_upload_session",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 44, nullable: false),
                    TokenHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ExpectedSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    MediaType = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ExpectedByteLength = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    UploadedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    FinalizedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_blob_upload_session", x => x.Id);
                    table.CheckConstraint("CK_blob_upload_session_byte_length", "\"ExpectedByteLength\" BETWEEN 1 AND 10485760");
                    table.CheckConstraint("CK_blob_upload_session_hashes", "length(\"TokenHash\") = 64 AND \"TokenHash\" NOT GLOB '*[^0-9a-f]*' AND length(\"ExpectedSha256\") = 64 AND \"ExpectedSha256\" NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_blob_upload_session_id", "length(\"Id\") = 44 AND substr(\"Id\", 1, 12) = 'blob-upload.' AND substr(\"Id\", 13) NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_blob_upload_session_media_type", "\"MediaType\" IN ('image/png', 'image/jpeg', 'image/webp')");
                    table.CheckConstraint("CK_blob_upload_session_state", "\"State\" IN ('pending', 'uploaded', 'finalized')");
                });

            migrationBuilder.CreateIndex(
                name: "IX_blob_upload_session_ExpectedSha256_State",
                table: "blob_upload_session",
                columns: new[] { "ExpectedSha256", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_blob_upload_session_ExpiresAtUtc",
                table: "blob_upload_session",
                column: "ExpiresAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "blob_asset");

            migrationBuilder.DropTable(
                name: "blob_upload_session");
        }
    }
}
