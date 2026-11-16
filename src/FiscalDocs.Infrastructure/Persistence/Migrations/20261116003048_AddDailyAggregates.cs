using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FiscalDocs.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDailyAggregates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "daily_issuer_aggregates",
                columns: table => new
                {
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    issuer_cnpj = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: false),
                    issuer_state = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    document_count = table.Column<int>(type: "integer", nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_daily_issuer_aggregates", x => new { x.date, x.issuer_cnpj, x.issuer_state });
                });

            migrationBuilder.CreateTable(
                name: "processed_messages",
                columns: table => new
                {
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_processed_messages", x => x.message_id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "daily_issuer_aggregates");

            migrationBuilder.DropTable(
                name: "processed_messages");
        }
    }
}
