# API Authentication and Authorisation

The backend now requires JSON Web Tokens (JWT) for every API endpoint and SignalR hub. Access is role-based and the demo environment exposes four personas:

| Username  | Role     | Default password |
|-----------|----------|------------------|
| `admin`   | Admin    | `Admin123!`      |
| `cashier` | Cashier  | `Cashier123!`    |
| `kitchen` | Kitchen  | `Kitchen123!`    |
| `readonly`| ReadOnly | `ReadOnly123!`   |

Tokens are issued via `POST /auth/login` by supplying the username and password. The response contains a bearer token (`accessToken`) and its UTC expiry (`expiresAtUtc`). Include the token in subsequent requests with an `Authorization: Bearer <token>` header or, for SignalR, via the `access_token` query string.

## Role policies

The platform exposes policies that map onto application roles:

| Policy                | Roles allowed                    | Applies to                                     |
|-----------------------|----------------------------------|------------------------------------------------|
| `ViewMenu`            | Admin, Cashier, Kitchen, ReadOnly| `GET /menu`                                    |
| `ViewCustomers`       | Admin, Cashier, ReadOnly         | `GET /customers`, `GET /customers/{id}`        |
| `ManageCustomers`     | Admin, Cashier                   | `POST/PUT/DELETE /customers`                   |
| `ViewOrders`          | Admin, Cashier, Kitchen, ReadOnly| `GET /orders/{code}` and SignalR `/hubs/orders`|
| `ManageOrders`        | Admin, Cashier                   | `POST /orders`, order updates and payments     |
| `ManageKitchen`       | Admin, Kitchen                   | `GET /kds/tickets`, SignalR `/hubs/kds`        |
| `VoiceAutomation`     | Admin, Cashier                   | `/voice` conversational ordering endpoints     |

Demo users and JWT settings (issuer, audience, signing key, expiry) are configured under the `Authentication` and `Jwt` sections of `appsettings.json`. Passwords are stored as SHA-256 hashes for the sample environment; update the hashes if you change the demo credentials.

## Testing checklist

Integration tests were updated to authenticate before calling APIs and assert role behaviour. Notably, `KitchenDisplayTests` now verifies that a `Cashier` token receives HTTP 403 (`Forbidden`) when requesting `/kds/tickets`, while the `Kitchen` role succeeds.
