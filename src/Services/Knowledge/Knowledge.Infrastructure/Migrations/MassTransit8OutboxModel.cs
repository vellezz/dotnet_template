using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Knowledge.Infrastructure.Migrations
{
    /// <summary>
    /// Expand step of the move to MassTransit 8 (ADR-0035): adds the outbox indexes MassTransit 8 queries by
    /// (<c>OutboxState.Created</c>, <c>OutboxMessage.EnqueueTime</c>, <c>OutboxMessage.ExpirationTime</c>).
    /// </summary>
    /// <remarks>
    /// MassTransit 9 added the nullable column <c>OutboxState.BusName</c> with the index <c>IX_OutboxState_BusName_Created</c>.
    /// MassTransit 8 does not map them, but they stay in the database on purpose: during a rolling update, instances still running
    /// MassTransit 9 keep writing them, and MassTransit 8 can insert rows because the column is nullable. Drop both in a later,
    /// hand-written contract migration once no MassTransit 9 instance is left (the model snapshot no longer contains them).
    /// </remarks>
    public partial class MassTransit8OutboxModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_OutboxState_Created",
                schema: "knowledge",
                table: "OutboxState",
                column: "Created");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessage_EnqueueTime",
                schema: "knowledge",
                table: "OutboxMessage",
                column: "EnqueueTime");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessage_ExpirationTime",
                schema: "knowledge",
                table: "OutboxMessage",
                column: "ExpirationTime");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OutboxState_Created",
                schema: "knowledge",
                table: "OutboxState");

            migrationBuilder.DropIndex(
                name: "IX_OutboxMessage_EnqueueTime",
                schema: "knowledge",
                table: "OutboxMessage");

            migrationBuilder.DropIndex(
                name: "IX_OutboxMessage_ExpirationTime",
                schema: "knowledge",
                table: "OutboxMessage");
        }
    }
}
