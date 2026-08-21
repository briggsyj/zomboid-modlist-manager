using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModlistManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AdminCredentials",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Username = table.Column<string>(type: "TEXT", nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminCredentials", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Mods",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Game = table.Column<string>(type: "TEXT", nullable: false),
                    WorkshopId = table.Column<string>(type: "TEXT", nullable: false),
                    FetchStatus = table.Column<string>(type: "TEXT", nullable: false),
                    FetchLog = table.Column<string>(type: "TEXT", nullable: true),
                    IsInModlist = table.Column<bool>(type: "INTEGER", nullable: false),
                    AddedToModlistAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Mods", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ModRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    ModId = table.Column<int>(type: "INTEGER", nullable: false),
                    RequesterName = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DecidedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    AdminNotes = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ModRequests_Mods_ModId",
                        column: x => x.ModId,
                        principalTable: "Mods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PzModIds",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ModId = table.Column<int>(type: "INTEGER", nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: true),
                    IsManual = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PzModIds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PzModIds_Mods_ModId",
                        column: x => x.ModId,
                        principalTable: "Mods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ModRequests_ModId",
                table: "ModRequests",
                column: "ModId");

            migrationBuilder.CreateIndex(
                name: "IX_ModRequests_RequesterName",
                table: "ModRequests",
                column: "RequesterName");

            migrationBuilder.CreateIndex(
                name: "IX_ModRequests_Status",
                table: "ModRequests",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Mods_Game_WorkshopId",
                table: "Mods",
                columns: new[] { "Game", "WorkshopId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PzModIds_ModId",
                table: "PzModIds",
                column: "ModId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdminCredentials");

            migrationBuilder.DropTable(
                name: "ModRequests");

            migrationBuilder.DropTable(
                name: "PzModIds");

            migrationBuilder.DropTable(
                name: "Mods");
        }
    }
}
