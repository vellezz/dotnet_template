namespace Knowledge.Application.Content.Blocks;

/// <summary>
/// One row of a <see cref="TableBlockDto"/>. Not a block by itself, so it has no <c>type</c> property.
/// </summary>
/// <remarks>JSON shape: <c>{ "cells": [ { "text": [ ...spans... ] } ] }</c>.</remarks>
/// <param name="Cells">Cells of the row from left to right; from 1 to 10 cells, the same number in every row of the table.</param>
public sealed record TableRowDto(IReadOnlyList<TableCellDto> Cells);
