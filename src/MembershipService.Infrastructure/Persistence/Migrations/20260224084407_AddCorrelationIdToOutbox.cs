using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MembershipService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCorrelationIdToOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InboxMessages_ProcessedAt",
                table: "InboxMessages");

            migrationBuilder.AddColumn<string>(
                name: "CorrelationId",
                table: "OutboxMessages",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CorrelationId",
                table: "OutboxMessages");

            migrationBuilder.CreateIndex(
                name: "IX_InboxMessages_ProcessedAt",
                table: "InboxMessages",
                column: "ProcessedAt",
                filter: "\"ProcessedAt\" IS NULL");
        }
    }
}
