using Knowledge.Application.Content.Blocks;

namespace Knowledge.Api.Controllers;

/// <summary>Request body of <c>PUT /v1/materials/{materialId}/content</c>: the complete new block content of a material.</summary>
/// <param name="Blocks">
/// Top-level blocks in display order, each with its child blocks (a tree whose nodes are distinguished by the <c>type</c> field).
/// An empty list removes the content, which is not allowed for a published material. At most 500 blocks in total, children included.
/// </param>
public sealed record ReplaceContentRequest(IReadOnlyList<ContentBlockDto> Blocks);
