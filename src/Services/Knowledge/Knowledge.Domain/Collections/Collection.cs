using SuperApp.Framework.Domain.Aggregates;
using SuperApp.Framework.Domain.Results;
using Knowledge.Domain.Categories;
using Knowledge.Domain.Collections.Events;
using Knowledge.Domain.Materials;
using Knowledge.Domain.Common;

namespace Knowledge.Domain.Collections;

/// <summary>
/// Aggregate root of a collection: an ordered, curated list of materials (for example a course or a reading list) with a title,
/// an optional description and category assignments (ADR-0028).
/// </summary>
/// <remarks>
/// <para><b>Lifecycle</b> (<see cref="PublicationStatus"/>), the same as for materials:</para>
/// <list type="number">
///   <item><description><see cref="Create"/> produces an empty <see cref="PublicationStatus.Draft"/>. Editors fill it with
///   <see cref="UpdateDetails"/>, <see cref="SetItems"/> and <see cref="SetCategories"/>.</description></item>
///   <item><description><see cref="Publish"/> requires at least one item. A published collection stays editable, but its item list
///   cannot become empty.</description></item>
///   <item><description><see cref="Archive"/> (from draft or published) is final: further changes and publication fail with
///   <see cref="CollectionErrors.Archived"/>.</description></item>
/// </list>
/// <para><b>Invariants and limits</b>:</para>
/// <list type="bullet">
///   <item><description><see cref="Title"/> is required, trimmed, at most <see cref="MaxTitleLength"/> characters; <see cref="Description"/> is optional,
///   trimmed, at most <see cref="MaxDescriptionLength"/> characters.</description></item>
///   <item><description>At most <see cref="MaxItems"/> items, each material at most once, positions 0..n-1 in the given order.</description></item>
///   <item><description>At most <see cref="MaxCategories"/> distinct categories.</description></item>
///   <item><description>The collection holds only IDs of other aggregates. Whether those materials and categories exist is checked by the command
///   handlers (<see cref="CollectionErrors.UnknownMaterials"/>, <see cref="Knowledge.Domain.Categories.CategoryErrors.UnknownCategories"/>).
///   The publication status of the materials is not checked: a published collection may reference draft or archived materials, and the query
///   side decides what readers see.</description></item>
/// </list>
/// <para>
/// <b>Domain events</b>: only <see cref="Archive"/> raises an event (<see cref="CollectionArchived"/>), which removes the collection from all
/// favorites. Creation, changes and publication raise no events. Every successful change sets <see cref="UpdatedAt"/> to the <c>now</c> argument;
/// failed operations change nothing.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// if (!Collection.Create("Better sleep in 7 days", null, clock.UtcNow).TryGetValue(out var collection, out var error))
/// {
///     return error;
/// }
///
/// var items = collection.SetItems([introId, routineId, caffeineId], clock.UtcNow); // after IMaterialRepository.AllExistAsync
/// if (items.IsFailure)
/// {
///     return items.Error;
/// }
///
/// collections.Add(collection);
/// return collection.Publish(clock.UtcNow);
/// </code>
/// </example>
/// <seealso cref="ICollectionRepository"/>
/// <seealso cref="CollectionErrors"/>
public sealed class Collection : AggregateRoot<CollectionId>
{
    /// <summary>Maximum length of the collection title in characters (after trimming white space).</summary>
    public const int MaxTitleLength = 200;

    /// <summary>Maximum length of the collection description in characters (after trimming white space).</summary>
    public const int MaxDescriptionLength = 2000;

    /// <summary>Maximum number of materials (items) in a collection.</summary>
    public const int MaxItems = 200;

    /// <summary>Maximum number of categories a collection can be assigned to.</summary>
    public const int MaxCategories = 20;

    private readonly List<CollectionItem> _items = [];
    private readonly List<CollectionCategory> _categories = [];

    private Collection(CollectionId id)
        : base(id)
    {
    }

    /// <summary>Gets the title: non-empty, trimmed, at most <see cref="MaxTitleLength"/> characters.</summary>
    public string Title { get; private set; } = string.Empty;

    /// <summary>Gets the optional description; an empty or white-space-only input is stored as <see langword="null"/>.</summary>
    public string? Description { get; private set; }

    /// <summary>Gets the lifecycle stage; a new collection is a <see cref="PublicationStatus.Draft"/>.</summary>
    public PublicationStatus Status { get; private set; }

    /// <summary>Gets the moment the collection was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Gets the moment of the last successful change (including publication and archiving).</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// Gets the moment of publication; <see langword="null"/> until the collection is published. Set once, because there is no way back to draft.
    /// </summary>
    public DateTimeOffset? PublishedAt { get; private set; }

    /// <summary>
    /// Gets the items (materials) of the collection; each material appears at most once and <see cref="CollectionItem.Position"/> gives the
    /// order. Do not rely on the list order after loading from the database; sort by position.
    /// </summary>
    public IReadOnlyList<CollectionItem> Items => _items;

    /// <summary>Gets the categories the collection is assigned to, without duplicates.</summary>
    public IReadOnlyList<CollectionCategory> Categories => _categories;

    /// <summary>Creates a new, empty collection in the <see cref="PublicationStatus.Draft"/> status. Raises no events.</summary>
    /// <remarks>The caller registers the result with <see cref="ICollectionRepository.Add"/>.</remarks>
    /// <param name="title">Title; trimmed, required, at most <see cref="MaxTitleLength"/> characters.</param>
    /// <param name="description">Optional description; trimmed, at most <see cref="MaxDescriptionLength"/> characters; blank means none.</param>
    /// <param name="now">Current time, stored as the creation and last change time.</param>
    /// <returns>
    /// The new collection, or the validation error <c>knowledge.collection.invalid_title</c> (blank or too long title) or
    /// <c>knowledge.collection.invalid_description</c> (too long description).
    /// </returns>
    public static Result<Collection> Create(string title, string? description, DateTimeOffset now)
    {
        var collection = new Collection(CollectionId.New()) { Status = PublicationStatus.Draft, CreatedAt = now };
        var details = collection.UpdateDetails(title, description, now);
        return details.IsSuccess ? collection : details.Error;
    }

    /// <summary>Replaces the title and description of the collection. Raises no events.</summary>
    /// <param name="title">New title; trimmed, required, at most <see cref="MaxTitleLength"/> characters.</param>
    /// <param name="description">New description or <see langword="null"/>; trimmed, at most <see cref="MaxDescriptionLength"/> characters; blank means none.</param>
    /// <param name="now">Current time, stored as the last change time.</param>
    /// <returns>
    /// Success, or <see cref="CollectionErrors.Archived"/> (<c>knowledge.collection.archived</c>) for an archived collection,
    /// or the validation error <c>knowledge.collection.invalid_title</c> or <c>knowledge.collection.invalid_description</c>.
    /// </returns>
    public Result UpdateDetails(string title, string? description, DateTimeOffset now)
    {
        if (Status == PublicationStatus.Archived)
        {
            return CollectionErrors.Archived;
        }

        if (!Text.Required(title, MaxTitleLength, "knowledge.collection.invalid_title", "title")
                .TryGetValue(out var validTitle, out var titleError))
        {
            return titleError;
        }

        if (!Text.Optional(description, MaxDescriptionLength, "knowledge.collection.invalid_description", "description")
                .TryGetValue(out var validDescription, out var descriptionError))
        {
            return descriptionError;
        }

        Title = validTitle;
        Description = validDescription;
        UpdatedAt = now;
        return Result.Success();
    }

    /// <summary>
    /// Replaces all items of the collection with the given materials, in the given order (the first gets position 0). Raises no events.
    /// </summary>
    /// <remarks>
    /// Used both to add/remove materials and to reorder them: the client always sends the complete list. Checks run in this order:
    /// archived, duplicates, count, empty list of a published collection. Duplicates are rejected, not removed, so the count is always
    /// the count of distinct materials. The handler must check beforehand that all materials exist
    /// (<see cref="Knowledge.Domain.Materials.IMaterialRepository.AllExistAsync"/>, otherwise <see cref="CollectionErrors.UnknownMaterials"/>).
    /// </remarks>
    /// <param name="materialIds">Materials in the target order; no duplicates, at most <see cref="MaxItems"/>; may be empty for a draft.</param>
    /// <param name="now">Current time, stored as the last change time.</param>
    /// <returns>
    /// Success, or one of: <see cref="CollectionErrors.Archived"/> (<c>knowledge.collection.archived</c>),
    /// <see cref="CollectionErrors.DuplicateItems"/> (<c>knowledge.collection.duplicate_items</c>),
    /// <see cref="CollectionErrors.TooManyItems"/> (<c>knowledge.collection.too_many_items</c>) or
    /// <see cref="CollectionErrors.ItemsRequired"/> (<c>knowledge.collection.items_required</c>, an empty list for a published collection).
    /// </returns>
    public Result SetItems(IReadOnlyList<MaterialId> materialIds, DateTimeOffset now)
    {
        if (Status == PublicationStatus.Archived)
        {
            return CollectionErrors.Archived;
        }

        if (materialIds.Distinct().Count() != materialIds.Count)
        {
            return CollectionErrors.DuplicateItems;
        }

        if (materialIds.Count > MaxItems)
        {
            return CollectionErrors.TooManyItems;
        }

        if (Status == PublicationStatus.Published && materialIds.Count == 0)
        {
            return CollectionErrors.ItemsRequired;
        }

        _items.Clear();
        _items.AddRange(materialIds.Select((id, position) => new CollectionItem(id, position)));
        UpdatedAt = now;
        return Result.Success();
    }

    /// <summary>
    /// Replaces the set of categories the collection is assigned to; duplicates are silently removed and an empty collection removes all
    /// assignments. Raises no events.
    /// </summary>
    /// <remarks>
    /// That the categories exist is checked by the command handler (another aggregate), which returns
    /// <see cref="Knowledge.Domain.Categories.CategoryErrors.UnknownCategories"/> otherwise.
    /// </remarks>
    /// <param name="categoryIds">
    /// Target categories; at most <see cref="MaxCategories"/> distinct entries. Duplicates are removed before counting, so repeating an
    /// identifier never causes <see cref="CollectionErrors.TooManyCategories"/>.
    /// </param>
    /// <param name="now">Current time, stored as the last change time.</param>
    /// <returns>
    /// Success, or <see cref="CollectionErrors.Archived"/> (<c>knowledge.collection.archived</c>) or
    /// <see cref="CollectionErrors.TooManyCategories"/> (<c>knowledge.collection.too_many_categories</c>, more than <see cref="MaxCategories"/>
    /// distinct categories).
    /// </returns>
    public Result SetCategories(IReadOnlyCollection<CategoryId> categoryIds, DateTimeOffset now)
    {
        if (Status == PublicationStatus.Archived)
        {
            return CollectionErrors.Archived;
        }

        var distinctIds = categoryIds.Distinct().ToList();
        if (distinctIds.Count > MaxCategories)
        {
            return CollectionErrors.TooManyCategories;
        }

        _categories.Clear();
        _categories.AddRange(distinctIds.Select(id => new CollectionCategory(id)));
        UpdatedAt = now;
        return Result.Success();
    }

    /// <summary>
    /// Publishes a draft collection, making it visible to readers. Requires at least one item. Raises no events.
    /// </summary>
    /// <remarks>
    /// On success the status becomes <see cref="PublicationStatus.Published"/> and <see cref="PublishedAt"/> and <see cref="UpdatedAt"/> are set.
    /// Publishing an already published collection is an idempotent no-op.
    /// </remarks>
    /// <param name="now">Current time, stored as the publication and last change time.</param>
    /// <returns>
    /// Success, or <see cref="CollectionErrors.Archived"/> (<c>knowledge.collection.archived</c>) or
    /// <see cref="CollectionErrors.ItemsRequired"/> (<c>knowledge.collection.items_required</c>) for a collection without items.
    /// </returns>
    public Result Publish(DateTimeOffset now)
    {
        switch (Status)
        {
            case PublicationStatus.Published:
                return Result.Success();
            case PublicationStatus.Archived:
                return CollectionErrors.Archived;
        }

        if (_items.Count == 0)
        {
            return CollectionErrors.ItemsRequired;
        }

        Status = PublicationStatus.Published;
        PublishedAt = now;
        UpdatedAt = now;
        return Result.Success();
    }

    /// <summary>
    /// Archives the collection, irreversibly blocking all further changes, and raises <see cref="CollectionArchived"/>.
    /// </summary>
    /// <remarks>
    /// Allowed from draft and published. The event carries <paramref name="now"/> as <see cref="CollectionArchived.ArchivedAt"/> and eventually
    /// removes the collection from all favorites. Archiving an already archived
    /// collection is an idempotent no-op without an event.
    /// </remarks>
    /// <param name="now">Current time, stored as the last change time and reported as the archiving time.</param>
    /// <returns>Always success.</returns>
    public Result Archive(DateTimeOffset now)
    {
        if (Status == PublicationStatus.Archived)
        {
            return Result.Success();
        }

        Status = PublicationStatus.Archived;
        UpdatedAt = now;
        Raise(new CollectionArchived(Id, now));
        return Result.Success();
    }
}
