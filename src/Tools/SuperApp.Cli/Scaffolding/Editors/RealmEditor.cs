using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SuperApp.Cli.Scaffolding.Editors;

/// <summary>Edits the local CIAM realm <c>deploy/local/keycloak/realm-superapp.json</c> (ADR-0031): clients and client scopes.</summary>
/// <remarks>
/// <para>
/// The realm is the local model of what the CIAM team configures (ADR-0016). A domain service gets a resource client
/// <c>{service}-api</c> (its audience), a client-credentials client <c>{service}-client</c> for system calls (ADR-0042) and one client
/// scope per permission with an audience mapper to <c>{service}-api</c>, offered as optional scopes to the clients of the module
/// (<c>bff-web</c>, <c>mobile-android</c>, <c>mobile-ios</c>, <c>dev-cli</c>). A BFF gets the default scope <c>{experience}-bff-audience</c>
/// and the optional scope <c>{experience}.internal.read</c> for <c>dev-cli</c> (ADR-0039, ADR-0040).
/// </para>
/// <para>The local client secret is a development value of the realm, like the other local secrets; real secrets come from Vault.</para>
/// </remarks>
internal static class RealmEditor
{
    /// <summary>Path of the realm.</summary>
    public const string Path = "deploy/local/keycloak/realm-superapp.json";

    private static readonly string[] ModuleClients = ["bff-web", "mobile-android", "mobile-ios", "dev-cli"];

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Adds the clients and scopes of a domain service.</summary>
    /// <param name="content">Content of the realm.</param>
    /// <param name="key">Lower-case service name.</param>
    /// <param name="scopes">Full scope names, e.g. <c>billing.invoice.read</c>.</param>
    /// <returns>The new content.</returns>
    public static string AddService(string content, string key, IReadOnlyList<string> scopes) => Edit(content, realm =>
    {
        var clients = realm["clients"]!.AsArray();
        AddClient(clients, ResourceClient($"{key}-api"));
        AddClient(clients, ServiceAccountClient($"{key}-client", key));
        var clientScopes = realm["clientScopes"]!.AsArray();
        foreach (var scope in scopes)
        {
            AddScope(clientScopes, Scope(scope, $"Scope serwisu ({key}-api)", $"{key}-api", inToken: true));
            foreach (var client in ModuleClients)
            {
                AddToList(clients, client, "optionalClientScopes", scope);
            }
        }
    });

    /// <summary>Removes the clients and every scope with the service's prefix.</summary>
    /// <param name="content">Content of the realm.</param>
    /// <param name="key">Lower-case service name.</param>
    /// <returns>The new content.</returns>
    public static string RemoveService(string content, string key) => Edit(content, realm =>
    {
        var clients = realm["clients"]!.AsArray();
        RemoveWhere(clients, client => (string?)client?["clientId"] is { } id && (id == $"{key}-api" || id == $"{key}-client"));
        RemoveScopes(realm, name => name.StartsWith($"{key}.", StringComparison.Ordinal));
    });

    /// <summary>Removes one scope of a service from the client scopes and from every client.</summary>
    /// <param name="content">Content of the realm.</param>
    /// <param name="scope">Full scope name, e.g. <c>billing.invoice.read</c>.</param>
    /// <returns>The new content.</returns>
    public static string RemoveScope(string content, string scope) => Edit(content, realm => RemoveScopes(realm, name => name == scope));

    /// <summary>Adds the audience scope and the internal API scope of a BFF.</summary>
    /// <param name="content">Content of the realm.</param>
    /// <param name="key">Lower-case experience name.</param>
    /// <param name="name">Experience name in PascalCase, for descriptions.</param>
    /// <returns>The new content.</returns>
    public static string AddBff(string content, string key, string name) => Edit(content, realm =>
    {
        var clients = realm["clients"]!.AsArray();
        var clientScopes = realm["clientScopes"]!.AsArray();
        AddScope(clientScopes, Scope($"{key}-bff-audience", $"Audience BFF experience {name} (ADR-0038, ADR-0040)", $"{key}-bff", inToken: false));
        AddScope(clientScopes, Scope($"{key}.internal.read", $"API wewnętrzne BFF experience {name} (ADR-0039)", $"{key}-bff", inToken: true));
        foreach (var client in ModuleClients)
        {
            AddToList(clients, client, "defaultClientScopes", $"{key}-bff-audience");
        }

        AddToList(clients, "dev-cli", "optionalClientScopes", $"{key}.internal.read");
    });

    /// <summary>Removes the scopes of a BFF.</summary>
    /// <param name="content">Content of the realm.</param>
    /// <param name="key">Lower-case experience name.</param>
    /// <returns>The new content.</returns>
    public static string RemoveBff(string content, string key) => Edit(content, realm =>
        RemoveScopes(realm, scope => scope == $"{key}-bff-audience" || scope.StartsWith($"{key}.internal.", StringComparison.Ordinal)));

    private static string Edit(string content, Action<JsonObject> change)
    {
        var realm = JsonNode.Parse(content)?.AsObject() ?? throw new ScaffoldException($"{Path} is not a JSON object.");
        var before = realm.ToJsonString(WriteOptions);
        change(realm);
        var after = realm.ToJsonString(WriteOptions);
        if (after == before)
        {
            return content;
        }

        // Keep the line endings of the file: a checkout with core.autocrlf has CRLF, the serializer writes the platform's.
        var newLine = content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        return after.ReplaceLineEndings(newLine) + newLine;
    }

    private static void AddClient(JsonArray clients, JsonObject client)
    {
        if (!clients.Any(existing => (string?)existing?["clientId"] == (string?)client["clientId"]))
        {
            clients.Add(client);
        }
    }

    private static void AddScope(JsonArray scopes, JsonObject scope)
    {
        if (!scopes.Any(existing => (string?)existing?["name"] == (string?)scope["name"]))
        {
            scopes.Add(scope);
        }
    }

    private static void AddToList(JsonArray clients, string clientId, string list, string scope)
    {
        if (clients.FirstOrDefault(client => (string?)client?["clientId"] == clientId)?[list] is JsonArray values
            && !values.Any(value => (string?)value == scope))
        {
            values.Add(scope);
        }
    }

    private static void RemoveScopes(JsonObject realm, Func<string, bool> matches)
    {
        RemoveWhere(realm["clientScopes"]!.AsArray(), scope => (string?)scope?["name"] is { } name && matches(name));
        if (realm["scopeMappings"] is JsonArray mappings)
        {
            RemoveWhere(mappings, mapping => (string?)mapping?["clientScope"] is { } name && matches(name));
        }

        foreach (var client in realm["clients"]!.AsArray().OfType<JsonObject>())
        {
            foreach (var list in (string[])["defaultClientScopes", "optionalClientScopes"])
            {
                if (client[list] is JsonArray values)
                {
                    RemoveWhere(values, value => (string?)value is { } name && matches(name));
                }
            }
        }
    }

    private static void RemoveWhere(JsonArray array, Func<JsonNode?, bool> matches)
    {
        foreach (var item in array.Where(matches).ToList())
        {
            array.Remove(item);
        }
    }

    private static JsonObject Scope(string name, string description, string audience, bool inToken) => new()
    {
        ["name"] = name,
        ["description"] = description,
        ["protocol"] = "openid-connect",
        ["attributes"] = new JsonObject { ["include.in.token.scope"] = inToken ? "true" : "false", ["display.on.consent.screen"] = "false" },
        ["protocolMappers"] = new JsonArray(new JsonObject
        {
            ["name"] = $"audience {audience}",
            ["protocol"] = "openid-connect",
            ["protocolMapper"] = "oidc-audience-mapper",
            ["consentRequired"] = false,
            ["config"] = new JsonObject
            {
                ["id.token.claim"] = "false",
                ["access.token.claim"] = "true",
                ["introspection.token.claim"] = "true",
                ["included.custom.audience"] = audience,
                ["userinfo.token.claim"] = "false",
            },
        }),
    };

    private static JsonObject ResourceClient(string clientId) => Client(clientId, "Resource server (audience); bez przepływów OAuth", serviceAccount: false, secret: null);

    private static JsonObject ServiceAccountClient(string clientId, string key) =>
        Client(clientId, "Client credentials do wywołań systemowych (ADR-0040, ADR-0042)", serviceAccount: true, secret: $"dev-{key}-client-secret");

    private static JsonObject Client(string clientId, string description, bool serviceAccount, string? secret)
    {
        var client = new JsonObject
        {
            ["clientId"] = clientId,
            ["name"] = clientId,
            ["description"] = description,
            ["enabled"] = true,
            ["clientAuthenticatorType"] = "client-secret",
        };
        if (secret is not null)
        {
            client["secret"] = secret;
        }

        client["redirectUris"] = new JsonArray();
        client["webOrigins"] = new JsonArray();
        client["bearerOnly"] = false;
        client["consentRequired"] = false;
        client["standardFlowEnabled"] = false;
        client["implicitFlowEnabled"] = false;
        client["directAccessGrantsEnabled"] = false;
        client["serviceAccountsEnabled"] = serviceAccount;
        client["publicClient"] = false;
        client["frontchannelLogout"] = false;
        client["protocol"] = "openid-connect";
        client["fullScopeAllowed"] = true;
        client["defaultClientScopes"] = new JsonArray([.. (serviceAccount ? ["service_account"] : Array.Empty<string>()).Concat(["web-origins", "acr", "roles", "profile", "basic", "email"]).Select(value => (JsonNode)value)]);
        client["optionalClientScopes"] = new JsonArray([.. ((string[])["address", "phone", "organization", "offline_access", "microprofile-jwt"]).Select(value => (JsonNode)value)]);
        return client;
    }
}
