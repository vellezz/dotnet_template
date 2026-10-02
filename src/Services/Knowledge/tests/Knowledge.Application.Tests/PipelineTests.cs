using SuperApp.Framework.Application.Persistence;
using SuperApp.Framework.Application.Security;
using SuperApp.Framework.Domain.Results;
using Knowledge.Application.Tests.Fakes;
using SuperApp.Framework.Application;
using Knowledge.Application.Features.Categories.CreateCategory;
using Knowledge.Domain.Categories;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using SuperApp.Framework.Testing;

namespace Knowledge.Application.Tests;

/// <summary>Runs the real MediatR pipeline with fake ports to verify the behavior order (ADR-0017) and the transaction behavior (ADR-0027).</summary>
public sealed class PipelineTests
{
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly FakeCategoryRepository _categories = new();

    [Fact]
    public async Task Authorization_runs_before_validation()
    {
        var result = await SendAsync(new CreateCategory(string.Empty, string.Empty), scopes: []);

        Assert.Equal("auth.missing_scope", ResultAssert.Failure(result).Code);
    }

    [Fact]
    public async Task Validation_errors_are_returned_as_result()
    {
        var result = await SendAsync(new CreateCategory(string.Empty, string.Empty), KnowledgeScopes.CatalogWrite);

        Assert.Equal(ErrorType.Validation, ResultAssert.Failure(result).Type);
        Assert.NotNull(ResultAssert.Failure(result).Details);
        Assert.Equal(0, _unitOfWork.CommitCount);
    }

    [Fact]
    public async Task Successful_command_is_saved_and_committed()
    {
        var result = await SendAsync(new CreateCategory("Sen", "sen"), KnowledgeScopes.CatalogWrite);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, _unitOfWork.SaveCount);
        Assert.Equal(1, _unitOfWork.CommitCount);
    }

    [Fact]
    public async Task Failed_command_is_not_committed()
    {
        _categories.Add(ResultAssert.Success(Category.Create("Sen", "sen")));

        var result = await SendAsync(new CreateCategory("Sen", "sen"), KnowledgeScopes.CatalogWrite);

        Assert.Equal(CategoryErrors.SlugTaken, result.Error);
        Assert.Equal(0, _unitOfWork.SaveCount);
        Assert.Equal(0, _unitOfWork.CommitCount);
    }

    [Fact]
    public async Task Save_conflict_is_returned_as_result_and_not_committed()
    {
        _unitOfWork.SaveResult = CategoryErrors.SlugTaken;

        var result = await SendAsync(new CreateCategory("Sen", "sen"), KnowledgeScopes.CatalogWrite);

        Assert.Equal(CategoryErrors.SlugTaken, result.Error);
        Assert.Equal(1, _unitOfWork.SaveCount);
        Assert.Equal(0, _unitOfWork.CommitCount);
    }

    [Fact]
    public async Task Failed_command_in_consumer_transaction_discards_its_changes()
    {
        _unitOfWork.HasActiveTransaction = true;
        _categories.Add(ResultAssert.Success(Category.Create("Sen", "sen")));

        var result = await SendAsync(new CreateCategory("Sen", "sen"), KnowledgeScopes.CatalogWrite);

        Assert.Equal(CategoryErrors.SlugTaken, result.Error);
        Assert.Equal(0, _unitOfWork.SaveCount);
        Assert.Equal(1, _unitOfWork.DiscardCount);
    }

    [Fact]
    public async Task Successful_command_in_consumer_transaction_is_saved_but_not_committed()
    {
        _unitOfWork.HasActiveTransaction = true;

        var result = await SendAsync(new CreateCategory("Sen", "sen"), KnowledgeScopes.CatalogWrite);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, _unitOfWork.SaveCount);
        Assert.Equal(0, _unitOfWork.CommitCount);
        Assert.Equal(0, _unitOfWork.DiscardCount);
    }

    private async Task<Result<Guid>> SendAsync(CreateCategory command, params string[] scopes)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAppApplication(KnowledgeApplication.Assembly);
        services.AddSingleton<ICurrentUser>(new FakeCurrentUser("editor", scopes));
        services.AddSingleton<IUnitOfWork>(_unitOfWork);
        services.AddSingleton<ICategoryRepository>(_categories);

        await using var provider = services.BuildServiceProvider();
        return await provider.GetRequiredService<ISender>().Send(command, TestContext.Current.CancellationToken);
    }
}
