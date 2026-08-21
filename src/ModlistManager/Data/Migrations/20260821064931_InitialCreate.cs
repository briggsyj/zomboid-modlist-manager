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
                name: "ModRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Game = table.Column<string>(type: "TEXT", nullable: false),
                    WorkshopUrlInput = table.Column<string>(type: "TEXT", nullable: false),
                    WorkshopId = table.Column<string>(type: "TEXT", nullable: false),
                    RequesterName = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DecidedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    AdminNotes = table.Column<string>(type: "TEXT", nullable: true),
                    FetchStatus = table.Column<string>(type: "TEXT", nullable: false),
                    FetchLog = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModRequests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ModRequestModIds",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ModRequestId = table.Column<int>(type: "INTEGER", nullable: false),
                    ModId = table.Column<string>(type: "TEXT", nullable: false),
                    ModName = table.Column<string>(type: "TEXT", nullable: true),
                    IsManual = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModRequestModIds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ModRequestModIds_ModRequests_ModRequestId",
                        column: x => x.ModRequestId,
                        principalTable: "ModRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ModRequestModIds_ModRequestId",
                table: "ModRequestModIds",
                column: "ModRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_ModRequests_Game",
                table: "ModRequests",
                column: "Game");

            migrationBuilder.CreateIndex(
                name: "IX_ModRequests_RequesterName",
                table: "ModRequests",
                column: "RequesterName");

            migrationBuilder.CreateIndex(
                name: "IX_ModRequests_Status",
                table: "ModRequests",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdminCredentials");

            migrationBuilder.DropTable(
                name: "ModRequestModIds");

            migrationBuilder.DropTable(
                name: "ModRequests");
        }
    }
}
