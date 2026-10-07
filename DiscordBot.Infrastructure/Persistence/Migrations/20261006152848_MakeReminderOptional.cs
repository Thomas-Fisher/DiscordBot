using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiscordBot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MakeReminderOptional : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "ReminderMinutesBefore",
                table: "Events",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            // The reminder used to message everyone signed up and now pings people who have not responded, so existing events start
            // with none rather than suddenly pinging people at a time chosen for the old behaviour
            migrationBuilder.Sql("UPDATE \"Events\" SET \"ReminderMinutesBefore\" = NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE \"Events\" SET \"ReminderMinutesBefore\" = 30 WHERE \"ReminderMinutesBefore\" IS NULL;");

            migrationBuilder.AlterColumn<int>(
                name: "ReminderMinutesBefore",
                table: "Events",
                type: "integer",
                nullable: false,
                defaultValue: 30,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);
        }
    }
}
