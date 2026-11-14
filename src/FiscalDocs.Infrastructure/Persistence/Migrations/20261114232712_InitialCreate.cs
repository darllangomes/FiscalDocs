using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FiscalDocs.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "fiscal_documents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    AccessKey = table.Column<string>(type: "character varying(44)", maxLength: 44, nullable: false),
                    Number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Series = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    IssueDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IssuerCnpj = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: false),
                    IssuerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IssuerState = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    RecipientDocument = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    RecipientName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    RecipientState = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    TotalAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    XmlHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RawXml = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fiscal_documents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_fiscal_documents_AccessKey",
                table: "fiscal_documents",
                column: "AccessKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fiscal_documents_IssueDate",
                table: "fiscal_documents",
                column: "IssueDate");

            migrationBuilder.CreateIndex(
                name: "IX_fiscal_documents_IssuerCnpj_IssueDate",
                table: "fiscal_documents",
                columns: new[] { "IssuerCnpj", "IssueDate" });

            migrationBuilder.CreateIndex(
                name: "IX_fiscal_documents_IssuerState_IssueDate",
                table: "fiscal_documents",
                columns: new[] { "IssuerState", "IssueDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "fiscal_documents");
        }
    }
}
