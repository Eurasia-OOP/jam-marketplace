# Sushi Marketplace — order pipeline

A small ASP.NET Core 10 sushi marketplace with a responsive storefront and Web API. It demonstrates dependency injection, EF Core with SQLite, an explicit order state machine, idempotent order submission, append-only order events, and RFC 7807 Problem Details for invalid transitions.

## Run locally

Install the .NET 10 SDK, then from `SushiMarketplace.Api` run (or use the `--project` form from the parent folder):

```sh
dotnet restore
dotnet run
# From the parent folder instead: dotnet run --project "SushiMarketplace.Api/SushiMarketplace.Api.csproj"
```

Open [http://localhost:5080/scalar/v1](http://localhost:5080/scalar/v1). The first run creates `sushi-marketplace.db` and seeds the eight menu plates. The storefront is served from the app root at [http://localhost:5080](http://localhost:5080). It includes the Menu, Kitchen, Orders, and API views; the menu basket creates orders through the API and the order pages advance the same state machine. Scalar shows the request and response schemas.

## Demo flow in Scalar

1. `GET /api/products` and note the product IDs.
2. `POST /api/orders` with two products:

```json
{
  "items": [
    { "productId": 1, "quantity": 2 },
    { "productId": 2, "quantity": 1 }
  ]
}
```

Copy the returned `id`.

3. In sequence, call `POST /api/orders/{id}/pay`, `/pack`, then `/ship`.
4. `GET /api/orders/{id}` returns `status: "Shipped"`, the line items, and four events: `OrderReceived`, `PaymentRecorded`, `OrderPacked`, and `OrderShipped`.
5. Call `/pay` again. The API returns **409 Conflict** with Problem Details, and it does not append another payment event.

## Idempotency and state rules

For safe retry of order creation, optionally send an `Idempotency-Key` header. Repeating a create request with the same key returns the original order instead of creating another. A lifecycle action is accepted only from its preceding state: Received → Paid → Packed → Shipped. Repeating an action after it has succeeded returns 409. The state machine stores allowed transitions in one service, and EF Core uses the current status as a concurrency token so simultaneous duplicate actions cannot both win.

`OrderEvent` rows are append-only in the application flow. They record each accepted state and timestamp. The order response reconstructs its visible history by reading these rows in sequence. Payment is simulated; no payment provider or real charge is involved.

## API routes

- `GET /api/products`
- `POST /api/orders`
- `GET /api/orders`
- `GET /api/orders/{id}`
- `POST /api/orders/{id}/pay`
- `POST /api/orders/{id}/pack`
- `POST /api/orders/{id}/ship`
- `GET /openapi/v1.json`
- `GET /scalar/v1`
