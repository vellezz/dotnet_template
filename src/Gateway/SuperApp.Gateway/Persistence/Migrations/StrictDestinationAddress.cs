using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SuperApp.Gateway.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StrictDestinationAddress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Destinations_ClusterAddress",
                schema: "gateway",
                table: "Destinations");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Destinations_ClusterAddress",
                schema: "gateway",
                table: "Destinations",
                sql: "[Address] COLLATE Latin1_General_BIN2 LIKE 'http://[a-z0-9]%.[a-z0-9]%.svc.cluster.local:[1-9]%' AND [Address] COLLATE Latin1_General_BIN2 NOT LIKE '%[^-a-z0-9.:/]%' AND [Address] NOT LIKE '%-.%' AND LEN([Address]) - LEN(REPLACE([Address], '.', '')) = 4 AND LEN([Address]) - LEN(REPLACE([Address], ':', '')) = 2 AND (LEN([Address]) - LEN(REPLACE([Address], '/', '')) = 2 OR (LEN([Address]) - LEN(REPLACE([Address], '/', '')) = 3 AND [Address] LIKE '%/')) AND ISNULL(TRY_CAST(REPLACE(SUBSTRING([Address], CHARINDEX('.svc.cluster.local:', [Address]) + 19, 500), '/', '') AS int), 0) BETWEEN 1 AND 65535");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Destinations_ClusterAddress",
                schema: "gateway",
                table: "Destinations");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Destinations_ClusterAddress",
                schema: "gateway",
                table: "Destinations",
                sql: "[Address] LIKE 'http://%.svc.cluster.local%' OR [Address] LIKE 'https://%.svc.cluster.local%'");
        }
    }
}
