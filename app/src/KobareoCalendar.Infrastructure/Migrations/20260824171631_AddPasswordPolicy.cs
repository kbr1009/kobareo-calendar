using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KobareoCalendar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPasswordPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PasswordPolicies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RequiredLength = table.Column<int>(type: "INTEGER", nullable: false),
                    RequireDigit = table.Column<bool>(type: "INTEGER", nullable: false),
                    RequireLowercase = table.Column<bool>(type: "INTEGER", nullable: false),
                    RequireUppercase = table.Column<bool>(type: "INTEGER", nullable: false),
                    RequireNonAlphanumeric = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PasswordPolicies", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PasswordPolicies");
        }
    }
}
