using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SpaceAutomation.Game;
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
    internal static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web)
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
        app.MapGet("/state", () => GetState(session));
        app.MapGet("/objects/{id}", (string id) => GetObject(session, id));
        app.MapPost("/objects/{id}/move", (string id, HttpContext context) => MoveRover(session, id, context));
        app.MapPost("/session/pause", () => PauseSession(session));
        app.MapPost("/session/resume", () => ResumeSession(session));
        app.MapPost("/session/step", () => StepSession(session));
        app.MapPost("/session/save", () => SaveSession(session));
    }

    private static IResult GetState(GameSession session)
    {
        return Results.Json(session.State, Json);
    }

    private static IResult GetObject(GameSession session, string id)
    {
        var state = session.State;
        foreach (var projection in state.Objects)
        {
            if (!projection.TryGetValue("id", out var objectId))
            {
                continue;
            }

            if ((string?)objectId == id)
            {
                return Results.Json(projection, Json);
            }
        }

        return Results.Json(CommandResult.Reject("unknown_object"), Json, statusCode: 404);
    }

    private static async Task<IResult> MoveRover(GameSession session, string id, HttpContext context)
    {
        MoveRequest? body;
        try
        {
            body = await JsonSerializer.DeserializeAsync<MoveRequest>(context.Request.Body, Json);
        }
        catch (JsonException)
        {
            return Results.Json(CommandResult.Reject("invalid_body"), Json, statusCode: 400);
        }

        if (body is null)
        {
            return Results.Json(CommandResult.Reject("invalid_body"), Json, statusCode: 400);
        }

        if (body.Direction is null ||
            body.Direction.X is null ||
            body.Direction.Y is null)
        {
            return Results.Json(CommandResult.Reject("invalid_direction"), Json);
        }

        if (body.Speed is null)
        {
            return Results.Json(CommandResult.Reject("invalid_speed"), Json);
        }

        float x = body.Direction.X.Value;
        float y = body.Direction.Y.Value;
        float speed = body.Speed.Value;
        var direction = new Vector2(x, y);

        // Look up the object and execute its command together on the game thread.
        return await session.Call<IResult>(world =>
        {
            GameObject? obj = world.Find<GameObject>(id);
            if (obj is null)
            {
                return Results.Json(CommandResult.Reject("unknown_object"), Json, statusCode: 404);
            }

            Rover? rover = obj as Rover;
            if (rover is null)
            {
                return Results.Json(CommandResult.Reject("unsupported_object"), Json, statusCode: 400);
            }

            CommandResult result = rover.Move(direction, speed);
            return Results.Json(result, Json);
        });
    }

    private static IResult PauseSession(GameSession session)
    {
        session.Pause();
        return Results.Json(CommandResult.Ok(), Json);
    }

    private static IResult ResumeSession(GameSession session)
    {
        session.Resume();
        return Results.Json(CommandResult.Ok(), Json);
    }

    private static async Task<IResult> StepSession(GameSession session)
    {
        CommandResult result = await session.Step();
        return Results.Json(result, Json);
    }

    private static IResult SaveSession(GameSession session)
    {
        session.Save();
        return Results.Json(CommandResult.Ok(), Json);
    }
}

// Nullable properties distinguish missing fields from valid zero values.
// The route handler explicitly reads and validates the JSON request body.
public sealed record VectorBody(float? X, float? Y);

public sealed record MoveRequest(VectorBody? Direction, float? Speed);
