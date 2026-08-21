using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModlistManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class DropRequestTitle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Titles now live on the Mod and come from the workshop item. Before dropping the
            // per-request titles, salvage them for any mod whose workshop lookup never resolved a
            // title - a human-entered name beats showing "Workshop item 123456". Mods that already
            // have a real workshop title are left alone, and a later fetch overwrites either way.
            migrationBuilder.Sql("""
                UPDATE Mods
                SET Title = (
                    SELECT r.Title
                    FROM ModRequests r
                    WHERE r.ModId = Mods.Id
                      AND r.Title IS NOT NULL
                      AND TRIM(r.Title) <> ''
                    ORDER BY r.CreatedAtUtc
                    LIMIT 1
                )
                WHERE (Title IS NULL OR TRIM(Title) = '')
                  AND EXISTS (
                    SELECT 1 FROM ModRequests r
                    WHERE r.ModId = Mods.Id
                      AND r.Title IS NOT NULL
                      AND TRIM(r.Title) <> ''
                  );
                """);

            migrationBuilder.DropColumn(
                name: "Title",
                table: "ModRequests");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "ModRequests",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }
    }
}
