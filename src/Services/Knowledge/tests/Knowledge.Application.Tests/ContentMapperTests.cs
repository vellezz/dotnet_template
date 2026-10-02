using Knowledge.Application.Content.Blocks;
using System.Text.Json;
using Knowledge.Application.Content;
using Knowledge.Domain.Materials.Content;

namespace Knowledge.Application.Tests;

public sealed class ContentMapperTests
{
    private static readonly SpanDto[] Text = [new("Tekst", [TextMarks.Bold, TextMarks.Italic], "https://example.com")];

    [Fact]
    public void Content_survives_round_trip_through_block_tree()
    {
        IReadOnlyList<ContentBlockDto> content =
        [
            new HeadingBlockDto(2, "Nagłówek"),
            new ListBlockDto(ListStyle.Ordered, [new ListItemDto(Text, new ListBlockDto(ListStyle.Unordered, [new ListItemDto(Text)]))]),
            new CalloutBlockDto(CalloutVariant.Tip, [new ParagraphBlockDto(Text)], "Wskazówka"),
            new TableBlockDto(true, [new TableRowDto([new TableCellDto(Text), new TableCellDto([])])]),
            new GalleryBlockDto([new ImageBlockDto("https://cdn.example.com/1.png", "Pierwszy"), new ImageBlockDto("https://cdn.example.com/2.png", "Drugi")]),
            new TranscriptBlockDto([new TranscriptSegmentDto(15, Text, "Prowadzący")]),
            new DividerBlockDto(),
        ];

        var specs = ContentMapper.ToSpecs(content);
        var built = ContentBuilder.Build(specs);
        var roundTrip = ContentMapper.ToDtos(specs);

        Assert.True(built.IsSuccess, built.IsFailure ? built.Error.Message : null);
        Assert.Equal(JsonSerializer.Serialize(content), JsonSerializer.Serialize(roundTrip));
    }
}
