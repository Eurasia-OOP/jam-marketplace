# Sushi Marketplace Order API

ASP.NET Core 10 Web API demonstrating order state transitions, EF Core persistence, PostgreSQL, idempotency, append-only events, Problem Details, OpenAPI, and Scalar.

## Run locally

Requirements: .NET 10 SDK and Docker Compose.

```sh
docker compose up -d postgres
cd SushiMarketplace.Api
dotnet tool restore
dotnet ef database update
dotnet run
```

Open `/scalar` at the HTTP URL printed by `dotnet run`. The project includes an initial EF Core migration and seeds three sample products when the app starts. When the model changes, add a migration from `SushiMarketplace.Api` with `dotnet ef migrations add <Name>`, then apply it with `dotnet ef database update`.

## Happy path

List seeded products with `GET /products`, then create an order:

```http
POST /orders
Content-Type: application/json

{
  "items": [
    { "productId": "11111111-1111-4111-8111-111111111111", "quantity": 1 },
    { "productId": "22222222-2222-4222-8222-222222222222", "quantity": 1 }
  ]
}
```

Use the returned order ID for each transition:

```http
POST /orders/{id}/pay
POST /orders/{id}/pack
POST /orders/{id}/ship
GET  /orders/{id}
```

The order response shows `Shipped` and four events: initial receipt, payment, packing, and shipping. Each transition route validates the current status through one state machine. Invalid transitions return HTTP 409 Problem Details.

For retry-safe clients, send the same `Idempotency-Key` header on a retry of a transition. The original event is returned with `replayed: true` and no additional event is written. Repeating an operation with a different key (or no key) after the state has advanced is a 409. Reusing a key for a different transition is also a 409. Transition updates lock the order row in PostgreSQL so concurrent requests cannot both advance it.

## Routes

- `GET /products`
- `POST /orders`
- `GET /orders/{id}`
- `POST /orders/{id}/pay`
- `POST /orders/{id}/pack`
- `POST /orders/{id}/ship`
- `GET /health`

OpenAPI JSON is available at `/openapi/v1.json` in Development; Scalar is at `/scalar`.
