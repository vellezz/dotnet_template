using Knowledge.Domain.Materials.Content;
using SuperApp.Framework.Testing;

namespace Knowledge.Domain.Tests;

public sealed class ContentBuilderTests
{
    private static readonly SpanSpec[] Text = [new("Treść")];

    [Fact]
    public void Flattens_nested_tree_with_parent_links_and_positions()
    {
        var result = ContentBuilder.Build(
        [
            new BlockSpec(BlockType.Heading, Level: 2, PlainText: "Wstęp"),
            new BlockSpec(BlockType.List, Variant: "Ordered", Children:
            [
                new BlockSpec(BlockType.ListItem, Text: Text, Children:
                [
                    new BlockSpec(BlockType.List, Variant: "Unordered", Children: [new BlockSpec(BlockType.ListItem, Text: Text)]),
                ]),
            ]),
        ]);

        Assert.True(result.IsSuccess);
        var blocks = ResultAssert.Success(result).Blocks;
        Assert.Equal(5, blocks.Count);
        Assert.Null(blocks[0].ParentBlockId);
        Assert.Equal(1, blocks[1].Position);
        Assert.Equal(blocks[1].Id, blocks[2].ParentBlockId);
        Assert.Equal(blocks[3].Id, blocks[4].ParentBlockId);
    }

    [Fact]
    public void Rejects_block_in_wrong_place_with_path()
    {
        var result = ContentBuilder.Build([new BlockSpec(BlockType.ListItem, Text: Text)]);

        Assert.True(result.IsFailure);
        Assert.Equal("knowledge.content.invalid_block", result.Error.Code);
        Assert.StartsWith("blocks[0]", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_list_nesting_deeper_than_three_levels()
    {
        static BlockSpec List(BlockSpec? nested) =>
            new(BlockType.List, Variant: "Unordered", Children:
                [new BlockSpec(BlockType.ListItem, Text: Text, Children: nested is null ? null : [nested])]);

        var result = ContentBuilder.Build([List(List(List(List(null))))]);

        Assert.True(result.IsFailure);
        Assert.Contains("3 poziomach", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Requires_alt_text_for_images()
    {
        var result = ContentBuilder.Build([new BlockSpec(BlockType.Image, Url: "https://cdn.example.com/a.png")]);

        Assert.True(result.IsFailure);
        Assert.Contains("tekstu alternatywnego", result.Error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("http://cdn.example.com/a.png")]
    [InlineData("javascript:alert(1)")]
    public void Rejects_non_https_urls(string url)
    {
        var result = ContentBuilder.Build([new BlockSpec(BlockType.Image, Url: url, AltText: "Opis")]);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Allows_embeds_only_from_allowed_providers()
    {
        var allowed = ContentBuilder.Build([new BlockSpec(BlockType.Embed, Url: "https://www.youtube.com/embed/abc")]);
        var rejected = ContentBuilder.Build([new BlockSpec(BlockType.Embed, Url: "https://evil.example.com/embed")]);

        Assert.True(allowed.IsSuccess);
        Assert.True(rejected.IsFailure);
    }

    [Fact]
    public void Rejects_fields_not_applicable_to_block_type()
    {
        var result = ContentBuilder.Build([new BlockSpec(BlockType.Divider, Url: "https://example.com")]);

        Assert.True(result.IsFailure);
        Assert.Contains("nie dotyczą", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Requires_equal_cell_count_in_table_rows()
    {
        static BlockSpec Row(int cells) =>
            new(BlockType.TableRow, Children: Enumerable.Range(0, cells).Select(_ => new BlockSpec(BlockType.TableCell, Text: Text)).ToList());

        var result = ContentBuilder.Build([new BlockSpec(BlockType.Table, HasHeaderRow: true, Children: [Row(2), Row(3)])]);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Rejects_links_with_unsafe_scheme()
    {
        var result = ContentBuilder.Build([new BlockSpec(BlockType.Paragraph, Text: [new SpanSpec("klik", TextMarks.Bold, "javascript:alert(1)")])]);

        Assert.True(result.IsFailure);
    }

    /// <summary>
    /// Only the exact member names are accepted: numeric strings, other casing, padded names and comma-separated lists would pass
    /// <c>Enum.TryParse</c> and then violate the <c>CHECK</c> constraints of the database.
    /// </summary>
    [Theory]
    [InlineData("0")]
    [InlineData("7")]
    [InlineData("ordered")]
    [InlineData(" Ordered")]
    [InlineData("Ordered, Unordered")]
    [InlineData("")]
    public void Rejects_list_style_that_is_not_an_exact_member_name(string variant)
    {
        var result = ContentBuilder.Build([new BlockSpec(BlockType.List, Variant: variant, Children: [new BlockSpec(BlockType.ListItem, Text: Text)])]);

        Assert.True(result.IsFailure);
        Assert.Contains("styl listy", result.Error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("42")]
    [InlineData("warning")]
    [InlineData("Info,Tip")]
    public void Rejects_callout_variant_that_is_not_an_exact_member_name(string variant)
    {
        var result = ContentBuilder.Build([new BlockSpec(BlockType.Callout, Variant: variant, Children: [new BlockSpec(BlockType.Paragraph, Text: Text)])]);

        Assert.True(result.IsFailure);
        Assert.Contains("wariant wyróżnienia", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Accepts_every_defined_list_style_and_callout_variant()
    {
        var lists = Enum.GetNames<ListStyle>()
            .Select(name => new BlockSpec(BlockType.List, Variant: name, Children: [new BlockSpec(BlockType.ListItem, Text: Text)]));
        var callouts = Enum.GetNames<CalloutVariant>()
            .Select(name => new BlockSpec(BlockType.Callout, Variant: name, Children: [new BlockSpec(BlockType.Paragraph, Text: Text)]));

        var result = ContentBuilder.Build([.. lists, .. callouts]);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Rejects_children_of_paragraph_as_foreign_field()
    {
        var result = ContentBuilder.Build([new BlockSpec(BlockType.Paragraph, Text: Text, Children: [new BlockSpec(BlockType.Paragraph, Text: Text)])]);

        Assert.True(result.IsFailure);
        Assert.StartsWith("blocks[0]: pola Children nie dotyczą bloku Paragraph", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_children_of_takeaway_item_and_table_cell_as_foreign_field()
    {
        var takeaways = ContentBuilder.Build(
        [
            new BlockSpec(BlockType.KeyTakeaways, Children:
                [new BlockSpec(BlockType.TakeawayItem, Text: Text, Children: [new BlockSpec(BlockType.Paragraph, Text: Text)])]),
        ]);
        var table = ContentBuilder.Build(
        [
            new BlockSpec(BlockType.Table, Children:
            [
                new BlockSpec(BlockType.TableRow, Children:
                    [new BlockSpec(BlockType.TableCell, Text: Text, Children: [new BlockSpec(BlockType.Paragraph, Text: Text)])]),
            ]),
        ]);

        Assert.Contains("pola Children nie dotyczą bloku TakeawayItem", ResultAssert.Failure(takeaways).Message, StringComparison.Ordinal);
        Assert.Contains("pola Children nie dotyczą bloku TableCell", ResultAssert.Failure(table).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_text_marks_with_undefined_bits()
    {
        var result = ContentBuilder.Build([new BlockSpec(BlockType.Paragraph, Text: [new SpanSpec("Treść", TextMarks.Bold | (TextMarks)64)])]);

        Assert.True(result.IsFailure);
        Assert.Contains("nieznane oznaczenia", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Accepts_combination_of_all_defined_text_marks()
    {
        var all = Enum.GetValues<TextMarks>().Aggregate(TextMarks.None, (marks, mark) => marks | mark);

        var result = ContentBuilder.Build([new BlockSpec(BlockType.Paragraph, Text: [new SpanSpec("Treść", all)])]);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Computes_reading_time_and_plain_text()
    {
        var words = string.Join(' ', Enumerable.Repeat("słowo", 450));

        var result = ResultAssert.Success(ContentBuilder.Build([new BlockSpec(BlockType.Paragraph, Text: [new SpanSpec(words)])]));

        Assert.Equal(3, result.ReadingTimeMinutes);
        Assert.Equal(words, result.PlainText);
    }
}
