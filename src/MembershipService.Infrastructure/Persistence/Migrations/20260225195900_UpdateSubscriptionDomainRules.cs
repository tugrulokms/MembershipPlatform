using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MembershipService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UpdateSubscriptionDomainRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // EF Core generates DROP IDENTITY after ALTER TYPE, but PostgreSQL requires
            // the reverse order. We use raw SQL to control the sequence explicitly.

            // 1. Drop the FK on Entitlements that references Subscriptions.Id
            migrationBuilder.Sql(@"ALTER TABLE ""Entitlements"" DROP CONSTRAINT IF EXISTS ""FK_Entitlements_Subscriptions_SubscriptionId"";");

            // 2. Drop IDENTITY before changing the column type (PostgreSQL requirement)
            migrationBuilder.Sql(@"ALTER TABLE ""Subscriptions"" ALTER COLUMN ""Id"" DROP IDENTITY IF EXISTS;");

            // 3. Change Subscriptions.Id to uuid — table is empty so USING is safe
            migrationBuilder.Sql(@"ALTER TABLE ""Subscriptions"" ALTER COLUMN ""Id"" TYPE uuid USING gen_random_uuid();");

            // 4. Change Entitlements.SubscriptionId to uuid
            migrationBuilder.Sql(@"ALTER TABLE ""Entitlements"" ALTER COLUMN ""SubscriptionId"" TYPE uuid USING gen_random_uuid();");

            // 5. Restore the FK constraint
            migrationBuilder.Sql(@"ALTER TABLE ""Entitlements"" ADD CONSTRAINT ""FK_Entitlements_Subscriptions_SubscriptionId"" FOREIGN KEY (""SubscriptionId"") REFERENCES ""Subscriptions""(""Id"") ON DELETE RESTRICT;");

            migrationBuilder.AddColumn<DateTime>(
                name: "PastDueAt",
                table: "Subscriptions",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PastDueAt",
                table: "Subscriptions");

            migrationBuilder.AlterColumn<int>(
                name: "Id",
                table: "Subscriptions",
                type: "integer",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid")
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AlterColumn<int>(
                name: "SubscriptionId",
                table: "Entitlements",
                type: "integer",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid");
        }
    }
}
