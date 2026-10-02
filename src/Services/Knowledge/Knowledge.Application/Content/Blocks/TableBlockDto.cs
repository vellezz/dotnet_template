namespace Knowledge.Application.Content.Blocks;

/// <summary>
/// Table block (discriminator <c>"type": "table"</c>): a simple grid of cells containing formatted text, without merged cells.
/// </summary>
/// <remarks>
/// <para>
/// JSON shape: <c>{ "type": "table", "hasHeaderRow": true, "rows": [ { "cells": [ { "text": [ ...spans... ] } ] } ] }</c>.
/// Rows and cells are not blocks and have no <c>type</c> property.
/// </para>
/// <para>
/// Limits: from 1 to 20 rows, from 1 to 10 cells per row, and every row must have the same number of cells.
/// Allowed at the top level and inside <see cref="CalloutBlockDto"/> and <see cref="ToggleBlockDto"/>.
/// Rule violations are reported as <c>knowledge.content.invalid_block</c> (HTTP 400).
/// </para>
/// </remarks>
/// <example>
/// <code>
/// { "type": "table", "hasHeaderRow": true, "rows": [
///     { "cells": [ { "text": [ { "text": "Age" } ] }, { "text": [ { "text": "Hours" } ] } ] },
///     { "cells": [ { "text": [ { "text": "18-64" } ] }, { "text": [ { "text": "7-9" } ] } ] } ] }
/// </code>
/// </example>
/// <param name="HasHeaderRow"><see langword="true"/> when the first row holds column headers and should be rendered as such.</param>
/// <param name="Rows">Rows of the table from top to bottom; from 1 to 20 rows, all with the same number of cells.</param>
public sealed record TableBlockDto(bool HasHeaderRow, IReadOnlyList<TableRowDto> Rows) : ContentBlockDto;
