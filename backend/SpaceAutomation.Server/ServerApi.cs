using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SpaceAutomation.Game;
using SpaceAutomation.Game.Energy;
using SpaceAutomation.Game.Objects;

namespace SpaceAutomation.Server;

// The player-facing HTTP surface: the whole game. Reads serve the published
// snapshot; writes run as calls on the game thread and answer with the same
// CommandResult values the domain produces — acceptance is synchronous,
// completion happens over simulation time. Binds Kestrel to localhost only.
public static class ServerApi
{
    // Snake-case rejection fields with null reasons omitted: {"accepted":true}
    // or {"accepted":false,"reason":"invalid_speed"}.
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static WebApplication Build(GameSession session, int port)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders(); // the server speaks through its own prints
        builder.WebHost.ConfigureKestrel(server => server.Listen(System.Net.IPAddress.Loopback, port));
        var app = builder.Build();
        MapRoutes(app, session);
        return app;
    }

    private static void MapRoutes(WebApplication app, GameSession session)
    {
        app.MapGet("/state", () => Results.Json(session.State, Json));

        app.MapGet("/objects/{id}", (string id) =>
        {
            var projection = FindProjection(session, id);
            return projection is null
                ? Results.Json(CommandResult.Reject("unknown_object"), Json, statusCode: 404)
                : Results.Json(projection, Json);
        });

        app.MapGet("/objects/{id}/grid_status", (string id) =>
            WithObject<IEnergyNode>(session, id, async node =>
            {
                var report = await session.Call(_ => node.GridStatus());
                return Results.Json(StateProjection.Encode(report), Json);
            }));

        app.MapPost("/objects/{id}/move", (string id, MoveRequest? body) =>
            WithObject<Rover>(session, id, async rover =>
            {
                if (body is null)
                    return Results.Json(CommandResult.Reject("invalid_body"), Json, statusCode: 400);

                if (body.Direction is not { X: { } x, Y: { } y })
                    return Results.Json(CommandResult.Reject("invalid_direction"), Json);

                if (body.Speed is not { } speed)
                    return Results.Json(CommandResult.Reject("invalid_speed"), Json);

                return Results.Json(await session.Call(_ => rover.Move(new Vector2(x, y), speed)), Json);
            }));

        app.MapPost("/objects/{id}/connect", (string id, ConnectRequest? body) =>
            WithObject<IEnergyNode>(session, id, async node =>
            {
                if (body is null)
                    return Results.Json(CommandResult.Reject("invalid_body"), Json, statusCode: 400);

                if (body.Target is not { } target)
                    return Results.Json(CommandResult.Reject("invalid_energy_endpoint"), Json);

                return Results.Json(await session.Call(_ => node.Connect(target)), Json);
            }));

        app.MapPost("/objects/{id}/disconnect", (string id, ConnectRequest? body) =>
            WithObject<IEnergyNode>(session, id, async node =>
            {
                if (body is null)
                    return Results.Json(CommandResult.Reject("invalid_body"), Json, statusCode: 400);

                if (body.Target is not { } target)
                    return Results.Json(CommandResult.Reject("invalid_energy_endpoint"), Json);

                return Results.Json(await session.Call(_ => node.Disconnect(target)), Json);
            }));

        app.MapPost("/objects/{id}/replace_component", (string id, ReplaceComponentRequest? body) =>
            WithObject<Rover>(session, id, async rover =>
            {
                if (body is null)
                    return Results.Json(CommandResult.Reject("invalid_body"), Json, statusCode: 400);

                if (body.Slot is not { } slot)
                    return Results.Json(CommandResult.Reject("invalid_slot"), Json);

                // A null component removes the part from that slot.
                return Results.Json(await session.Call(_ => rover.ReplaceComponent(slot, body.Component)), Json);
            }));

        app.MapPost("/objects/{id}/set_enabled", (string id, SetEnabledRequest? body) =>
            WithObject<SolarPanel>(session, id, async panel =>
            {
                if (body is null)
                    return Results.Json(CommandResult.Reject("invalid_body"), Json, statusCode: 400);

                if (body.Enabled is not { } enabled)
                    return Results.Json(CommandResult.Reject("invalid_enabled"), Json);

                return Results.Json(await session.Call(_ => panel.SetEnabled(enabled)), Json);
            }));

        app.MapPost("/session/pause", () => { session.Pause(); return Results.Json(CommandResult.Ok(), Json); });

        app.MapPost("/session/resume", () => { session.Resume(); return Results.Json(CommandResult.Ok(), Json); });

        app.MapPost("/session/step", async () => Results.Json(await session.Step(), Json));

        app.MapPost("/session/save", () => { session.Save(); return Results.Json(CommandResult.Ok(), Json); });
    }

    // Resolves the target on the game thread, then hands the typed object to
    // the route's own call. Unknown ids are 404s; objects that do not support
    // the command answer 400 with the same rejection shape as gameplay rules.
    private static async Task<IResult> WithObject<T>(GameSession session, string id, Func<T, Task<IResult>> action)
        where T : class
    {
        var target = await session.Call(world => world.Find<GameObject>(id));

        if (target is null)
            return Results.Json(CommandResult.Reject("unknown_object"), Json, statusCode: 404);

        if (target is not T typed)
            return Results.Json(CommandResult.Reject("unsupported_object"), Json, statusCode: 400);

        return await action(typed);
    }

    private static IReadOnlyDictionary<string, object?>? FindProjection(GameSession session, string id) =>
        session.State.Objects.FirstOrDefault(fields => fields.TryGetValue("id", out var objectId) && (string?)objectId == id);
}

// Request bodies are plain data: nullable fields distinguish "absent" from
// "wrong type". BindAsync reads the body as JSON whatever the Content-Type
// header says, so plain `curl -d` works without -H 'Content-Type: application/json';
// malformed JSON becomes a null body, which routes reject as invalid_body.
public sealed record VectorBody(double? X, double? Y);

public sealed record MoveRequest(VectorBody? Direction, double? Speed)
{
    public static async ValueTask<MoveRequest?> BindAsync(HttpContext context)
    {
        try { return await JsonSerializer.DeserializeAsync<MoveRequest>(context.Request.Body, ServerApi.Json); }
        catch (JsonException) { return null; }
    }
}

public sealed record ConnectRequest(string? Target)
{
    public static async ValueTask<ConnectRequest?> BindAsync(HttpContext context)
    {
        try { return await JsonSerializer.DeserializeAsync<ConnectRequest>(context.Request.Body, ServerApi.Json); }
        catch (JsonException) { return null; }
    }
}

public sealed record ReplaceComponentRequest(string? Slot, string? Component)
{
    public static async ValueTask<ReplaceComponentRequest?> BindAsync(HttpContext context)
    {
        try { return await JsonSerializer.DeserializeAsync<ReplaceComponentRequest>(context.Request.Body, ServerApi.Json); }
        catch (JsonException) { return null; }
    }
}

public sealed record SetEnabledRequest(bool? Enabled)
{
    public static async ValueTask<SetEnabledRequest?> BindAsync(HttpContext context)
    {
        try { return await JsonSerializer.DeserializeAsync<SetEnabledRequest>(context.Request.Body, ServerApi.Json); }
        catch (JsonException) { return null; }
    }
}
