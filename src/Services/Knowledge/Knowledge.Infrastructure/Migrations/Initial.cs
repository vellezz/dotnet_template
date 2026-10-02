using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Knowledge.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "knowledge");

            migrationBuilder.CreateTable(
                name: "Categories",
                schema: "knowledge",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Categories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Collections",
                schema: "knowledge",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Collections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Favorites",
                schema: "knowledge",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ItemType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AddedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Favorites", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InboxState",
                schema: "knowledge",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConsumerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LockId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Received = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReceiveCount = table.Column<int>(type: "int", nullable: false),
                    ExpirationTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Consumed = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Delivered = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastSequenceNumber = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InboxState", x => x.Id);
                    table.UniqueConstraint("AK_InboxState_MessageId_ConsumerId", x => new { x.MessageId, x.ConsumerId });
                });

            migrationBuilder.CreateTable(
                name: "MaterialCompletions",
                schema: "knowledge",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    MaterialId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaterialCompletions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Materials",
                schema: "knowledge",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    MainMediaUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    MainMediaDurationSeconds = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ContentPlainText = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReadingTimeMinutes = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Materials", x => x.Id);
                    table.CheckConstraint("CK_Materials_MainMediaUrl", "[MainMediaUrl] IS NULL OR [MainMediaUrl] LIKE 'https://%'");
                    table.CheckConstraint("CK_Materials_ReadingTime", "[ReadingTimeMinutes] >= 0");
                });

            migrationBuilder.CreateTable(
                name: "OutboxState",
                schema: "knowledge",
                columns: table => new
                {
                    OutboxId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LockId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Created = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Delivered = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastSequenceNumber = table.Column<long>(type: "bigint", nullable: true),
                    BusName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxState", x => x.OutboxId);
                });

            migrationBuilder.CreateTable(
                name: "CollectionCategories",
                schema: "knowledge",
                columns: table => new
                {
                    CategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CollectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CollectionCategories", x => new { x.CollectionId, x.CategoryId });
                    table.ForeignKey(
                        name: "FK_CollectionCategories_Collections_CollectionId",
                        column: x => x.CollectionId,
                        principalSchema: "knowledge",
                        principalTable: "Collections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CollectionItems",
                schema: "knowledge",
                columns: table => new
                {
                    Position = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CollectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MaterialId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CollectionItems", x => new { x.CollectionId, x.Position });
                    table.CheckConstraint("CK_CollectionItems_Position", "[Position] >= 0");
                    table.ForeignKey(
                        name: "FK_CollectionItems_Collections_CollectionId",
                        column: x => x.CollectionId,
                        principalSchema: "knowledge",
                        principalTable: "Collections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ContentBlocks",
                schema: "knowledge",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParentBlockId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Position = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Level = table.Column<int>(type: "int", nullable: true),
                    Variant = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PlainText = table.Column<string>(type: "nvarchar(max)", maxLength: 20000, nullable: true),
                    Url = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    AltText = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Caption = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Credit = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Language = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    DurationSeconds = table.Column<int>(type: "int", nullable: true),
                    AtSeconds = table.Column<int>(type: "int", nullable: true),
                    Speaker = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsChecked = table.Column<bool>(type: "bit", nullable: true),
                    HasHeaderRow = table.Column<bool>(type: "bit", nullable: true),
                    Author = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Source = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    MaterialId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentBlocks", x => x.Id);
                    table.CheckConstraint("CK_ContentBlocks_AtSeconds", "[Type] NOT IN ('Timestamp','TranscriptSegment') OR ([AtSeconds] IS NOT NULL AND [AtSeconds] >= 0)");
                    table.CheckConstraint("CK_ContentBlocks_Callout", "[Type] <> 'Callout' OR ([Variant] IS NOT NULL AND [Variant] IN ('Info','Tip','Warning','Important'))");
                    table.CheckConstraint("CK_ContentBlocks_ChecklistItem", "[Type] <> 'ChecklistItem' OR [IsChecked] IS NOT NULL");
                    table.CheckConstraint("CK_ContentBlocks_ChildHasParent", "[Type] NOT IN ('ListItem','ChecklistItem','TakeawayItem','TableRow','TableCell','TranscriptSegment') OR [ParentBlockId] IS NOT NULL");
                    table.CheckConstraint("CK_ContentBlocks_Code", "[Type] <> 'Code' OR [PlainText] IS NOT NULL");
                    table.CheckConstraint("CK_ContentBlocks_Duration", "[DurationSeconds] IS NULL OR [DurationSeconds] >= 0");
                    table.CheckConstraint("CK_ContentBlocks_Heading", "[Type] <> 'Heading' OR ([Level] IS NOT NULL AND [Level] BETWEEN 1 AND 4 AND [PlainText] IS NOT NULL)");
                    table.CheckConstraint("CK_ContentBlocks_Image", "[Type] <> 'Image' OR ([Url] IS NOT NULL AND [AltText] IS NOT NULL)");
                    table.CheckConstraint("CK_ContentBlocks_List", "[Type] <> 'List' OR ([Variant] IS NOT NULL AND [Variant] IN ('Ordered','Unordered'))");
                    table.CheckConstraint("CK_ContentBlocks_MediaUrl", "[Type] NOT IN ('Video','Audio','Embed','LinkCard') OR [Url] IS NOT NULL");
                    table.CheckConstraint("CK_ContentBlocks_Position", "[Position] >= 0");
                    table.CheckConstraint("CK_ContentBlocks_Title", "[Type] NOT IN ('Toggle','LinkCard') OR [Title] IS NOT NULL");
                    table.CheckConstraint("CK_ContentBlocks_UrlHttps", "[Url] IS NULL OR [Url] LIKE 'https://%'");
                    table.ForeignKey(
                        name: "FK_ContentBlocks_ContentBlocks_ParentBlockId",
                        column: x => x.ParentBlockId,
                        principalSchema: "knowledge",
                        principalTable: "ContentBlocks",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ContentBlocks_Materials_MaterialId",
                        column: x => x.MaterialId,
                        principalSchema: "knowledge",
                        principalTable: "Materials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MaterialCategories",
                schema: "knowledge",
                columns: table => new
                {
                    CategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MaterialId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaterialCategories", x => new { x.MaterialId, x.CategoryId });
                    table.ForeignKey(
                        name: "FK_MaterialCategories_Materials_MaterialId",
                        column: x => x.MaterialId,
                        principalSchema: "knowledge",
                        principalTable: "Materials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OutboxMessage",
                schema: "knowledge",
                columns: table => new
                {
                    SequenceNumber = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EnqueueTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SentTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Headers = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Properties = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    InboxMessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    InboxConsumerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OutboxId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    MessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    MessageType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Body = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    InitiatorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SourceAddress = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    DestinationAddress = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ResponseAddress = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    FaultAddress = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ExpirationTime = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessage", x => x.SequenceNumber);
                    table.ForeignKey(
                        name: "FK_OutboxMessage_InboxState_InboxMessageId_InboxConsumerId",
                        columns: x => new { x.InboxMessageId, x.InboxConsumerId },
                        principalSchema: "knowledge",
                        principalTable: "InboxState",
                        principalColumns: new[] { "MessageId", "ConsumerId" });
                    table.ForeignKey(
                        name: "FK_OutboxMessage_OutboxState_OutboxId",
                        column: x => x.OutboxId,
                        principalSchema: "knowledge",
                        principalTable: "OutboxState",
                        principalColumn: "OutboxId");
                });

            migrationBuilder.CreateTable(
                name: "ContentTextSpans",
                schema: "knowledge",
                columns: table => new
                {
                    Position = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BlockId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", maxLength: 5000, nullable: false),
                    Marks = table.Column<int>(type: "int", nullable: false),
                    LinkHref = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentTextSpans", x => new { x.BlockId, x.Position });
                    table.CheckConstraint("CK_ContentTextSpans_Link", "[LinkHref] IS NULL OR [LinkHref] LIKE 'https://%' OR [LinkHref] LIKE 'mailto:%'");
                    table.ForeignKey(
                        name: "FK_ContentTextSpans_ContentBlocks_BlockId",
                        column: x => x.BlockId,
                        principalSchema: "knowledge",
                        principalTable: "ContentBlocks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Categories_Slug",
                schema: "knowledge",
                table: "Categories",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CollectionCategories_CategoryId",
                schema: "knowledge",
                table: "CollectionCategories",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_CollectionItems_MaterialId",
                schema: "knowledge",
                table: "CollectionItems",
                column: "MaterialId");

            migrationBuilder.CreateIndex(
                name: "IX_Collections_Status_PublishedAt",
                schema: "knowledge",
                table: "Collections",
                columns: new[] { "Status", "PublishedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ContentBlocks_MaterialId_ParentBlockId_Position",
                schema: "knowledge",
                table: "ContentBlocks",
                columns: new[] { "MaterialId", "ParentBlockId", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_ContentBlocks_ParentBlockId",
                schema: "knowledge",
                table: "ContentBlocks",
                column: "ParentBlockId");

            migrationBuilder.CreateIndex(
                name: "IX_Favorites_ItemType_ItemId",
                schema: "knowledge",
                table: "Favorites",
                columns: new[] { "ItemType", "ItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_Favorites_UserId_ItemType_ItemId",
                schema: "knowledge",
                table: "Favorites",
                columns: new[] { "UserId", "ItemType", "ItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InboxState_Delivered",
                schema: "knowledge",
                table: "InboxState",
                column: "Delivered");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialCategories_CategoryId",
                schema: "knowledge",
                table: "MaterialCategories",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialCompletions_UserId_MaterialId",
                schema: "knowledge",
                table: "MaterialCompletions",
                columns: new[] { "UserId", "MaterialId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Materials_Status_PublishedAt",
                schema: "knowledge",
                table: "Materials",
                columns: new[] { "Status", "PublishedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessage_InboxMessageId_InboxConsumerId_SequenceNumber",
                schema: "knowledge",
                table: "OutboxMessage",
                columns: new[] { "InboxMessageId", "InboxConsumerId", "SequenceNumber" },
                unique: true,
                filter: "[InboxMessageId] IS NOT NULL AND [InboxConsumerId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessage_OutboxId_SequenceNumber",
                schema: "knowledge",
                table: "OutboxMessage",
                columns: new[] { "OutboxId", "SequenceNumber" },
                unique: true,
                filter: "[OutboxId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxState_BusName_Created",
                schema: "knowledge",
                table: "OutboxState",
                columns: new[] { "BusName", "Created" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Categories",
                schema: "knowledge");

            migrationBuilder.DropTable(
                name: "CollectionCategories",
                schema: "knowledge");

            migrationBuilder.DropTable(
                name: "CollectionItems",
                schema: "knowledge");

            migrationBuilder.DropTable(
                name: "ContentTextSpans",
                schema: "knowledge");

            migrationBuilder.DropTable(
                name: "Favorites",
                schema: "knowledge");

            migrationBuilder.DropTable(
                name: "MaterialCategories",
                schema: "knowledge");

            migrationBuilder.DropTable(
                name: "MaterialCompletions",
                schema: "knowledge");

            migrationBuilder.DropTable(
                name: "OutboxMessage",
                schema: "knowledge");

            migrationBuilder.DropTable(
                name: "Collections",
                schema: "knowledge");

            migrationBuilder.DropTable(
                name: "ContentBlocks",
                schema: "knowledge");

            migrationBuilder.DropTable(
                name: "InboxState",
                schema: "knowledge");

            migrationBuilder.DropTable(
                name: "OutboxState",
                schema: "knowledge");

            migrationBuilder.DropTable(
                name: "Materials",
                schema: "knowledge");
        }
    }
}
