using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FxSandbox.Simulation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FxSandbox.Api.Tests;

public sealed class SandboxApiTests : IClassFixture<SandboxApiTests.Factory>
{
    // Far from the seed rates, so a Buy at High is marketable and a Buy at Low stays pending.
    private const decimal High = 5m;
    private const decimal Low = 0.01m;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly Factory _factory;
    private readonly HttpClient _client;

    public SandboxApiTests(Factory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _client.PostAsync("/api/reset", null).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task GetState_returns_starting_capital_rates_and_history()
    {
        var state = await GetState();

        Assert.Equal(10_000m, state.Cash);
        Assert.Equal(10_000m, state.Equity);
        Assert.Equal(0m, state.UnrealisedPnl);
        Assert.Empty(state.Orders);
        Assert.Empty(state.Positions);
        Assert.Equal(3, state.Rates.Count);
        var eur = Assert.Single(state.Rates, r => r.Pair == "UsdEur");
        Assert.Equal(0.8885m, eur.Rate);
        Assert.Equal([0.8885m], eur.History);
    }

    [Fact]
    public async Task Place_pending_order_returns_201_and_appears_in_state()
    {
        var response = await Place("UsdEur", "Buy", 1000m, Low);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var order = await response.Content.ReadFromJsonAsync<OrderDto>(Json);
        Assert.Equal(1001, order!.Id);
        Assert.Equal("Pending", order.Status);
        var state = await GetState();
        Assert.Equal(1001, Assert.Single(state.Orders).Id);
        Assert.Empty(state.Positions);
    }

    [Fact]
    public async Task Place_marketable_order_fills_at_market_and_opens_position()
    {
        var response = await Place("UsdEur", "Buy", 1000m, High);

        var order = await response.Content.ReadFromJsonAsync<OrderDto>(Json);
        Assert.Equal("Filled", order!.Status);
        var position = Assert.Single((await GetState()).Positions);
        Assert.Equal("UsdEur", position.Pair);
        Assert.Equal("Long", position.Direction);
        Assert.Equal(1000m, position.Quantity);
        Assert.Equal(0.8885m, position.EntryPrice); // the seed rate, not the limit
    }

    [Theory]
    [InlineData(0, 0.9, "quantity")]
    [InlineData(-5, 0.9, "quantity")]
    [InlineData(100, 0, "limitPrice")]
    [InlineData(100, -1, "limitPrice")]
    public async Task Place_with_invalid_numbers_returns_400_validation_problem(
        decimal quantity, decimal limit, string field)
    {
        var response = await Place("UsdEur", "Buy", quantity, limit);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(Json);
        Assert.Contains(field, problem!.Errors.Keys);
    }

    [Fact]
    public async Task Place_with_missing_pair_returns_400()
    {
        var response = await _client.PostAsJsonAsync("/api/orders", new { side = "Buy", quantity = 100, limitPrice = 0.9 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Place_with_unknown_pair_returns_400()
    {
        var response = await Place("UsdXyz", "Buy", 100m, 0.9m);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Place_over_capital_limit_returns_400_problem()
    {
        var response = await Place("UsdEur", "Buy", 10_000.01m, Low);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(Json);
        Assert.Equal("Order rejected", problem!.Title);
        Assert.Empty((await GetState()).Orders);
    }

    [Fact]
    public async Task Cancel_pending_order_returns_200_and_marks_it_cancelled()
    {
        await Place("UsdEur", "Buy", 100m, Low);

        var response = await _client.DeleteAsync("/api/orders/1001");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Cancelled", Assert.Single((await GetState()).Orders).Status);
    }

    [Fact]
    public async Task Cancel_filled_order_returns_409()
    {
        await Place("UsdEur", "Buy", 100m, High);

        var response = await _client.DeleteAsync("/api/orders/1001");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Cancel_cancelled_order_returns_409()
    {
        await Place("UsdEur", "Buy", 100m, Low);
        await _client.DeleteAsync("/api/orders/1001");

        var response = await _client.DeleteAsync("/api/orders/1001");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Cancel_unknown_order_returns_404()
    {
        var response = await _client.DeleteAsync("/api/orders/9999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Reset_clears_orders_and_positions_and_restarts_order_ids()
    {
        await Place("UsdEur", "Buy", 100m, High);
        await Place("UsdGbp", "Buy", 100m, Low);

        var response = await _client.PostAsync("/api/reset", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var state = await response.Content.ReadFromJsonAsync<StateDto>(Json);
        Assert.Empty(state!.Orders);
        Assert.Empty(state.Positions);
        Assert.Equal(10_000m, state.Cash);
        var next = await (await Place("UsdEur", "Buy", 100m, Low)).Content.ReadFromJsonAsync<OrderDto>(Json);
        Assert.Equal(1001, next!.Id);
    }

    [Fact]
    public async Task Tick_updates_state_rates()
    {
        _factory.Services.GetRequiredService<SandboxEngine>().Tick();

        var state = await GetState();

        Assert.All(state.Rates, r => Assert.Equal(2, r.History.Count));
    }

    [Fact]
    public async Task Preflight_from_vite_origin_is_allowed()
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/orders");
        request.Headers.Add("Origin", "http://localhost:5173");
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "content-type");

        var response = await _client.SendAsync(request);

        Assert.True(response.IsSuccessStatusCode);
        Assert.Equal("http://localhost:5173", response.Headers.GetValues("Access-Control-Allow-Origin").Single());
    }

    private Task<HttpResponseMessage> Place(string pair, string side, decimal quantity, decimal limitPrice) =>
        _client.PostAsJsonAsync("/api/orders", new { pair, side, quantity, limitPrice });

    private async Task<StateDto> GetState() =>
        (await _client.GetFromJsonAsync<StateDto>("/api/state", Json))!;

    private sealed record OrderDto(int Id, string Pair, string Side, decimal Quantity, decimal LimitPrice, string Status);

    private sealed record RateDto(string Pair, decimal Rate, List<decimal> History);

    private sealed record PositionDto(string Pair, string Direction, decimal Quantity, decimal EntryPrice);

    private sealed record StateDto(
        decimal Cash,
        decimal Equity,
        decimal UnrealisedPnl,
        List<RateDto> Rates,
        List<PositionDto> Positions,
        List<OrderDto> Orders);

    /// <summary>Hosts the API without the background timer, so tests control when ticks happen.</summary>
    public sealed class Factory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder) =>
            builder.ConfigureServices(services =>
            {
                var tick = services.Single(d => d.ImplementationType == typeof(TickService));
                services.Remove(tick);
            });
    }
}
