namespace SuperApp.Cli.Scaffolding.Editors;

/// <summary>Source code of a new use case (vertical slice, ADR-0032): command or query, validator, DTO, handler and controller action.</summary>
/// <remarks>
/// <para>
/// The code compiles and passes the analyzers as generated, so the solution stays green between scaffolding and implementation: XML
/// documentation is complete with <c>TODO</c> where the domain has to be described, the handler throws <see cref="NotImplementedException"/>
/// (a call before it is written fails loudly instead of returning a fake success), and the action has its own route segment, so it never
/// collides with existing actions. Everything marked <c>TODO</c> is for the developer (recipes 01 and 02).
/// </para>
/// </remarks>
internal static class UseCaseCode
{
    /// <summary>The command record.</summary>
    /// <param name="service">Service name.</param>
    /// <param name="feature">Feature folder (the aggregate), e.g. <c>Invoices</c>.</param>
    /// <param name="name">Use case name, e.g. <c>IssueInvoice</c>.</param>
    /// <param name="scopeConstant">Name of the scope constant in <c>{Service}Scopes</c>.</param>
    /// <returns>The file content.</returns>
    public static string Command(string service, string feature, string name, string scopeConstant) => $$"""
        using SuperApp.Framework.Application.Messaging;
        using SuperApp.Framework.Application.Security;

        namespace {{service}}.Application.Features.{{feature}}.{{name}};

        /// <summary>
        /// Command: TODO what it changes, in the language of the domain.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Requires the scope <see cref="{{service}}Scopes.{{scopeConstant}}"/>. TODO: the rules the aggregate checks and the events it raises.
        /// </para>
        /// <para>
        /// Result: success, or TODO every error of the aggregate with its code and HTTP status, plus <c>validation.failed</c> (HTTP 400) and the
        /// pipeline errors <c>auth.missing_scope</c> and <c>auth.unauthenticated</c> (HTTP 403).
        /// </para>
        /// </remarks>
        [RequiresScope({{service}}Scopes.{{scopeConstant}})]
        public sealed record {{name}} : ICommand;

        """;

    /// <summary>The handler of a command (Application).</summary>
    /// <param name="service">Service name.</param>
    /// <param name="feature">Feature folder.</param>
    /// <param name="name">Use case name.</param>
    /// <returns>The file content.</returns>
    public static string CommandHandler(string service, string feature, string name) => $$"""
        using SuperApp.Framework.Application.Messaging;
        using SuperApp.Framework.Domain.Results;

        namespace {{service}}.Application.Features.{{feature}}.{{name}};

        /// <summary>
        /// Handles <see cref="{{name}}"/>: TODO load the aggregate through its repository, call one of its methods and return its result.
        /// </summary>
        /// <remarks>
        /// Orchestration only: the business rule belongs to the aggregate (ADR-0002); one command changes one aggregate. The transaction
        /// behavior saves the unit of work, dispatches domain events and writes the outbox when this handler returns success.
        /// </remarks>
        internal sealed class {{name}}Handler : ICommandHandler<{{name}}>
        {
            /// <inheritdoc />
            public Task<Result> Handle({{name}} command, CancellationToken cancellationToken) =>
                throw new NotImplementedException("TODO: implement {{name}} (recipe 01).");
        }

        """;

    /// <summary>The query record.</summary>
    /// <param name="service">Service name.</param>
    /// <param name="feature">Feature folder.</param>
    /// <param name="name">Use case name, e.g. <c>GetInvoice</c>.</param>
    /// <param name="dto">Name of the result DTO.</param>
    /// <param name="scopeConstant">Name of the scope constant in <c>{Service}Scopes</c>.</param>
    /// <returns>The file content.</returns>
    public static string Query(string service, string feature, string name, string dto, string scopeConstant) => $$"""
        using SuperApp.Framework.Application.Messaging;
        using SuperApp.Framework.Application.Security;
        using SuperApp.Framework.Domain.Results;

        namespace {{service}}.Application.Features.{{feature}}.{{name}};

        /// <summary>
        /// Query: TODO what it returns and for whom.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Requires the scope <see cref="{{service}}Scopes.{{scopeConstant}}"/>. The handler lives in Infrastructure (ADR-0026) and projects the
        /// read model directly to <see cref="{{dto}}"/>, bypassing the domain.
        /// </para>
        /// <para>
        /// Result: <see cref="{{dto}}"/>, or TODO every error with its code, plus <c>validation.failed</c> (HTTP 400) and the pipeline errors
        /// <c>auth.missing_scope</c> and <c>auth.unauthenticated</c> (HTTP 403).
        /// </para>
        /// </remarks>
        [RequiresScope({{service}}Scopes.{{scopeConstant}})]
        public sealed record {{name}} : IQuery<Result<{{dto}}>>;

        """;

    /// <summary>The result DTO of a query.</summary>
    /// <param name="service">Service name.</param>
    /// <param name="feature">Feature folder.</param>
    /// <param name="name">Use case name.</param>
    /// <param name="dto">Name of the DTO.</param>
    /// <returns>The file content.</returns>
    public static string Dto(string service, string feature, string name, string dto) => $$"""
        namespace {{service}}.Application.Features.{{feature}}.{{name}};

        /// <summary>
        /// Result of <see cref="{{name}}"/>: TODO the data a client needs, shaped for the screen or API (primitives only, no domain types).
        /// </summary>
        public sealed record {{dto}};

        """;

    /// <summary>The handler of a query (Infrastructure, ADR-0026).</summary>
    /// <param name="service">Service name.</param>
    /// <param name="feature">Feature folder.</param>
    /// <param name="name">Use case name.</param>
    /// <param name="dto">Name of the result DTO.</param>
    /// <returns>The file content.</returns>
    public static string QueryHandler(string service, string feature, string name, string dto) => $$"""
        using SuperApp.Framework.Application.Messaging;
        using SuperApp.Framework.Domain.Results;
        using {{service}}.Application.Features.{{feature}}.{{name}};

        namespace {{service}}.Infrastructure.Features.{{feature}};

        /// <summary>
        /// Handles <see cref="{{name}}"/> on the read side (ADR-0026): TODO inject <c>{{service}}ReadDbContext</c> and project its read model to
        /// <see cref="{{dto}}"/> (no aggregates, no repositories, no tracking).
        /// </summary>
        /// <remarks>Data of one user is always filtered by <c>ICurrentUser</c>; shared data may be cached with tags (ADR-0020).</remarks>
        internal sealed class {{name}}Handler : IQueryHandler<{{name}}, Result<{{dto}}>>
        {
            /// <inheritdoc />
            public Task<Result<{{dto}}>> Handle({{name}} query, CancellationToken cancellationToken) =>
                throw new NotImplementedException("TODO: implement {{name}} (recipe 02).");
        }

        """;

    /// <summary>The validator of a command or query.</summary>
    /// <param name="service">Service name.</param>
    /// <param name="feature">Feature folder.</param>
    /// <param name="name">Use case name.</param>
    /// <returns>The file content.</returns>
    public static string Validator(string service, string feature, string name) => $$"""
        using FluentValidation;

        namespace {{service}}.Application.Features.{{feature}}.{{name}};

        /// <summary>
        /// Validates <see cref="{{name}}"/> before the handler runs: TODO the input rules, with limits taken from the domain constants.
        /// </summary>
        /// <remarks>
        /// Run by the validation pipeline behavior after authorization; failures become <c>validation.failed</c> (HTTP 400) with messages per
        /// field. Business rules stay in the aggregate (chapter 06).
        /// </remarks>
        internal sealed class {{name}}Validator : AbstractValidator<{{name}}>
        {
            /// <summary>Defines the rules. TODO: one <c>RuleFor</c> per property.</summary>
            public {{name}}Validator()
            {
            }
        }

        """;

    /// <summary>A new controller for the feature, in the layout of the existing ones.</summary>
    /// <param name="service">Service name.</param>
    /// <param name="feature">Feature folder; the controller is <c>{Feature}Controller</c> with route <c>v1/{feature}</c>.</param>
    /// <param name="route">Route prefix, e.g. <c>v1/invoices</c>.</param>
    /// <param name="hasBase">Whether the service has <c>{Service}ControllerBase</c> (services from the template).</param>
    /// <returns>The file content.</returns>
    public static string Controller(string service, string feature, string route, bool hasBase)
    {
        var attributes = hasBase
            ? string.Empty
            : """
              [ApiController]
              [ProducesErrorResponseType(typeof(void))]
              [ProducesResponseType(StatusCodes.Status401Unauthorized)]
              [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, MediaTypeNames.Application.ProblemJson)]

              """;
        var baseType = hasBase ? $"{service}ControllerBase" : "ControllerBase";
        return $$"""
            using MediatR;
            using Microsoft.AspNetCore.Mvc;
            using System.Net.Mime;
            using SuperApp.Framework.Infrastructure.Api;

            namespace {{service}}.Api.Controllers;

            /// <summary>
            /// TODO: the resource {{feature}} of the {{service}} service, described for API consumers (it is copied into the OpenAPI contract).
            /// </summary>
            /// <param name="sender">MediatR sender that forwards commands and queries to the Application layer.</param>
            {{attributes}}[Route("{{route}}")]
            public sealed class {{feature}}Controller(ISender sender) : {{baseType}}
            {
            }

            """;
    }

    /// <summary>The controller action that sends the command or query.</summary>
    /// <param name="name">Use case name.</param>
    /// <param name="segment">Route segment of the action, e.g. <c>issue-invoice</c>.</param>
    /// <param name="scope">Full scope name, for the documentation.</param>
    /// <param name="dto">Name of the result DTO for a query; <see langword="null"/> for a command.</param>
    /// <returns>The lines of the action, indented for a class body.</returns>
    public static string[] Action(string name, string segment, string scope, string? dto)
    {
        var isQuery = dto is not null;
        string[] responses = isQuery
            ? [$"    [ProducesResponseType<{dto}>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]"]
            : ["    [ProducesResponseType(StatusCodes.Status204NoContent)]"];
        return
        [
            string.Empty,
            $"    /// <summary>TODO: what the endpoint {(isQuery ? "returns" : "does")}, for API consumers.</summary>",
            $"    /// <remarks>Required scope: <c>{scope}</c>. TODO: rules and limits the caller has to know.</remarks>",
            "    /// <param name=\"cancellationToken\">Cancellation of the HTTP request.</param>",
            $"    /// <returns>{(isQuery ? "TODO: the returned data." : "Nothing on success.")}</returns>",
            $"    /// <response code=\"{(isQuery ? "200" : "204")}\">{(isQuery ? "TODO: the data." : "Done.")}</response>",
            "    /// <response code=\"400\">Invalid input (<c>validation.failed</c>).</response>",
            "    /// <response code=\"401\">Missing, expired or invalid access token.</response>",
            $"    /// <response code=\"403\">The token lacks the scope <c>{scope}</c> (<c>auth.missing_scope</c>) or does not identify a user (<c>auth.unauthenticated</c>).</response>",
            $"    [{(isQuery ? "HttpGet" : "HttpPost")}(\"{segment}\")]",
            .. responses,
            "    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, MediaTypeNames.Application.ProblemJson)]",
            $"    public async Task<IActionResult> {name}(CancellationToken cancellationToken) =>",
            $"        this.ToActionResult(await sender.Send(new {name}(), cancellationToken));",
        ];
    }
}
