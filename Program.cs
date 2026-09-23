using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using SushiMarketplace.Api.Contracts;
using SushiMarketplace.Api.Data;
using SushiMarketplace.Api.Domain;
using SushiMarketplace.Api.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<MarketplaceDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Marketplace") ?? "Data Source=sushi-marketplace.db"));
builder.Services.AddSingleton<OrderStateMachine>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

// Seed the reference menu and keep existing product IDs stable for existing orders.
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
    await db.Database.EnsureCreatedAsync();
    var menu = new[]
    {
        new Product { Name = "Maguro nigiri", Description = "Lean tuna over seasoned rice. Two pieces.", Price = 6.20m, ImageUrl = "/images/maguro-nigiri.jpg" },
        new Product { Name = "Sake nigiri", Description = "Farmed salmon, clean fat line. Two pieces.", Price = 5.80m, ImageUrl = "/images/sake-nigiri.jpg" },
        new Product { Name = "Hamachi nigiri", Description = "Yellowtail with a buttery finish. Two pieces.", Price = 6.40m, ImageUrl = "/images/hamachi-nigiri.jpg" },
        new Product { Name = "Spicy tuna roll", Description = "Chopped tuna, chili oil, toasted sesame. Six pieces.", Price = 8.90m, ImageUrl = "/images/spicy-tuna-roll.jpg" },
        new Product { Name = "Dragon roll", Description = "Eel and cucumber, avocado roof, unagi glaze. Eight pieces.", Price = 12.90m, ImageUrl = "/images/dragon-roll.jpg" },
        new Product { Name = "Salmon sashimi", Description = "Five slices, daikon, no rice.", Price = 9.90m, ImageUrl = "/images/salmon-sashimi.jpg" },
        new Product { Name = "Salted edamame", Description = "Blanched soybeans, sea salt.", Price = 3.50m, ImageUrl = "/images/salted-edamame.jpg" },
        new Product { Name = "Pan-seared gyoza", Description = "Six dumplings, crisp skirt, soy-vinegar.", Price = 5.90m, ImageUrl = "/images/pan-seared-gyoza.jpg" }
    };
    var existing = await db.Products.OrderBy(p => p.Id).ToListAsync();
    if (existing.Count == 0)
    {
        db.Products.AddRange(menu);
    }
    else
    {
        // Migrate only the original demo names once. Later catalog edits are left alone.
        var legacyNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Salmon Nigiri"] = "Maguro nigiri",
            ["Tuna Roll"] = "Sake nigiri",
            ["Avocado Maki"] = "Hamachi nigiri",
            ["California Roll"] = "Spicy tuna roll"
        };
        foreach (var product in existing)
        {
            if (!legacyNames.TryGetValue(product.Name, out var newName)) continue;
            var seeded = menu.Single(item => item.Name == newName);
            product.Name = seeded.Name;
            product.Description = seeded.Description;
            product.Price = seeded.Price;
            product.ImageUrl = seeded.ImageUrl;
        }

        var menuByName = menu.ToDictionary(product => product.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var product in existing)
        {
            if (menuByName.TryGetValue(product.Name, out var seeded) &&
                product.ImageUrl.StartsWith("https://images.unsplash.com/", StringComparison.OrdinalIgnoreCase))
            {
                // Move the old remote demo photos to the local copies without resetting other edits.
                product.ImageUrl = seeded.ImageUrl;
            }
        }

        var existingNames = existing.Select(product => product.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        db.Products.AddRange(menu.Where(product => !existingNames.Contains(product.Name)));
    }
    await db.SaveChangesAsync();
}

app.MapGet("/openapi/v1.json", (IWebHostEnvironment environment) =>
    Results.File(Path.Combine(environment.ContentRootPath, "openapi.json"), "application/json"));
app.MapScalarApiReference();
app.MapGet("/api/products", async (MarketplaceDbContext db) =>
    Results.Ok(await db.Products.Where(p => p.IsAvailable).OrderBy(p => p.Id).ToListAsync()))
    .WithName("ListProducts").WithSummary("List available sushi products");

app.MapPost("/api/orders", async Task<Results<Created<OrderView>, ValidationProblem>> (
    CreateOrderRequest request, HttpRequest http, MarketplaceDbContext db) =>
{
    if (request.Items is null || request.Items.Count == 0)
        return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["items"] = ["Add at least one product."] });
    if (request.Items.Any(i => i.Quantity is < 1 or > 99))
        return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["items"] = ["Quantity must be between 1 and 99."] });

    var key = http.Headers["Idempotency-Key"].FirstOrDefault()?.Trim();
    if (key is { Length: > 200 })
        return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["Idempotency-Key"] = ["The key must be 200 characters or fewer."] });
    if (!string.IsNullOrWhiteSpace(key))
    {
        var existing = await db.Orders.AsNoTracking().Include(o => o.Items).Include(o => o.Events)
            .SingleOrDefaultAsync(o => o.IdempotencyKey == key);
        if (existing is not null)
            return TypedResults.Created($"/api/orders/{existing.Id}", ToView(existing));
    }

    var quantities = request.Items.GroupBy(i => i.ProductId).ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity));
    var products = await db.Products.Where(p => p.IsAvailable && quantities.Keys.Contains(p.Id)).ToListAsync();
    if (products.Count != quantities.Count)
        return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["items"] = ["One or more products do not exist or are unavailable."] });

    var now = DateTimeOffset.UtcNow;
    var order = new Order
    {
        Id = Guid.NewGuid(), CreatedAt = now, Status = OrderStatus.Received, IdempotencyKey = key,
        Items = products.Select(p => new OrderItem { ProductId = p.Id, ProductName = p.Name, UnitPrice = p.Price, Quantity = quantities[p.Id] }).ToList()
    };
    order.Total = order.Items.Sum(i => i.UnitPrice * i.Quantity);
    order.Events.Add(new OrderEvent { Type = "OrderReceived", Status = OrderStatus.Received, OccurredAt = now, Details = "Order submitted" });
    db.Orders.Add(order);
    await db.SaveChangesAsync();
    return TypedResults.Created($"/api/orders/{order.Id}", ToView(order));
})
.WithName("CreateOrder").WithSummary("Submit an order; repeat the same Idempotency-Key to safely retrieve the original order");

app.MapGet("/api/orders", async (MarketplaceDbContext db) =>
{
    var orders = await db.Orders.AsNoTracking().Include(o => o.Items).Include(o => o.Events)
        .OrderByDescending(o => o.CreatedAt).ToListAsync();
    return Results.Ok(orders.Select(ToView));
}).WithName("ListOrders").WithSummary("List orders for the kitchen dashboard");

app.MapGet("/api/orders/{id:guid}", async Task<Results<Ok<OrderView>, NotFound>> (Guid id, MarketplaceDbContext db) =>
{
    var order = await db.Orders.AsNoTracking().Include(o => o.Items).Include(o => o.Events)
        .SingleOrDefaultAsync(o => o.Id == id);
    return order is null ? TypedResults.NotFound() : TypedResults.Ok(ToView(order));
}).WithName("GetOrder").WithSummary("Get the current order and its replayable event history");

MapTransition("pay", OrderStatus.Paid, "PaymentRecorded", "Payment simulated successfully");
MapTransition("pack", OrderStatus.Packed, "OrderPacked", "Order packed");
MapTransition("ship", OrderStatus.Shipped, "OrderShipped", "Order shipped");

app.Run();

void MapTransition(string action, OrderStatus target, string eventType, string details)
{
    app.MapPost("/api/orders/{id:guid}/" + action, async Task<IResult> (
        Guid id, MarketplaceDbContext db, OrderStateMachine stateMachine) =>
    {
        var order = await db.Orders.Include(o => o.Items).Include(o => o.Events).SingleOrDefaultAsync(o => o.Id == id);
        if (order is null) return Results.NotFound();
        if (!stateMachine.TryTransition(order.Status, target))
        {
            var next = OrderStateMachine.Next(order.Status)?.ToString() ?? "no further transition";
            return Results.Problem(
                title: "Invalid order transition",
                detail: $"This order is {order.Status}. Action '{action}' requires the preceding state; the next allowed state is {next}.",
                statusCode: StatusCodes.Status409Conflict,
                type: "https://httpstatuses.com/409",
                extensions: new Dictionary<string, object?> { ["orderId"] = order.Id, ["currentStatus"] = order.Status.ToString(), ["requestedStatus"] = target.ToString() });
        }
        var now = DateTimeOffset.UtcNow;
        order.Status = target;
        order.Events.Add(new OrderEvent { Type = eventType, Status = target, OccurredAt = now, Details = details });
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Results.Problem(
                title: "Invalid order transition",
                detail: "Another request changed this order first. Fetch the order and retry only if the transition is still allowed.",
                statusCode: StatusCodes.Status409Conflict,
                type: "https://httpstatuses.com/409");
        }
        return Results.Ok(ToView(order));
    }).WithName("TransitionOrderTo" + target).WithSummary(details);
}

static OrderView ToView(Order order)
{
    // Replay the append-only event stream to expose the current status.
    var replayedStatus = order.Events.OrderBy(e => e.Id).LastOrDefault()?.Status ?? order.Status;
    return new OrderView(
    order.Id, replayedStatus.ToString(), order.CreatedAt, order.Total,
    order.Items.OrderBy(i => i.Id).Select(i => new OrderItemView(i.ProductId, i.ProductName, i.UnitPrice, i.Quantity, i.LineTotal)).ToArray(),
    order.Events.OrderBy(e => e.Id).Select(e => new OrderEventView(e.Id, e.Type, e.Status.ToString(), e.OccurredAt, e.Details)).ToArray());
}

public sealed record OrderView(Guid Id, string Status, DateTimeOffset CreatedAt, decimal Total,
    IReadOnlyList<OrderItemView> Items, IReadOnlyList<OrderEventView> Events);
public sealed record OrderItemView(int ProductId, string ProductName, decimal UnitPrice, int Quantity, decimal LineTotal);
public sealed record OrderEventView(long Id, string Type, string Status, DateTimeOffset OccurredAt, string? Details);
