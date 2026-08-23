using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModlistManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPzModIdIsEnabled : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsEnabled",
                table: "PzModIds",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsEnabled",
                table: "PzModIds");
        }
    }
}
