using SuperApp.Framework.Domain.Aggregates;
using SuperApp.Framework.Domain.Results;
using Knowledge.Domain.Categories;
using Knowledge.Domain.Materials.Events;
using Knowledge.Domain.Common;
using Knowledge.Domain.Materials.Content;

namespace Knowledge.Domain.Materials;

/// <summary>
/// Aggregate root of an educational material: an article, a video or a podcast, with a title, an optional description,
/// optional main media, category assignments and block-based content (ADR-0028).
/// </summary>
/// <remarks>
/// <para><b>Lifecycle</b> (<see cref="PublicationStatus"/>):</para>
/// <list type="number">
///   <item><description><see cref="Create"/> produces a <see cref="PublicationStatus.Draft"/> without content. Editors fill it with
///   <see cref="UpdateDetails"/>, <see cref="ReplaceContent"/> and <see cref="SetCategories"/> in any order.</description></item>
///   <item><description><see cref="Publish"/> makes it visible to readers. It requires at least one content block and, for
///   <see cref="MaterialType.Video"/> and <see cref="MaterialType.Podcast"/>, a <see cref="MainMediaUrl"/>. A published material remains
///   editable, but no change may remove its content or (video/podcast) its main media.</description></item>
///   <item><description><see cref="Archive"/> (allowed from draft or published) is final: every further change or publication
///   fails with <see cref="MaterialErrors.Archived"/>.</description></item>
/// </list>
/// <para><b>Invariants and limits</b>:</para>
/// <list type="bullet">
///   <item><description><see cref="Type"/> is set once at creation and never changes.</description></item>
///   <item><description><see cref="Title"/> is required, trimmed, at most <see cref="MaxTitleLength"/> characters;
///   <see cref="Description"/> is optional, trimmed, at most <see cref="MaxDescriptionLength"/> characters.</description></item>
///   <item><description>An <see cref="MaterialType.Article"/> never has main media; <see cref="MainMediaDurationSeconds"/> is non-negative
///   and exists only together with <see cref="MainMediaUrl"/>.</description></item>
///   <item><description>At most <see cref="MaxCategories"/> distinct categories. That the categories exist is checked by the command handler
///   (another aggregate), which returns <see cref="Knowledge.Domain.Categories.CategoryErrors.UnknownCategories"/>.</description></item>
///   <item><description>Content is always valid according to <see cref="ContentBuilder"/> and is replaced as a whole;
///   <see cref="ContentPlainText"/> and <see cref="ReadingTimeMinutes"/> are always derived from it.</description></item>
/// </list>
/// <para><b>Domain events</b>:</para>
/// <list type="bullet">
///   <item><description><see cref="MaterialChanged"/> after every successful change, including <see cref="Create"/>, <see cref="Publish"/>
///   and <see cref="Archive"/>. It is raised at most once per instance until the unit of work dispatches and clears the events, so a command
///   that changes the material several times still produces a single event. It invalidates the material's cache entries (ADR-0020).</description></item>
///   <item><description><see cref="MaterialPublished"/> when a draft is published; translated to the integration event
///   <c>MaterialPublishedV1</c>.</description></item>
///   <item><description><see cref="MaterialArchived"/> when the material is archived; translated to <c>MaterialArchivedV1</c>, after which
///   the Worker removes the material from all favorites.</description></item>
/// </list>
/// <para>
/// Every successful change updates <see cref="UpdatedAt"/> to the <c>now</c> argument. Failed operations leave the aggregate unchanged
/// and raise no events. Times are passed in by the caller (<c>IClock</c>), the aggregate never reads the system clock.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// if (!Material.Create(MaterialType.Podcast, "Sleep and memory", null, "https://cdn.example.com/ep-12.mp3", 1860, clock.UtcNow)
///         .TryGetValue(out var material, out var error))
/// {
///     return error;
/// }
///
/// var content = material.ReplaceContent([new BlockSpec(BlockType.Paragraph, Text: [new SpanSpec("Show notes")])], clock.UtcNow);
/// if (content.IsFailure)
/// {
///     return content.Error;
/// }
///
/// materials.Add(material);
/// return material.Publish(clock.UtcNow); // raises MaterialChanged and MaterialPublished
/// </code>
/// </example>
/// <seealso cref="IMaterialRepository"/>
/// <seealso cref="MaterialErrors"/>
/// <seealso cref="ContentBuilder"/>
public sealed class Material : AggregateRoot<MaterialId>
{
    /// <summary>Maximum length of the material title in characters (after trimming white space).</summary>
    public const int MaxTitleLength = 200;

    /// <summary>Maximum length of the material description in characters (after trimming white space).</summary>
    public const int MaxDescriptionLength = 2000;

    /// <summary>Maximum number of categories a material can be assigned to.</summary>
    public const int MaxCategories = 20;

    private readonly List<MaterialCategory> _categories = [];
    private readonly List<ContentBlock> _blocks = [];

    private Material(MaterialId id)
        : base(id)
    {
    }

    /// <summary>Gets the kind of material; set at creation and immutable. Decides whether main media is allowed or required.</summary>
    public MaterialType Type { get; private set; }

    /// <summary>Gets the title: non-empty, trimmed, at most <see cref="MaxTitleLength"/> characters.</summary>
    public string Title { get; private set; } = string.Empty;

    /// <summary>Gets the optional description; an empty or white-space-only input is stored as <see langword="null"/>.</summary>
    public string? Description { get; private set; }

    /// <summary>
    /// Gets the HTTPS address of the main video or audio file; always <see langword="null"/> for an <see cref="MaterialType.Article"/>.
    /// Required for publishing a <see cref="MaterialType.Video"/> or <see cref="MaterialType.Podcast"/>.
    /// </summary>
    public WebUrl? MainMediaUrl { get; private set; }

    /// <summary>
    /// Gets the non-negative duration of the main media in seconds, or <see langword="null"/> when unknown.
    /// Can only be set together with <see cref="MainMediaUrl"/>.
    /// </summary>
    public int? MainMediaDurationSeconds { get; private set; }

    /// <summary>Gets the lifecycle stage; a new material is a <see cref="PublicationStatus.Draft"/>.</summary>
    public PublicationStatus Status { get; private set; }

    /// <summary>
    /// Gets the plain text of the content (heading texts, code and formatted text, one line per block), recomputed by
    /// <see cref="ReplaceContent"/>; intended for search (ADR-0028). Empty for a material without content.
    /// </summary>
    public string ContentPlainText { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the estimated reading time in whole minutes, recomputed by <see cref="ReplaceContent"/>
    /// (<see cref="ContentBuilder.WordsPerMinute"/> words per minute, rounded up; 0 when the content has no words).
    /// </summary>
    public int ReadingTimeMinutes { get; private set; }

    /// <summary>Gets the moment the material was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Gets the moment of the last successful change (including publication and archiving).</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// Gets the moment of publication; <see langword="null"/> until the material is published. Set once, because a material
    /// can be published only once (there is no way back to draft).
    /// </summary>
    public DateTimeOffset? PublishedAt { get; private set; }

    /// <summary>Gets the categories the material is assigned to, without duplicates.</summary>
    public IReadOnlyList<MaterialCategory> Categories => _categories;

    /// <summary>
    /// Gets the block content as a flattened tree: blocks in depth-first order (parent before its children), linked through
    /// <see cref="ContentBlock.ParentBlockId"/> and ordered among siblings by <see cref="ContentBlock.Position"/>.
    /// </summary>
    public IReadOnlyList<ContentBlock> Blocks => _blocks;

    /// <summary>Creates a new material without content in the <see cref="PublicationStatus.Draft"/> status.</summary>
    /// <remarks>
    /// Validation is the same as in <see cref="UpdateDetails"/>. On success the material has a new <see cref="MaterialId"/>,
    /// <see cref="CreatedAt"/> and <see cref="UpdatedAt"/> equal to <paramref name="now"/>, and a pending <see cref="MaterialChanged"/> event.
    /// The caller must still register it with <see cref="IMaterialRepository.Add"/>.
    /// </remarks>
    /// <param name="type">Kind of material; cannot be changed later.</param>
    /// <param name="title">Title; trimmed, required, at most <see cref="MaxTitleLength"/> characters.</param>
    /// <param name="description">Optional description; trimmed, at most <see cref="MaxDescriptionLength"/> characters; blank means none.</param>
    /// <param name="mainMediaUrl">
    /// HTTPS address of the main media, or <see langword="null"/> for none; must be <see langword="null"/> for <see cref="MaterialType.Article"/>.
    /// </param>
    /// <param name="mainMediaDurationSeconds">
    /// Non-negative duration of the main media in seconds, or <see langword="null"/>; allowed only together with <paramref name="mainMediaUrl"/>.
    /// </param>
    /// <param name="now">Current time, stored as the creation and last change time.</param>
    /// <returns>
    /// The new material, or the first error found by <see cref="UpdateDetails"/>: <c>knowledge.material.invalid_title</c>,
    /// <c>knowledge.material.invalid_description</c>, <see cref="MaterialErrors.MediaNotAllowedForArticle"/>
    /// (<c>knowledge.material.media_not_allowed</c>), <c>knowledge.url.invalid</c> or <see cref="MaterialErrors.InvalidDuration"/>
    /// (<c>knowledge.material.invalid_duration</c>).
    /// </returns>
    public static Result<Material> Create(
        MaterialType type,
        string title,
        string? description,
        string? mainMediaUrl,
        int? mainMediaDurationSeconds,
        DateTimeOffset now)
    {
        var material = new Material(MaterialId.New()) { Type = type, Status = PublicationStatus.Draft, CreatedAt = now };
        var details = material.UpdateDetails(title, description, mainMediaUrl, mainMediaDurationSeconds, now);
        return details.IsSuccess ? material : details.Error;
    }

    /// <summary>Replaces the title, description and main media of the material in one operation.</summary>
    /// <remarks>
    /// All four values are replaced; passing <see langword="null"/> for <paramref name="mainMediaUrl"/> removes the main media.
    /// The checks run in this order and the first failure is returned: archived status, title, description, media allowed for the type,
    /// URL format, duration, main media still present for a published video or podcast. On success raises <see cref="MaterialChanged"/>
    /// (once per unit of work) and sets <see cref="UpdatedAt"/>.
    /// </remarks>
    /// <param name="title">New title; trimmed, required, at most <see cref="MaxTitleLength"/> characters.</param>
    /// <param name="description">New description or <see langword="null"/>; trimmed, at most <see cref="MaxDescriptionLength"/> characters; blank means none.</param>
    /// <param name="mainMediaUrl">
    /// HTTPS address of the main media or <see langword="null"/> to have none; any non-null value (even empty) is rejected for an <see cref="MaterialType.Article"/>.
    /// </param>
    /// <param name="mainMediaDurationSeconds">
    /// Non-negative duration of the main media in seconds, or <see langword="null"/>; allowed only together with <paramref name="mainMediaUrl"/>.
    /// </param>
    /// <param name="now">Current time, stored as the last change time.</param>
    /// <returns>
    /// Success, or one of these errors:
    /// <list type="bullet">
    ///   <item><description><see cref="MaterialErrors.Archived"/> (<c>knowledge.material.archived</c>) when the material is archived;</description></item>
    ///   <item><description><c>knowledge.material.invalid_title</c> (validation) when the title is blank or too long;</description></item>
    ///   <item><description><c>knowledge.material.invalid_description</c> (validation) when the description is too long;</description></item>
    ///   <item><description><see cref="MaterialErrors.MediaNotAllowedForArticle"/> (<c>knowledge.material.media_not_allowed</c>) for an article with main media;</description></item>
    ///   <item><description><c>knowledge.url.invalid</c> (validation) when the address is not an absolute HTTPS URL (see <see cref="WebUrl"/>);</description></item>
    ///   <item><description><see cref="MaterialErrors.InvalidDuration"/> (<c>knowledge.material.invalid_duration</c>) for a negative duration or a duration without media;</description></item>
    ///   <item><description><see cref="MaterialErrors.MainMediaRequired"/> (<c>knowledge.material.main_media_required</c>) when removing the media of a published video or podcast.</description></item>
    /// </list>
    /// </returns>
    public Result UpdateDetails(string title, string? description, string? mainMediaUrl, int? mainMediaDurationSeconds, DateTimeOffset now)
    {
        if (Status == PublicationStatus.Archived)
        {
            return MaterialErrors.Archived;
        }

        if (!Text.Required(title, MaxTitleLength, "knowledge.material.invalid_title", "title")
                .TryGetValue(out var validTitle, out var titleError))
        {
            return titleError;
        }

        if (!Text.Optional(description, MaxDescriptionLength, "knowledge.material.invalid_description", "description")
                .TryGetValue(out var validDescription, out var descriptionError))
        {
            return descriptionError;
        }

        WebUrl? media = null;
        if (mainMediaUrl is not null)
        {
            if (Type == MaterialType.Article)
            {
                return MaterialErrors.MediaNotAllowedForArticle;
            }

            if (!WebUrl.Create(mainMediaUrl).TryGetValue(out var url, out var urlError))
            {
                return urlError;
            }

            media = url;
        }

        if (mainMediaDurationSeconds is < 0 || (mainMediaDurationSeconds is not null && media is null))
        {
            return MaterialErrors.InvalidDuration;
        }

        if (Status == PublicationStatus.Published && Type != MaterialType.Article && media is null)
        {
            return MaterialErrors.MainMediaRequired;
        }

        Title = validTitle;
        Description = validDescription;
        MainMediaUrl = media;
        MainMediaDurationSeconds = mainMediaDurationSeconds;
        Touch(now);
        return Result.Success();
    }

    /// <summary>
    /// Replaces the whole block content after validating it with <see cref="ContentBuilder.Build"/>, and recomputes
    /// <see cref="ContentPlainText"/> and <see cref="ReadingTimeMinutes"/>.
    /// </summary>
    /// <remarks>
    /// Content is never patched block by block: clients send the complete tree and every call replaces all blocks, generating
    /// new <see cref="BlockId"/>s. An empty list is allowed for a draft (it clears the content) but rejected for a published material.
    /// On success raises <see cref="MaterialChanged"/> (once per unit of work) and sets <see cref="UpdatedAt"/>.
    /// </remarks>
    /// <param name="blocks">
    /// Top-level blocks of the new content, each with its nested blocks in <see cref="BlockSpec.Children"/>; see <see cref="ContentBuilder"/>
    /// for the allowed structure and limits.
    /// </param>
    /// <param name="now">Current time, stored as the last change time.</param>
    /// <returns>
    /// Success, or one of these errors: <see cref="MaterialErrors.Archived"/> (<c>knowledge.material.archived</c>) when the material is archived;
    /// <c>knowledge.content.invalid_block</c> (validation) whose message starts with the path to the first invalid block,
    /// e.g. <c>blocks[2].children[0]</c>; <see cref="MaterialErrors.ContentRequired"/> (<c>knowledge.material.content_required</c>)
    /// when the new content of a published material is empty.
    /// </returns>
    public Result ReplaceContent(IReadOnlyList<BlockSpec> blocks, DateTimeOffset now)
    {
        if (Status == PublicationStatus.Archived)
        {
            return MaterialErrors.Archived;
        }

        if (!ContentBuilder.Build(blocks).TryGetValue(out var content, out var contentError))
        {
            return contentError;
        }

        if (Status == PublicationStatus.Published && content.Blocks.Count == 0)
        {
            return MaterialErrors.ContentRequired;
        }

        _blocks.Clear();
        _blocks.AddRange(content.Blocks);
        ContentPlainText = content.PlainText;
        ReadingTimeMinutes = content.ReadingTimeMinutes;
        Touch(now);
        return Result.Success();
    }

    /// <summary>Replaces the set of categories the material is assigned to.</summary>
    /// <remarks>
    /// Duplicates are silently removed first; an empty collection removes all assignments. The aggregate cannot see other aggregates,
    /// so the command handler must first check that every category exists (<see cref="Knowledge.Domain.Categories.ICategoryRepository.AllExistAsync"/>)
    /// and return <see cref="Knowledge.Domain.Categories.CategoryErrors.UnknownCategories"/> otherwise.
    /// On success raises <see cref="MaterialChanged"/> (once per unit of work) and sets <see cref="UpdatedAt"/>.
    /// </remarks>
    /// <param name="categoryIds">
    /// Target categories; at most <see cref="MaxCategories"/> distinct entries. The limit is checked after duplicates are removed, so
    /// repeating an identifier never causes <see cref="MaterialErrors.TooManyCategories"/>.
    /// </param>
    /// <param name="now">Current time, stored as the last change time.</param>
    /// <returns>
    /// Success, or <see cref="MaterialErrors.Archived"/> (<c>knowledge.material.archived</c>) when the material is archived, or
    /// <see cref="MaterialErrors.TooManyCategories"/> (<c>knowledge.material.too_many_categories</c>) when more than <see cref="MaxCategories"/>
    /// distinct categories are given.
    /// </returns>
    public Result SetCategories(IReadOnlyCollection<CategoryId> categoryIds, DateTimeOffset now)
    {
        if (Status == PublicationStatus.Archived)
        {
            return MaterialErrors.Archived;
        }

        var distinctIds = categoryIds.Distinct().ToList();
        if (distinctIds.Count > MaxCategories)
        {
            return MaterialErrors.TooManyCategories;
        }

        _categories.Clear();
        _categories.AddRange(distinctIds.Select(id => new MaterialCategory(id)));
        Touch(now);
        return Result.Success();
    }

    /// <summary>
    /// Publishes a draft material, making it visible to readers, and raises <see cref="MaterialPublished"/>.
    /// </summary>
    /// <remarks>
    /// Preconditions: the material is not archived, has at least one content block and, for a video or podcast, has <see cref="MainMediaUrl"/>.
    /// On success the status becomes <see cref="PublicationStatus.Published"/>, <see cref="PublishedAt"/> and <see cref="UpdatedAt"/> are set to
    /// <paramref name="now"/>, and <see cref="MaterialChanged"/> and <see cref="MaterialPublished"/> (carrying the same <paramref name="now"/>
    /// as <see cref="MaterialPublished.PublishedAt"/>) are raised.
    /// Publishing an already published material is an idempotent no-op: it succeeds without changes or events.
    /// </remarks>
    /// <param name="now">Current time, stored as the publication and last change time.</param>
    /// <returns>
    /// Success, or <see cref="MaterialErrors.Archived"/> (<c>knowledge.material.archived</c>) for an archived material,
    /// <see cref="MaterialErrors.ContentRequired"/> (<c>knowledge.material.content_required</c>) when there is no content, or
    /// <see cref="MaterialErrors.MainMediaRequired"/> (<c>knowledge.material.main_media_required</c>) for a video or podcast without main media.
    /// </returns>
    public Result Publish(DateTimeOffset now)
    {
        switch (Status)
        {
            case PublicationStatus.Published:
                return Result.Success();
            case PublicationStatus.Archived:
                return MaterialErrors.Archived;
        }

        if (_blocks.Count == 0)
        {
            return MaterialErrors.ContentRequired;
        }

        if (Type != MaterialType.Article && MainMediaUrl is null)
        {
            return MaterialErrors.MainMediaRequired;
        }

        Status = PublicationStatus.Published;
        PublishedAt = now;
        Touch(now);
        Raise(new MaterialPublished(Id, Type, Title, now));
        return Result.Success();
    }

    // Records a successful change: updates the timestamp and raises MaterialChanged unless one is already pending,
    // so that one unit of work yields at most one cache invalidation per material.
    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        if (!DomainEvents.OfType<MaterialChanged>().Any())
        {
            Raise(new MaterialChanged(Id));
        }
    }

    /// <summary>
    /// Archives the material, irreversibly blocking all further changes, and raises <see cref="MaterialArchived"/>.
    /// </summary>
    /// <remarks>
    /// Allowed from both <see cref="PublicationStatus.Draft"/> and <see cref="PublicationStatus.Published"/>. On success the status becomes
    /// <see cref="PublicationStatus.Archived"/>, <see cref="UpdatedAt"/> is set and <see cref="MaterialChanged"/> and <see cref="MaterialArchived"/>
    /// are raised; the latter carries <paramref name="now"/> as <see cref="MaterialArchived.ArchivedAt"/> and eventually removes the material
    /// from all favorites. <see cref="PublishedAt"/> is kept.
    /// Archiving an already archived material is an idempotent no-op without events.
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
        Touch(now);
        Raise(new MaterialArchived(Id, now));
        return Result.Success();
    }
}
