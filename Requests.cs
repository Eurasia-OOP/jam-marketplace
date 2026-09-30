namespace SushiMarketplace.Api.Contracts;

public sealed record CreateOrderRequest(IReadOnlyList<CreateOrderItem> Items);
public sealed record CreateOrderItem(Guid ProductId, int Quantity);
