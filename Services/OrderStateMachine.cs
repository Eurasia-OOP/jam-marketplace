using SushiMarketplace.Api.Domain;

namespace SushiMarketplace.Api.Services;

public sealed class OrderStateMachine
{
    private static readonly IReadOnlyDictionary<OrderStatus, OrderStatus> Transitions =
        new Dictionary<OrderStatus, OrderStatus>
        {
            [OrderStatus.Received] = OrderStatus.Paid,
            [OrderStatus.Paid] = OrderStatus.Packed,
            [OrderStatus.Packed] = OrderStatus.Shipped
        };

    public bool TryGetNext(OrderStatus current, string action, out OrderStatus next)
    {
        var expected = action.ToLowerInvariant() switch
        {
            "pay" => OrderStatus.Paid,
            "pack" => OrderStatus.Packed,
            "ship" => OrderStatus.Shipped,
            _ => (OrderStatus?)null
        };
        if (expected is not null && Transitions.TryGetValue(current, out next) && next == expected)
            return true;
        next = default;
        return false;
    }
}
