using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FiscalDocs.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddXminConcurrencyToken : Migration
    {
        // xmin is a Postgres system column present on every table; this migration only
        // updates the model snapshot so EF treats it as a concurrency token.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
