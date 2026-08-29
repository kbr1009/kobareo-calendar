using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KobareoCalendar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTimelinePreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserTimelinePreferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TargetUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    TargetResourceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserTimelinePreferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserTimelinePreferences_AspNetUsers_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserTimelinePreferences_AspNetUsers_TargetUserId",
                        column: x => x.TargetUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserTimelinePreferences_Resources_TargetResourceId",
                        column: x => x.TargetResourceId,
                        principalTable: "Resources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserTimelinePreferences_OwnerUserId_SortOrder",
                table: "UserTimelinePreferences",
                columns: new[] { "OwnerUserId", "SortOrder" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserTimelinePreferences_TargetResourceId",
                table: "UserTimelinePreferences",
                column: "TargetResourceId");

            migrationBuilder.CreateIndex(
                name: "IX_UserTimelinePreferences_TargetUserId",
                table: "UserTimelinePreferences",
                column: "TargetUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserTimelinePreferences");
        }
    }
}
