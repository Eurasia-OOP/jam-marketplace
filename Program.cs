using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using Serilog;
using SushiMarketplace.Api.Contracts;
using SushiMarketplace.Api.Data;
using SushiMarketplace.Api.Domain;
using SushiMarketplace.Api.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog((context, configuration) => configuration.ReadFrom.Configuration(context.Configuration)
    .WriteTo.Console());
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddDbContext<MarketplaceDbContext>(options => options.UseNpgsql(
    builder.Configuration.GetConnectionString("Marketplace") ??
    "Host=localhost;Port=5432;Database=marketplace;Username=marketplace;Password=marketplace"));
builder.Services.AddSingleton<OrderStateMachine>();

var app = builder.Build();
app.UseExceptionHandler();
app.UseStatusCodePages();
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
    await db.Database.MigrateAsync();
    if (!await db.Products.AnyAsync())
    {
        db.Products.AddRange(
            new Product { Id = Guid.Parse("11111111-1111-4111-8111-111111111111"), Name = "Salmon Roll", Price = 12.50m },
            new Product { Id = Guid.Parse("22222222-2222-4222-8222-222222222222"), Name = "Tuna Nigiri", Price = 8.00m },
            new Product { Id = Guid.Parse("33333333-3333-4333-8333-333333333333"), Name = "Avocado Maki", Price = 7.25m });
        await db.SaveChangesAsync();
    }
}
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapGet("/health", () => Results.Ok(new { status = "ok" })).WithTags("System");

app.MapGet("/products", async (MarketplaceDbContext db) =>
    Results.Ok(await db.Products.AsNoTracking().OrderBy(p => p.Name).ToListAsync()))
    .WithTags("Products").WithSummary("List products");

app.MapPost("/orders", async (CreateOrderRequest request, MarketplaceDbContext db, CancellationToken ct) =>
{
    if (request.Items is null || request.Items.Count == 0 || request.Items.Any(x => x.Quantity <= 0))
        return Problem(400, "Invalid order", "An order needs at least one item and every quantity must be positive.");

    var requested = request.Items.GroupBy(x => x.ProductId).Select(g => new { ProductId = g.Key, Quantity = g.Sum(x => x.Quantity) }).ToList();
    var products = await db.Products.Where(p => requested.Select(x => x.ProductId).Contains(p.Id) && p.IsAvailable).ToDictionaryAsync(p => p.Id, ct);
    if (products.Count != requested.Count)
        return Problem(400, "Invalid products", "One or more products do not exist or are unavailable.");

    var order = new Order();
    foreach (var item in requested)
    {
        var product = products[item.ProductId];
        order.Items.Add(new OrderLine { OrderId = order.Id, ProductId = product.Id, ProductName = product.Name, UnitPrice = product.Price, Quantity = item.Quantity });
    }
    order.Events.Add(new OrderEvent { OrderId = order.Id, Type = "Received", FromStatus = OrderStatus.Received, ToStatus = OrderStatus.Received });
    db.Orders.Add(order);
    await db.SaveChangesAsync(ct);
    return Results.Created($"/orders/{order.Id}", ToOrderResponse(order));
}).WithTags("Orders").WithSummary("Submit an order");

app.MapGet("/orders/{id:guid}", async (Guid id, MarketplaceDbContext db, CancellationToken ct) =>
{
    var order = await db.Orders.AsNoTracking().Include(o => o.Items).Include(o => o.Events)
        .SingleOrDefaultAsync(o => o.Id == id, ct);
    return order is null ? Problem(404, "Order not found", $"Order {id} was not found.") : Results.Ok(ToOrderResponse(order));
}).WithTags("Orders").WithSummary("Get an order and its replayable event history");

foreach (var action in new[] { "pay", "pack", "ship" })
{
    var routeAction = action;
    app.MapPost($"/orders/{{id:guid}}/{routeAction}", async (Guid id, HttpRequest request, MarketplaceDbContext db,
        OrderStateMachine stateMachine, CancellationToken ct) =>
    {
        var key = request.Headers["Idempotency-Key"].FirstOrDefault()?.Trim();
        if (key is { Length: > 200 }) return Problem(400, "Invalid idempotency key", "Idempotency-Key must be at most 200 characters.");

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var order = await db.Orders.FromSqlInterpolated($"SELECT * FROM \"Orders\" WHERE \"Id\" = {id} FOR UPDATE")
            .SingleOrDefaultAsync(ct);
        if (order is null) return Problem(404, "Order not found", $"Order {id} was not found.");

        if (!string.IsNullOrEmpty(key))
        {
            var prior = await db.OrderEvents.AsNoTracking().SingleOrDefaultAsync(e => e.OrderId == id && e.IdempotencyKey == key, ct);
            if (prior is not null)
            {
                if (prior.Type != routeAction) return Problem(409, "Idempotency key conflict", "This key was already used for a different order transition.");
                await transaction.CommitAsync(ct);
                return Results.Ok(new { orderId = id, status = prior.ToStatus.ToString(), replayed = true, eventId = prior.Id });
            }
        }

        if (!stateMachine.TryGetNext(order.Status, routeAction, out var next))
            return Problem(409, "Invalid order transition", $"Cannot {routeAction} an order in state {order.Status}. Allowed next state is defined by the order state machine.");

        var previous = order.Status;
        order.Status = next;
        var orderEvent = new OrderEvent { OrderId = order.Id, Type = routeAction, FromStatus = previous, ToStatus = next, IdempotencyKey = string.IsNullOrEmpty(key) ? null : key };
        db.OrderEvents.Add(orderEvent);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.Ok(new { orderId = id, status = next.ToString(), replayed = false, eventId = orderEvent.Id });
    }).WithTags("Order transitions").WithSummary($"Transition an order using {routeAction}");
}

app.Run();

static IResult Problem(int status, string title, string detail) => Results.Problem(statusCode: status, title: title, detail: detail);

static object ToOrderResponse(Order order) => new
{
    order.Id,
    status = order.Status.ToString(),
    order.CreatedAt,
    items = order.Items.Select(i => new { i.ProductId, i.ProductName, i.UnitPrice, i.Quantity }),
    total = order.Items.Sum(i => i.UnitPrice * i.Quantity),
    events = order.Events.OrderBy(e => e.Id).Select(e => new
    {
        e.Id,
        e.Type,
        from = e.FromStatus.ToString(),
        to = e.ToStatus.ToString(),
        e.OccurredAt
    })
};

public partial class Program;
