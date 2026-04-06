using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarPricePredictionAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddPredictionHistoryAndMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ModelMetadatas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Algorithm = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RSquared = table.Column<float>(type: "real", nullable: false),
                    RMSE = table.Column<float>(type: "real", nullable: false),
                    TrainedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TrainingTimeMs = table.Column<long>(type: "bigint", nullable: false),
                    DatasetUsed = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModelMetadatas", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PredictionHistories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Brand = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Mileage = table.Column<float>(type: "real", nullable: false),
                    Fuel = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Transmission = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PriceSDCA = table.Column<float>(type: "real", nullable: false),
                    PriceFastTree = table.Column<float>(type: "real", nullable: false),
                    PredictedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PredictionHistories", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ModelMetadatas");

            migrationBuilder.DropTable(
                name: "PredictionHistories");
        }
    }
}
