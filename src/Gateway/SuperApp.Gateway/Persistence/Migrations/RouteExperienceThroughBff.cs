using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace SuperApp.Gateway.Persistence.Migrations
{
    /// <summary>
    /// Routing data only (no schema change): replaces the routes of the domain services (<c>/api/knowledge/**</c>, <c>/api/sleepdiary/**</c>)
    /// with one route of the Example experience to its BFF, <c>/api/example/v{version:int}/**</c> (ADR-0038, ADR-0039).
    /// </summary>
    /// <remarks>
    /// Not an expand/contract change: the old routes go away in the same release, because the module talks only to the BFF from now on.
    /// A replica of the previous gateway version that reloads this configuration rejects it (it does not know the <c>example</c> policy) and
    /// keeps its last valid configuration until it is replaced by the rollout. Deploy the BFF (<c>example-bff</c>) before applying this
    /// migration, otherwise the experience is unreachable.
    /// </remarks>
    public partial class RouteExperienceThroughBff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "gateway",
                table: "Destinations",
                keyColumns: new[] { "ClusterId", "DestinationId", "Profile" },
                keyValues: new object[] { "knowledge", "knowledge-api", "bff-web" });

            migrationBuilder.DeleteData(
                schema: "gateway",
                table: "Destinations",
                keyColumns: new[] { "ClusterId", "DestinationId", "Profile" },
                keyValues: new object[] { "sleepdiary", "sleepdiary-api", "bff-web" });

            migrationBuilder.DeleteData(
                schema: "gateway",
                table: "Destinations",
                keyColumns: new[] { "ClusterId", "DestinationId", "Profile" },
                keyValues: new object[] { "knowledge", "knowledge-api", "gateway-mobile" });

            migrationBuilder.DeleteData(
                schema: "gateway",
                table: "Destinations",
                keyColumns: new[] { "ClusterId", "DestinationId", "Profile" },
                keyValues: new object[] { "sleepdiary", "sleepdiary-api", "gateway-mobile" });

            migrationBuilder.DeleteData(
                schema: "gateway",
                table: "RouteTransforms",
                keyColumns: new[] { "Order", "Profile", "RouteId" },
                keyValues: new object[] { 0, "bff-web", "knowledge-api" });

            migrationBuilder.DeleteData(
                schema: "gateway",
                table: "RouteTransforms",
                keyColumns: new[] { "Order", "Profile", "RouteId" },
                keyValues: new object[] { 0, "bff-web", "sleepdiary-api" });

            migrationBuilder.DeleteData(
                schema: "gateway",
                table: "RouteTransforms",
                keyColumns: new[] { "Order", "Profile", "RouteId" },
                keyValues: new object[] { 0, "gateway-mobile", "knowledge-api" });

            migrationBuilder.DeleteData(
                schema: "gateway",
                table: "RouteTransforms",
                keyColumns: new[] { "Order", "Profile", "RouteId" },
                keyValues: new object[] { 0, "gateway-mobile", "sleepdiary-api" });

            migrationBuilder.DeleteData(
                schema: "gateway",
                table: "Routes",
                keyColumns: new[] { "Profile", "RouteId" },
                keyValues: new object[] { "bff-web", "knowledge-api" });

            migrationBuilder.DeleteData(
                schema: "gateway",
                table: "Routes",
                keyColumns: new[] { "Profile", "RouteId" },
                keyValues: new object[] { "bff-web", "sleepdiary-api" });

            migrationBuilder.DeleteData(
                schema: "gateway",
                table: "Routes",
                keyColumns: new[] { "Profile", "RouteId" },
                keyValues: new object[] { "gateway-mobile", "knowledge-api" });

            migrationBuilder.DeleteData(
                schema: "gateway",
                table: "Routes",
                keyColumns: new[] { "Profile", "RouteId" },
                keyValues: new object[] { "gateway-mobile", "sleepdiary-api" });

            migrationBuilder.DeleteData(
                schema: "gateway",
                table: "Clusters",
                keyColumns: new[] { "ClusterId", "Profile" },
                keyValues: new object[] { "knowledge", "bff-web" });

            migrationBuilder.DeleteData(
                schema: "gateway",
                table: "Clusters",
                keyColumns: new[] { "ClusterId", "Profile" },
                keyValues: new object[] { "sleepdiary", "bff-web" });

            migrationBuilder.DeleteData(
                schema: "gateway",
                table: "Clusters",
                keyColumns: new[] { "ClusterId", "Profile" },
                keyValues: new object[] { "knowledge", "gateway-mobile" });

            migrationBuilder.DeleteData(
                schema: "gateway",
                table: "Clusters",
                keyColumns: new[] { "ClusterId", "Profile" },
                keyValues: new object[] { "sleepdiary", "gateway-mobile" });

            migrationBuilder.InsertData(
                schema: "gateway",
                table: "Clusters",
                columns: new[] { "ClusterId", "Profile", "ActivityTimeoutSeconds", "HealthCheckEnabled", "HealthCheckIntervalSeconds", "HealthCheckPath", "HealthCheckTimeoutSeconds", "HttpVersion", "LoadBalancingPolicy" },
                values: new object[,]
                {
                    { "example", "bff-web", 30, false, null, null, null, "1.1", "RoundRobin" },
                    { "example", "gateway-mobile", 30, false, null, null, null, "1.1", "RoundRobin" }
                });

            migrationBuilder.InsertData(
                schema: "gateway",
                table: "Destinations",
                columns: new[] { "ClusterId", "DestinationId", "Profile", "Address" },
                values: new object[,]
                {
                    { "example", "example-bff", "bff-web", "http://example-bff.example.svc.cluster.local:8080" },
                    { "example", "example-bff", "gateway-mobile", "http://example-bff.example.svc.cluster.local:8080" }
                });

            migrationBuilder.InsertData(
                schema: "gateway",
                table: "Routes",
                columns: new[] { "Profile", "RouteId", "AuthorizationPolicy", "ClusterId", "MaxRequestBodySize", "Order", "Path", "RateLimiterPolicy", "TimeoutSeconds" },
                values: new object[,]
                {
                    { "bff-web", "example-bff", "example", "example", null, 100, "/api/example/v{version:int}/{**rest}", "per-user", 30 },
                    { "gateway-mobile", "example-bff", "example", "example", null, 100, "/api/example/v{version:int}/{**rest}", "per-user", 30 }
                });

            migrationBuilder.InsertData(
                schema: "gateway",
                table: "RouteTransforms",
                columns: new[] { "Order", "Profile", "RouteId", "Kind", "Name", "Value" },
                values: new object[,]
                {
                    { 0, "bff-web", "example-bff", "PathRemovePrefix", null, "/api/example" },
                    { 0, "gateway-mobile", "example-bff", "PathRemovePrefix", null, "/api/example" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "gateway",
                table: "Destinations",
                keyColumns: new[] { "ClusterId", "DestinationId", "Profile" },
                keyValues: new object[] { "example", "example-bff", "bff-web" });

            migrationBuilder.DeleteData(
                schema: "gateway",
                table: "Destinations",
                keyColumns: new[] { "ClusterId", "DestinationId", "Profile" },
                keyValues: new object[] { "example", "example-bff", "gateway-mobile" });

            migrationBuilder.DeleteData(
                schema: "gateway",
                table: "RouteTransforms",
                keyColumns: new[] { "Order", "Profile", "RouteId" },
                keyValues: new object[] { 0, "bff-web", "example-bff" });

            migrationBuilder.DeleteData(
                schema: "gateway",
                table: "RouteTransforms",
                keyColumns: new[] { "Order", "Profile", "RouteId" },
                keyValues: new object[] { 0, "gateway-mobile", "example-bff" });

            migrationBuilder.DeleteData(
                schema: "gateway",
                table: "Routes",
                keyColumns: new[] { "Profile", "RouteId" },
                keyValues: new object[] { "bff-web", "example-bff" });

            migrationBuilder.DeleteData(
                schema: "gateway",
                table: "Routes",
                keyColumns: new[] { "Profile", "RouteId" },
                keyValues: new object[] { "gateway-mobile", "example-bff" });

            migrationBuilder.DeleteData(
                schema: "gateway",
                table: "Clusters",
                keyColumns: new[] { "ClusterId", "Profile" },
                keyValues: new object[] { "example", "bff-web" });

            migrationBuilder.DeleteData(
                schema: "gateway",
                table: "Clusters",
                keyColumns: new[] { "ClusterId", "Profile" },
                keyValues: new object[] { "example", "gateway-mobile" });

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
        }
    }
}
