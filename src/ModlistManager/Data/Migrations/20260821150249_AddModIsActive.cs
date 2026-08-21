using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModlistManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddModIsActive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "Mods",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "Mods");
        }
    }
}
