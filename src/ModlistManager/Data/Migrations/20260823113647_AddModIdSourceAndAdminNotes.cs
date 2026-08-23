using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModlistManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddModIdSourceAndAdminNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AdminNotes",
                table: "Mods",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ModIdSource",
                table: "Mods",
                type: "TEXT",
                nullable: false,
                defaultValue: "Unknown");

            // Existing mods pre-date the column, but the fetch log already records which lookup
            // succeeded, so recover it rather than leaving every historical mod as "Unknown".
            migrationBuilder.Sql("""
                UPDATE Mods
                SET ModIdSource = CASE
                    WHEN FetchLog LIKE '%SteamCMD found%' THEN 'SteamCmd'
                    WHEN FetchLog LIKE '%from the workshop description%' THEN 'SteamWorkshopApi'
                    ELSE 'Unknown'
                END
                WHERE FetchStatus = 'Completed' AND FetchLog IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AdminNotes",
                table: "Mods");

            migrationBuilder.DropColumn(
                name: "ModIdSource",
                table: "Mods");
        }
    }
}
