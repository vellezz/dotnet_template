using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Knowledge.Infrastructure.Migrations
{
    /// <summary>
    /// Turns <c>CollectionItems.Position</c> and <c>ContentTextSpans.Position</c> from <c>IDENTITY</c> columns into plain columns whose
    /// values are set by the domain (0..n-1 per owner).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The initial model let EF Core treat the int part of these owned collection keys as store generated, so a collection with more
    /// than one item and a block with more than one text span could not be saved (every item or span after the first was sent as an
    /// update of a non-existing row). SQL Server cannot remove the <c>IDENTITY</c> property in place, so each column is rebuilt: the
    /// primary key (and the position check) are dropped, a new column receives the existing positions renumbered 0..n-1 per owner in
    /// their current order, the old column is dropped, the new one renamed and the constraints are recreated. No row is lost.
    /// </para>
    /// <para>
    /// Expand/contract: the rebuilt columns get a default of 0 (<c>DF_CollectionItems_Position</c>, <c>DF_ContentTextSpans_Position</c>),
    /// so replicas of the previous version, which omit the position on insert, still work during a rolling update for the only case they
    /// could ever save (the first item or span). A later contract migration may drop these defaults.
    /// </para>
    /// <para>
    /// <see cref="Down"/> restores the <c>IDENTITY</c> columns; positions are then renumbered by the database, as with the initial schema.
    /// </para>
    /// </remarks>
    public partial class FixOwnedPositionKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(name: "CK_CollectionItems_Position", schema: "knowledge", table: "CollectionItems");
            RebuildWithoutIdentity(migrationBuilder, table: "CollectionItems", owner: "CollectionId");
            migrationBuilder.AddCheckConstraint(
                name: "CK_CollectionItems_Position",
                schema: "knowledge",
                table: "CollectionItems",
                sql: "[Position] >= 0");

            RebuildWithoutIdentity(migrationBuilder, table: "ContentTextSpans", owner: "BlockId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(name: "CK_CollectionItems_Position", schema: "knowledge", table: "CollectionItems");
            RebuildWithIdentity(migrationBuilder, table: "CollectionItems", owner: "CollectionId");
            migrationBuilder.AddCheckConstraint(
                name: "CK_CollectionItems_Position",
                schema: "knowledge",
                table: "CollectionItems",
                sql: "[Position] >= 0");

            RebuildWithIdentity(migrationBuilder, table: "ContentTextSpans", owner: "BlockId");
        }

        // Replaces the IDENTITY column Position of the table by a plain int column holding the positions renumbered 0..n-1 per owner.
        private static void RebuildWithoutIdentity(MigrationBuilder migrationBuilder, string table, string owner)
        {
            migrationBuilder.DropPrimaryKey(name: $"PK_{table}", schema: "knowledge", table: table);
            migrationBuilder.AddColumn<int>(name: "PositionNew", schema: "knowledge", table: table, type: "int", nullable: true);
            // EXEC defers compilation, so the idempotent script (prod, run by the DBA) does not fail on the column name when this
            // migration has already been applied and PositionNew no longer exists.
            migrationBuilder.Sql(
                $"""
                EXEC(N'WITH [Numbered] AS (
                    SELECT [PositionNew], ROW_NUMBER() OVER (PARTITION BY [{owner}] ORDER BY [Position]) - 1 AS [NewPosition]
                    FROM [knowledge].[{table}])
                UPDATE [Numbered] SET [PositionNew] = [NewPosition];');
                """);
            migrationBuilder.DropColumn(name: "Position", schema: "knowledge", table: table);
            migrationBuilder.RenameColumn(name: "PositionNew", schema: "knowledge", table: table, newName: "Position");
            migrationBuilder.AlterColumn<int>(
                name: "Position",
                schema: "knowledge",
                table: table,
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);
            migrationBuilder.Sql($"ALTER TABLE [knowledge].[{table}] ADD CONSTRAINT [DF_{table}_Position] DEFAULT 0 FOR [Position];");
            migrationBuilder.AddPrimaryKey(name: $"PK_{table}", schema: "knowledge", table: table, columns: [owner, "Position"]);
        }

        // Restores the IDENTITY column Position of the initial schema; the database assigns new position values.
        private static void RebuildWithIdentity(MigrationBuilder migrationBuilder, string table, string owner)
        {
            migrationBuilder.DropPrimaryKey(name: $"PK_{table}", schema: "knowledge", table: table);
            migrationBuilder.Sql($"ALTER TABLE [knowledge].[{table}] DROP CONSTRAINT [DF_{table}_Position];");
            migrationBuilder.DropColumn(name: "Position", schema: "knowledge", table: table);
            migrationBuilder.AddColumn<int>(name: "Position", schema: "knowledge", table: table, type: "int", nullable: false)
                .Annotation("SqlServer:Identity", "1, 1");
            migrationBuilder.AddPrimaryKey(name: $"PK_{table}", schema: "knowledge", table: table, columns: [owner, "Position"]);
        }
    }
}
