using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LiveService.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveTags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Tags");

            migrationBuilder.DropColumn(
                name: "TagSlugs",
                table: "Lives");

            migrationBuilder.RenameColumn(
                name: "StartTime",
                table: "Lives",
                newName: "StoppedAt");

            migrationBuilder.RenameColumn(
                name: "EndTime",
                table: "Lives",
                newName: "StartedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "StoppedAt",
                table: "Lives",
                newName: "StartTime");

            migrationBuilder.RenameColumn(
                name: "StartedAt",
                table: "Lives",
                newName: "EndTime");

            migrationBuilder.AddColumn<List<string>>(
                name: "TagSlugs",
                table: "Lives",
                type: "text[]",
                nullable: false);

            migrationBuilder.CreateTable(
                name: "Tags",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Slug = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tags", x => x.Id);
                });
        }
    }
}
