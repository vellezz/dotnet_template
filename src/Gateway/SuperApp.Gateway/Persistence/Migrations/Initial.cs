using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace SuperApp.Gateway.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "gateway");

            migrationBuilder.CreateTable(
                name: "Clusters",
                schema: "gateway",
                columns: table => new
                {
                    Profile = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ClusterId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    LoadBalancingPolicy = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    ActivityTimeoutSeconds = table.Column<int>(type: "int", nullable: false),
                    HttpVersion = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: true),
                    HealthCheckEnabled = table.Column<bool>(type: "bit", nullable: false),
                    HealthCheckPath = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    HealthCheckIntervalSeconds = table.Column<int>(type: "int", nullable: true),
                    HealthCheckTimeoutSeconds = table.Column<int>(type: "int", nullable: true),
                    PeriodEnd = table.Column<DateTime>(type: "datetime2", nullable: false)
                        .Annotation("SqlServer:TemporalIsPeriodEndColumn", true),
                    PeriodStart = table.Column<DateTime>(type: "datetime2", nullable: false)
                        .Annotation("SqlServer:TemporalIsPeriodStartColumn", true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Clusters", x => new { x.Profile, x.ClusterId });
                    table.CheckConstraint("CK_Clusters_HealthCheck", "[HealthCheckEnabled] = 0 OR [HealthCheckPath] IS NOT NULL");
                    table.CheckConstraint("CK_Clusters_HttpVersion", "[HttpVersion] IS NULL OR [HttpVersion] IN ('1.1','2')");
                    table.CheckConstraint("CK_Clusters_LoadBalancing", "[LoadBalancingPolicy] IS NULL OR [LoadBalancingPolicy] IN ('RoundRobin','PowerOfTwoChoices','LeastRequests','Random','FirstAlphabetical')");
                    table.CheckConstraint("CK_Clusters_Profile", "[Profile] IN ('bff-web','gateway-mobile')");
                    table.CheckConstraint("CK_Clusters_Timeouts", "[ActivityTimeoutSeconds] > 0 AND ([HealthCheckIntervalSeconds] IS NULL OR [HealthCheckIntervalSeconds] > 0) AND ([HealthCheckTimeoutSeconds] IS NULL OR [HealthCheckTimeoutSeconds] > 0)");
                })
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "ClustersHistory")
                .Annotation("SqlServer:TemporalHistoryTableSchema", "gateway")
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "PeriodEnd")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "PeriodStart");

            migrationBuilder.CreateTable(
                name: "DataProtectionKeys",
                schema: "gateway",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FriendlyName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Xml = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataProtectionKeys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Sessions",
                schema: "gateway",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Value = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Subject = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SessionId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Destinations",
                schema: "gateway",
                columns: table => new
                {
                    Profile = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ClusterId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DestinationId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Address = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    PeriodEnd = table.Column<DateTime>(type: "datetime2", nullable: false)
                        .Annotation("SqlServer:TemporalIsPeriodEndColumn", true),
                    PeriodStart = table.Column<DateTime>(type: "datetime2", nullable: false)
                        .Annotation("SqlServer:TemporalIsPeriodStartColumn", true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Destinations", x => new { x.Profile, x.ClusterId, x.DestinationId });
                    table.CheckConstraint("CK_Destinations_ClusterAddress", "[Address] LIKE 'http://%.svc.cluster.local%' OR [Address] LIKE 'https://%.svc.cluster.local%'");
                    table.CheckConstraint("CK_Destinations_Profile", "[Profile] IN ('bff-web','gateway-mobile')");
                    table.ForeignKey(
                        name: "FK_Destinations_Clusters_Profile_ClusterId",
                        columns: x => new { x.Profile, x.ClusterId },
                        principalSchema: "gateway",
                        principalTable: "Clusters",
                        principalColumns: new[] { "Profile", "ClusterId" },
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "DestinationsHistory")
                .Annotation("SqlServer:TemporalHistoryTableSchema", "gateway")
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "PeriodEnd")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "PeriodStart");

            migrationBuilder.CreateTable(
                name: "Routes",
                schema: "gateway",
                columns: table => new
                {
                    Profile = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RouteId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ClusterId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    Path = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    AuthorizationPolicy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RateLimiterPolicy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    TimeoutSeconds = table.Column<int>(type: "int", nullable: true),
                    MaxRequestBodySize = table.Column<long>(type: "bigint", nullable: true),
                    PeriodEnd = table.Column<DateTime>(type: "datetime2", nullable: false)
                        .Annotation("SqlServer:TemporalIsPeriodEndColumn", true),
                    PeriodStart = table.Column<DateTime>(type: "datetime2", nullable: false)
                        .Annotation("SqlServer:TemporalIsPeriodStartColumn", true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Routes", x => new { x.Profile, x.RouteId });
                    table.CheckConstraint("CK_Routes_Limits", "([TimeoutSeconds] IS NULL OR [TimeoutSeconds] > 0) AND ([MaxRequestBodySize] IS NULL OR [MaxRequestBodySize] > 0)");
                    table.CheckConstraint("CK_Routes_Path", "[Path] LIKE '/%'");
                    table.CheckConstraint("CK_Routes_Profile", "[Profile] IN ('bff-web','gateway-mobile')");
                    table.ForeignKey(
                        name: "FK_Routes_Clusters_Profile_ClusterId",
                        columns: x => new { x.Profile, x.ClusterId },
                        principalSchema: "gateway",
                        principalTable: "Clusters",
                        principalColumns: new[] { "Profile", "ClusterId" });
                })
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "RoutesHistory")
                .Annotation("SqlServer:TemporalHistoryTableSchema", "gateway")
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "PeriodEnd")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "PeriodStart");

            migrationBuilder.CreateTable(
                name: "RouteHosts",
                schema: "gateway",
                columns: table => new
                {
                    Profile = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RouteId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Host = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    PeriodEnd = table.Column<DateTime>(type: "datetime2", nullable: false)
                        .Annotation("SqlServer:TemporalIsPeriodEndColumn", true),
                    PeriodStart = table.Column<DateTime>(type: "datetime2", nullable: false)
                        .Annotation("SqlServer:TemporalIsPeriodStartColumn", true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RouteHosts", x => new { x.Profile, x.RouteId, x.Host });
                    table.ForeignKey(
                        name: "FK_RouteHosts_Routes_Profile_RouteId",
                        columns: x => new { x.Profile, x.RouteId },
                        principalSchema: "gateway",
                        principalTable: "Routes",
                        principalColumns: new[] { "Profile", "RouteId" },
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "RouteHostsHistory")
                .Annotation("SqlServer:TemporalHistoryTableSchema", "gateway")
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "PeriodEnd")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "PeriodStart");

            migrationBuilder.CreateTable(
                name: "RouteMethods",
                schema: "gateway",
                columns: table => new
                {
                    Profile = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RouteId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Method = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    PeriodEnd = table.Column<DateTime>(type: "datetime2", nullable: false)
                        .Annotation("SqlServer:TemporalIsPeriodEndColumn", true),
                    PeriodStart = table.Column<DateTime>(type: "datetime2", nullable: false)
                        .Annotation("SqlServer:TemporalIsPeriodStartColumn", true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RouteMethods", x => new { x.Profile, x.RouteId, x.Method });
                    table.CheckConstraint("CK_RouteMethods_Method", "[Method] IN ('GET','POST','PUT','PATCH','DELETE','HEAD','OPTIONS')");
                    table.ForeignKey(
                        name: "FK_RouteMethods_Routes_Profile_RouteId",
                        columns: x => new { x.Profile, x.RouteId },
                        principalSchema: "gateway",
                        principalTable: "Routes",
                        principalColumns: new[] { "Profile", "RouteId" },
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "RouteMethodsHistory")
                .Annotation("SqlServer:TemporalHistoryTableSchema", "gateway")
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "PeriodEnd")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "PeriodStart");

            migrationBuilder.CreateTable(
                name: "RouteTransforms",
                schema: "gateway",
                columns: table => new
                {
                    Profile = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RouteId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Value = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    PeriodEnd = table.Column<DateTime>(type: "datetime2", nullable: false)
                        .Annotation("SqlServer:TemporalIsPeriodEndColumn", true),
                    PeriodStart = table.Column<DateTime>(type: "datetime2", nullable: false)
                        .Annotation("SqlServer:TemporalIsPeriodStartColumn", true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RouteTransforms", x => new { x.Profile, x.RouteId, x.Order });
                    table.CheckConstraint("CK_RouteTransforms_Fields", "([Kind] IN ('PathRemovePrefix','PathPrefix','PathPattern') AND [Value] IS NOT NULL AND [Name] IS NULL) OR ([Kind] IN ('RequestHeaderSet','ResponseHeaderSet') AND [Name] IS NOT NULL AND [Value] IS NOT NULL) OR ([Kind] IN ('RequestHeaderRemove','ResponseHeaderRemove') AND [Name] IS NOT NULL AND [Value] IS NULL)");
                    table.CheckConstraint("CK_RouteTransforms_Kind", "[Kind] IN ('PathRemovePrefix','PathPrefix','PathPattern','RequestHeaderSet','RequestHeaderRemove','ResponseHeaderSet','ResponseHeaderRemove')");
                    table.ForeignKey(
                        name: "FK_RouteTransforms_Routes_Profile_RouteId",
                        columns: x => new { x.Profile, x.RouteId },
                        principalSchema: "gateway",
                        principalTable: "Routes",
                        principalColumns: new[] { "Profile", "RouteId" },
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "RouteTransformsHistory")
                .Annotation("SqlServer:TemporalHistoryTableSchema", "gateway")
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "PeriodEnd")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "PeriodStart");

            migrationBuilder.InsertData(
                schema: "gateway",
                table: "Clusters",
                columns: new[] { "ClusterId", "Profile", "ActivityTimeoutSeconds", "HealthCheckEnabled", "HealthCheckIntervalSeconds", "HealthCheckPath", "HealthCheckTimeoutSeconds", "HttpVersion", "LoadBalancingPolicy" },
                values: new object[,]
                {
                    { "knowledge", "bff-web", 30, false, null, null, null, "1.1", "RoundRobin" },
                    { "sleepdiary", "bff-web", 30, false, null, null, null, "1.1", "RoundRobin" },
                    { "knowledge", "gateway-mobile", 30, false, null, null, null, "1.1", "RoundRobin" },
                    { "sleepdiary", "gateway-mobile", 30, false, null, null, null, "1.1", "RoundRobin" }
                });

            migrationBuilder.InsertData(
                schema: "gateway",
                table: "Destinations",
                columns: new[] { "ClusterId", "DestinationId", "Profile", "Address" },
                values: new object[,]
                {
                    { "knowledge", "knowledge-api", "bff-web", "http://knowledge-api.knowledge.svc.cluster.local:8080" },
                    { "sleepdiary", "sleepdiary-api", "bff-web", "http://sleepdiary-api.sleepdiary.svc.cluster.local:8080" },
                    { "knowledge", "knowledge-api", "gateway-mobile", "http://knowledge-api.knowledge.svc.cluster.local:8080" },
                    { "sleepdiary", "sleepdiary-api", "gateway-mobile", "http://sleepdiary-api.sleepdiary.svc.cluster.local:8080" }
                });

            migrationBuilder.InsertData(
                schema: "gateway",
                table: "Routes",
                columns: new[] { "Profile", "RouteId", "AuthorizationPolicy", "ClusterId", "MaxRequestBodySize", "Order", "Path", "RateLimiterPolicy", "TimeoutSeconds" },
                values: new object[,]
                {
                    { "bff-web", "knowledge-api", "knowledge", "knowledge", null, 100, "/api/knowledge/{**rest}", "per-user", 30 },
                    { "bff-web", "sleepdiary-api", "sleepdiary", "sleepdiary", null, 100, "/api/sleepdiary/{**rest}", "per-user", 30 },
                    { "gateway-mobile", "knowledge-api", "knowledge", "knowledge", null, 100, "/api/knowledge/{**rest}", "per-user", 30 },
                    { "gateway-mobile", "sleepdiary-api", "sleepdiary", "sleepdiary", null, 100, "/api/sleepdiary/{**rest}", "per-user", 30 }
                });

            migrationBuilder.InsertData(
                schema: "gateway",
                table: "RouteTransforms",
                columns: new[] { "Order", "Profile", "RouteId", "Kind", "Name", "Value" },
                values: new object[,]
                {
                    { 0, "bff-web", "knowledge-api", "PathRemovePrefix", null, "/api/knowledge" },
                    { 0, "bff-web", "sleepdiary-api", "PathRemovePrefix", null, "/api/sleepdiary" },
                    { 0, "gateway-mobile", "knowledge-api", "PathRemovePrefix", null, "/api/knowledge" },
                    { 0, "gateway-mobile", "sleepdiary-api", "PathRemovePrefix", null, "/api/sleepdiary" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Routes_Profile_ClusterId",
                schema: "gateway",
                table: "Routes",
                columns: new[] { "Profile", "ClusterId" });

            migrationBuilder.CreateIndex(
                name: "IX_Routes_Profile_Path_Order",
                schema: "gateway",
                table: "Routes",
                columns: new[] { "Profile", "Path", "Order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_ExpiresAt",
                schema: "gateway",
                table: "Sessions",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_SessionId",
                schema: "gateway",
                table: "Sessions",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_Subject",
                schema: "gateway",
                table: "Sessions",
                column: "Subject");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DataProtectionKeys",
                schema: "gateway");

            migrationBuilder.DropTable(
                name: "Destinations",
                schema: "gateway")
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "DestinationsHistory")
                .Annotation("SqlServer:TemporalHistoryTableSchema", "gateway")
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "PeriodEnd")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "PeriodStart");

            migrationBuilder.DropTable(
                name: "RouteHosts",
                schema: "gateway")
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "RouteHostsHistory")
                .Annotation("SqlServer:TemporalHistoryTableSchema", "gateway")
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "PeriodEnd")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "PeriodStart");

            migrationBuilder.DropTable(
                name: "RouteMethods",
                schema: "gateway")
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "RouteMethodsHistory")
                .Annotation("SqlServer:TemporalHistoryTableSchema", "gateway")
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "PeriodEnd")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "PeriodStart");

            migrationBuilder.DropTable(
                name: "RouteTransforms",
                schema: "gateway")
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "RouteTransformsHistory")
                .Annotation("SqlServer:TemporalHistoryTableSchema", "gateway")
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "PeriodEnd")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "PeriodStart");

            migrationBuilder.DropTable(
                name: "Sessions",
                schema: "gateway");

            migrationBuilder.DropTable(
                name: "Routes",
                schema: "gateway")
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "RoutesHistory")
                .Annotation("SqlServer:TemporalHistoryTableSchema", "gateway")
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "PeriodEnd")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "PeriodStart");

            migrationBuilder.DropTable(
                name: "Clusters",
                schema: "gateway")
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "ClustersHistory")
                .Annotation("SqlServer:TemporalHistoryTableSchema", "gateway")
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "PeriodEnd")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "PeriodStart");
        }
    }
}
