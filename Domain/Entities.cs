namespace SushiMarketplace.Api.Domain;

public sealed class Product
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public decimal Price { get; set; }
    public bool IsAvailable { get; set; } = true;
}

public enum OrderStatus { Received, Paid, Packed, Shipped }

public sealed class Order
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public OrderStatus Status { get; set; } = OrderStatus.Received;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<OrderLine> Items { get; set; } = [];
    public List<OrderEvent> Events { get; set; } = [];
}

public sealed class OrderLine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public Guid ProductId { get; set; }
    public required string ProductName { get; set; }
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
}

public sealed class OrderEvent
{
    public long Id { get; set; }
    public Guid OrderId { get; set; }
    public required string Type { get; set; }
    public OrderStatus FromStatus { get; set; }
    public OrderStatus ToStatus { get; set; }
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
    public string? IdempotencyKey { get; set; }
}
