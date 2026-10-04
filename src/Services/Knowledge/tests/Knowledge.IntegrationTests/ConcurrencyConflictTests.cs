using SuperApp.Framework.Application.Persistence;
using SuperApp.Framework.Infrastructure.Persistence;
using Knowledge.Domain.Categories;
using Knowledge.Infrastructure.Persistence.Write;
using Knowledge.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SuperApp.Framework.Testing;

namespace Knowledge.IntegrationTests;

/// <summary>
/// Two commands that load the same aggregate and both change it: the second save hits a stale <c>rowversion</c>. Over HTTP this must be
/// a 409 <c>persistence.concurrency_conflict</c> result; inside a consumer's transaction it must stay an exception so the message is retried.
/// A failed command inside a consumer's transaction must leave nothing for the consumer's own save to write.
/// </summary>
[Trait("Category", "Integration")]
[Collection(PipelineCollection.Name)]
public sealed class ConcurrencyConflictTests(ServiceFixture fixture)
{
    [Fact]
    public async Task Stale_aggregate_in_own_transaction_is_reported_as_concurrency_conflict()
    {
        var id = await CreateCategoryAsync();
        await using var first = fixture.Services.CreateAsyncScope();
        await using var second = fixture.Services.CreateAsyncScope();
        var firstCategory = await LoadAsync(first, id);
        var secondCategory = await LoadAsync(second, id);

        Assert.True(firstCategory.Rename("Pierwsza zmiana").IsSuccess);
        Assert.True((await SaveInOwnTransactionAsync(first)).IsSuccess);

        Assert.True(secondCategory.Rename("Druga zmiana").IsSuccess);
        var result = await SaveInOwnTransactionAsync(second);

        Assert.Equal(WriteDbContextBase.ConcurrencyConflict, result.Error);
    }

    [Fact]
    public async Task Stale_aggregate_in_consumer_transaction_is_rethrown_for_retry()
    {
        var id = await CreateCategoryAsync();
        await using var first = fixture.Services.CreateAsyncScope();
        await using var consumer = fixture.Services.CreateAsyncScope();
        var firstCategory = await LoadAsync(first, id);
        var consumerCategory = await LoadAsync(consumer, id);

        Assert.True(firstCategory.Rename("Zmiana z API").IsSuccess);
        Assert.True((await SaveInOwnTransactionAsync(first)).IsSuccess);

        var context = consumer.ServiceProvider.GetRequiredService<KnowledgeWriteDbContext>();
        await using var transaction = await context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
        Assert.True(consumerCategory.Rename("Zmiana z konsumenta").IsSuccess);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            consumer.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Discarded_changes_are_not_written_by_a_later_save_in_the_same_scope()
    {
        var id = await CreateCategoryAsync();
        await using (var consumer = fixture.Services.CreateAsyncScope())
        {
            var category = await LoadAsync(consumer, id);
            var unitOfWork = consumer.ServiceProvider.GetRequiredService<IUnitOfWork>();

            Assert.True(category.Rename("Zmiana nieudanej komendy").IsSuccess);
            unitOfWork.DiscardChanges();

            Assert.True((await unitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken)).IsSuccess);
        }

        await using var check = fixture.Services.CreateAsyncScope();
        Assert.Equal("Kategoria testowa", (await LoadAsync(check, id)).Name);
    }

    private async Task<CategoryId> CreateCategoryAsync()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var category = ResultAssert.Success(Category.Create("Kategoria testowa", $"concurrency-{Guid.NewGuid():N}"));
        scope.ServiceProvider.GetRequiredService<ICategoryRepository>().Add(category);
        Assert.True((await SaveInOwnTransactionAsync(scope)).IsSuccess);
        return category.Id;
    }

    private static async Task<Category> LoadAsync(AsyncServiceScope scope, CategoryId id) =>
        await scope.ServiceProvider.GetRequiredService<ICategoryRepository>().GetAsync(id, TestContext.Current.CancellationToken)
        ?? throw new InvalidOperationException("The test category was not found.");

    // The same sequence as the transaction behavior for an HTTP request: open, save, commit only on success.
    private static async Task<SuperApp.Framework.Domain.Results.Result> SaveInOwnTransactionAsync(AsyncServiceScope scope)
    {
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var saved = await unitOfWork.SaveChangesAsync(cancellationToken);
        if (saved.IsSuccess)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return saved;
    }
}
