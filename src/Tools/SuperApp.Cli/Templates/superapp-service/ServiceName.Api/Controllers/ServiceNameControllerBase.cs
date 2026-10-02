using Microsoft.AspNetCore.Mvc;
using System.Net.Mime;

namespace ServiceName.Api.Controllers;

/// <summary>
/// Base class of every controller of the ServiceName service: enables API conventions (<c>[ApiController]</c>) and declares the
/// responses shared by all actions (<c>401</c>, <c>403</c>) for the OpenAPI contract.
/// </summary>
/// <remarks>
/// <para>
/// Controllers are thin: they map HTTP to a command or query, send it with <c>ISender.Send(...)</c> and turn the returned
/// <c>Result</c> into a response with <c>this.ToActionResult(result)</c>. Errors become <c>ProblemDetails</c> with the status derived from
/// <c>Error.Type</c>: Validation 400, Forbidden 403, NotFound 404, Conflict 409, BusinessRule 422 (ADR-0015). No business logic, no
/// <c>DbContext</c> and no Infrastructure types here.
/// </para>
/// <para>
/// Every request needs an authenticated user (fallback policy of <c>AddAppApi</c>); coarse-grained scope checks happen in the gateway,
/// fine-grained authorization in the Application pipeline. Routes are versioned in the path (<c>v1/...</c>); clients reach them through
/// the gateway routes <c>/api/servicename/...</c>, which must be added to the gateway configuration by a migration (ADR-0022).
/// </para>
/// <para>
/// Document every action with XML comments written for API consumers (<c>summary</c>, <c>param</c>, <c>returns</c>, one
/// <c>response</c> per status code with the error codes): they are copied into the committed OpenAPI contract (ADR-0033).
/// For an action with a request body, document <c>cancellationToken</c> before the body parameter: the OpenAPI XML comment generator
/// copies the description of every <c>param</c> that is not a route or query parameter into the request body description, so the last
/// such <c>param</c> wins.
/// </para>
/// <para>
/// Every action also declares its responses with <c>[ProducesResponseType]</c>, because actions return <c>IActionResult</c> and the
/// OpenAPI generator cannot infer anything from it: without the attributes the contract has only an empty <c>200</c> and generated clients
/// (Refitter, Angular, Android, iOS) get no types. Declare them to match what <c>ToActionResult</c> actually returns:
/// </para>
/// <list type="bullet">
///   <item><description>success: <c>[ProducesResponseType&lt;TDto&gt;(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]</c> for a value,
///   <c>[ProducesResponseType&lt;CreatedResponse&gt;(StatusCodes.Status201Created, MediaTypeNames.Application.Json)]</c> for a create (a small <c>CreatedResponse(Guid Id)</c>
///   record in the Api project, as in the Knowledge service), or
///   <c>[ProducesResponseType(StatusCodes.Status204NoContent)]</c> for a command without a value;</description></item>
///   <item><description>errors as <c>application/problem+json</c> (<see cref="MediaTypeNames.Application.ProblemJson"/>):
///   <see cref="ValidationProblemDetails"/> for <c>400</c> (validation errors and invalid route/query/body values rejected by model
///   binding), <see cref="ProblemDetails"/> for <c>404</c>, <c>409</c> and <c>422</c>, and only the statuses the action can really return
///   (the <c>response</c> XML tags and the attributes must agree).</description></item>
/// </list>
/// <para>
/// <c>401</c> (no body, returned by the authentication middleware) and <c>403</c> (<see cref="ProblemDetails"/>, missing scope or user)
/// can happen for every action and are therefore declared once on this base class and inherited. Do not declare <c>400</c> here: an action
/// without parameters and without a validator cannot return it; declare it on the actions (or on a derived controller whose actions all
/// can return it). <c>[ProducesErrorResponseType(typeof(void))]</c> keeps <c>401</c> without a body (by default the API conventions would
/// document every error status without a type as <see cref="ProblemDetails"/>).
/// </para>
/// <para>
/// Do not put <c>[Produces("application/json")]</c> on controllers: at runtime it overwrites the <c>application/problem+json</c> content
/// type of error responses with <c>application/json</c>, and in the contract it adds an empty <c>application/json</c> body to every response
/// (including <c>204</c>). Name the content type on each success <c>[ProducesResponseType]</c> instead, as shown above.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [Route("v1/things")]
/// public sealed class ThingsController(ISender sender) : ServiceNameControllerBase
/// {
///     /// &lt;summary&gt;Renames a thing.&lt;/summary&gt;
///     /// ...
///     [HttpPut("{id:guid}/name")]
///     [ProducesResponseType(StatusCodes.Status204NoContent)]
///     [ProducesResponseType&lt;ValidationProblemDetails&gt;(StatusCodes.Status400BadRequest, MediaTypeNames.Application.ProblemJson)]
///     [ProducesResponseType&lt;ProblemDetails&gt;(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
///     public async Task&lt;IActionResult&gt; Rename(Guid id, RenameThingRequest request, CancellationToken cancellationToken) =&gt;
///         this.ToActionResult(await sender.Send(new RenameThing(id, request.Name), cancellationToken));
/// }
/// </code>
/// </example>
[ApiController]
[ProducesErrorResponseType(typeof(void))]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, MediaTypeNames.Application.ProblemJson)]
public abstract class ServiceNameControllerBase : ControllerBase;
