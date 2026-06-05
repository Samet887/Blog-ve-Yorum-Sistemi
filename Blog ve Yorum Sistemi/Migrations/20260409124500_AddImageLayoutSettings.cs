using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Blog_ve_Yorum_Sistemi.Migrations
{
    /// <inheritdoc />
    public partial class AddImageLayoutSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ImagePlacement",
                table: "BlogPosts",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "Top");

            migrationBuilder.AddColumn<int>(
                name: "ImageWidthPercent",
                table: "BlogPosts",
                type: "int",
                nullable: false,
                defaultValue: 100);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ImagePlacement",
                table: "BlogPosts");

            migrationBuilder.DropColumn(
                name: "ImageWidthPercent",
                table: "BlogPosts");
        }
    }
}
