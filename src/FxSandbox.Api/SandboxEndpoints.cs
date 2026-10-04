using FxSandbox.Core;
using FxSandbox.Simulation;

namespace FxSandbox.Api;

public static class SandboxEndpoints
{
    public static IEndpointRouteBuilder MapSandboxEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/state", (SandboxEngine engine) => StateDto.From(engine.GetSnapshot()))
            .WithName("GetState");

        api.MapPost("/orders", PlaceOrder).WithName("PlaceOrder");

        api.MapDelete("/orders/{id:int}", CancelOrder).WithName("CancelOrder");

        api.MapPost("/reset", (SandboxEngine engine) => StateDto.From(engine.Reset())).WithName("Reset");

        return app;
    }

    private static IResult PlaceOrder(PlaceOrderRequest request, SandboxEngine engine)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.Pair is null) errors["pair"] = ["Pair is required."];
        if (request.Side is null) errors["side"] = ["Side is required."];
        if (request.Quantity <= 0) errors["quantity"] = ["Quantity must be greater than zero."];
        if (request.LimitPrice <= 0) errors["limitPrice"] = ["Limit price must be greater than zero."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var result = engine.Place(request.Pair!.Value, request.Side!.Value, request.Quantity, request.LimitPrice);
        if (result.Order is not { } placed)
            return Results.Problem(title: "Order rejected", detail: result.Error, statusCode: StatusCodes.Status400BadRequest);

        // Re-read so the response shows the order as it stands now (it may have filled on placement).
        var current = engine.GetSnapshot().Portfolio.Orders.Single(o => o.Id == placed.Id);
        return Results.Created($"/api/orders/{current.Id}", current);
    }

    private static IResult CancelOrder(int id, SandboxEngine engine)
    {
        switch (engine.Cancel(id))
        {
            case CancelResult.Cancelled:
                return Results.Ok(engine.GetSnapshot().Portfolio.Orders.Single(o => o.Id == id));
            case CancelResult.NotFound:
                return Results.Problem(
                    title: "Order not found",
                    detail: $"Order {id} does not exist.",
                    statusCode: StatusCodes.Status404NotFound);
            default:
                return Results.Problem(
                    title: "Order cannot be cancelled",
                    detail: $"Order {id} is not pending.",
                    statusCode: StatusCodes.Status409Conflict);
        }
    }
}
