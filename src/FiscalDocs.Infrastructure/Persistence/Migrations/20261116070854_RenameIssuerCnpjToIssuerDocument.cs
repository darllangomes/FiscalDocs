using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FiscalDocs.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RenameIssuerCnpjToIssuerDocument : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "IssuerCnpj",
                table: "fiscal_documents",
                newName: "IssuerDocument");

            migrationBuilder.RenameIndex(
                name: "IX_fiscal_documents_IssuerCnpj_IssueDate",
                table: "fiscal_documents",
                newName: "IX_fiscal_documents_IssuerDocument_IssueDate");

            migrationBuilder.RenameColumn(
                name: "issuer_cnpj",
                table: "daily_issuer_aggregates",
                newName: "issuer_document");

            // Eventos ainda na outbox (não publicados) trazem o nome antigo do campo.
            migrationBuilder.Sql("""
                UPDATE outbox_messages
                SET payload = (payload - 'issuerCnpj') || jsonb_build_object('issuerDocument', payload -> 'issuerCnpj')
                WHERE payload ? 'issuerCnpj';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "IssuerDocument",
                table: "fiscal_documents",
                newName: "IssuerCnpj");

            migrationBuilder.RenameIndex(
                name: "IX_fiscal_documents_IssuerDocument_IssueDate",
                table: "fiscal_documents",
                newName: "IX_fiscal_documents_IssuerCnpj_IssueDate");

            migrationBuilder.RenameColumn(
                name: "issuer_document",
                table: "daily_issuer_aggregates",
                newName: "issuer_cnpj");

            migrationBuilder.Sql("""
                UPDATE outbox_messages
                SET payload = (payload - 'issuerDocument') || jsonb_build_object('issuerCnpj', payload -> 'issuerDocument')
                WHERE payload ? 'issuerDocument';
                """);
        }
    }
}
