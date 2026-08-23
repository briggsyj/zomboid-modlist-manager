using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModlistManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddModSortOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "Mods",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            // Seed the order from how the modlist is sorted today - workshop ID ascending,
            // numerically - so upgrading doesn't reshuffle anyone's exported load order. CAST is
            // what makes it numeric: workshop IDs are stored as text, and older 9-digit items would
            // otherwise sort after 10-digit ones. Mods not on the modlist keep 0 and are numbered
            // when they're approved.
            migrationBuilder.Sql("""
                WITH ordered AS (
                    SELECT Id, ROW_NUMBER() OVER (ORDER BY CAST(WorkshopId AS INTEGER), Id) AS rn
                    FROM Mods
                    WHERE IsInModlist = 1
                )
                UPDATE Mods
                SET SortOrder = (SELECT rn FROM ordered WHERE ordered.Id = Mods.Id)
                WHERE IsInModlist = 1;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "Mods");
        }
    }
}
