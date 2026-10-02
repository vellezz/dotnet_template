using SuperApp.Framework.Domain.Aggregates;
using SuperApp.Framework.Domain.Results;
using Knowledge.Domain.Categories.Events;
using System.Text.RegularExpressions;
using Knowledge.Domain.Common;

namespace Knowledge.Domain.Categories;

/// <summary>
/// Aggregate root of a category: a flat (non-hierarchical) label with a display name and a URL slug, used to group materials
/// and collections (ADR-0028).
/// </summary>
/// <remarks>
/// <para>
/// Categories have no publication lifecycle and cannot be deleted; they are managed by editors (scope <c>knowledge.catalog.write</c>).
/// Materials and collections refer to them by <see cref="CategoryId"/> only (<see cref="Knowledge.Domain.Materials.MaterialCategory"/>,
/// <see cref="Knowledge.Domain.Collections.CollectionCategory"/>).
/// </para>
/// <para><b>Invariants</b>:</para>
/// <list type="bullet">
///   <item><description><see cref="Name"/> is required, trimmed, at most <see cref="MaxNameLength"/> characters; it can be changed with <see cref="Rename"/>.</description></item>
///   <item><description><see cref="Slug"/> matches <c>^[a-z0-9]+(-[a-z0-9]+)*$</c>, has at most <see cref="MaxSlugLength"/> characters and never changes,
///   so URLs built from it stay valid.</description></item>
///   <item><description>The slug is unique. The aggregate cannot check that itself: the command handler calls
///   <see cref="ICategoryRepository.SlugExistsAsync"/> and returns <see cref="CategoryErrors.SlugTaken"/>; a unique database index is the last line of defense,
///   and the unit of work reports its violation (two concurrent creations) as the same <see cref="CategoryErrors.SlugTaken"/>.</description></item>
/// </list>
/// <para>
/// <b>Domain events</b>: <see cref="CategoryChanged"/> on creation and on every rename that actually changes the name; it invalidates the cached category list.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // CreateCategoryHandler
/// if (await categories.SlugExistsAsync(command.Slug, cancellationToken))
/// {
///     return CategoryErrors.SlugTaken;
/// }
///
/// if (!Category.Create(command.Name, command.Slug).TryGetValue(out var category, out var error))
/// {
///     return error;
/// }
///
/// categories.Add(category);
/// </code>
/// </example>
/// <seealso cref="ICategoryRepository"/>
/// <seealso cref="CategoryErrors"/>
public sealed partial class Category : AggregateRoot<CategoryId>
{
    /// <summary>Maximum length of the category name in characters (after trimming white space).</summary>
    public const int MaxNameLength = 100;

    /// <summary>Maximum length of the category slug in characters.</summary>
    public const int MaxSlugLength = 100;

    private Category(CategoryId id)
        : base(id)
    {
    }

    /// <summary>Gets the display name: non-empty, trimmed, at most <see cref="MaxNameLength"/> characters. Not required to be unique.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the URL-friendly identifier, set at creation and immutable: lowercase ASCII letters and digits in segments separated by single
    /// hyphens (e.g. <c>sleep-hygiene</c>), at most <see cref="MaxSlugLength"/> characters. Unique across categories; the command handler
    /// checks it with <see cref="ICategoryRepository.SlugExistsAsync"/>.
    /// </summary>
    public string Slug { get; private set; } = string.Empty;

    /// <summary>Creates a new category and raises <see cref="CategoryChanged"/>.</summary>
    /// <remarks>
    /// Does not check slug uniqueness (the handler must do it before, see <see cref="CategoryErrors.SlugTaken"/>). The caller registers the
    /// result with <see cref="ICategoryRepository.Add"/>.
    /// </remarks>
    /// <param name="name">Display name; trimmed, required, at most <see cref="MaxNameLength"/> characters.</param>
    /// <param name="slug">
    /// Slug in the format <c>^[a-z0-9]+(-[a-z0-9]+)*$</c>, at most <see cref="MaxSlugLength"/> characters; not trimmed or lowercased,
    /// so <c>"Sleep"</c> or <c>" sleep"</c> are rejected rather than normalized.
    /// </param>
    /// <returns>
    /// The new category, or a validation error: <c>knowledge.category.invalid_name</c> for a blank or too long name, or
    /// <see cref="CategoryErrors.InvalidSlug"/> (<c>knowledge.category.invalid_slug</c>) for an invalid slug.
    /// </returns>
    public static Result<Category> Create(string name, string slug)
    {
        if (!Text.Required(name, MaxNameLength, "knowledge.category.invalid_name", "name")
                .TryGetValue(out var validName, out var nameError))
        {
            return nameError;
        }

        if (!IsValidSlug(slug))
        {
            return CategoryErrors.InvalidSlug;
        }

        var category = new Category(CategoryId.New()) { Name = validName, Slug = slug };
        category.Raise(new CategoryChanged(category.Id));
        return category;
    }

    /// <summary>Changes the display name of the category and raises <see cref="CategoryChanged"/>; the slug stays unchanged.</summary>
    /// <remarks>
    /// Idempotent: when the trimmed name equals the current <see cref="Name"/> (ordinal, case-sensitive comparison) the call succeeds
    /// without changing anything and without raising an event, so the cached category list is not invalidated needlessly.
    /// A change of letter case only (<c>"sleep"</c> to <c>"Sleep"</c>) is a real rename.
    /// </remarks>
    /// <param name="name">New display name; trimmed, required, at most <see cref="MaxNameLength"/> characters.</param>
    /// <returns>Success, or the validation error <c>knowledge.category.invalid_name</c> for a blank or too long name.</returns>
    public Result Rename(string name)
    {
        if (!Text.Required(name, MaxNameLength, "knowledge.category.invalid_name", "name")
                .TryGetValue(out var validName, out var nameError))
        {
            return nameError;
        }

        if (string.Equals(Name, validName, StringComparison.Ordinal))
        {
            return Result.Success();
        }

        Name = validName;
        Raise(new CategoryChanged(Id));
        return Result.Success();
    }

    private static bool IsValidSlug(string? slug) => slug is { Length: > 0 and <= MaxSlugLength } && SlugPattern().IsMatch(slug);

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex SlugPattern();
}
