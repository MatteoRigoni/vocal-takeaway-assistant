# Domain Model

```mermaid
erDiagram
    Shop ||--o{ Product : has
    Shop ||--o{ Order : receives
    Product }o--|| Category : categorized
    Customer ||--o{ Order : places
    OrderChannel ||--o{ Order : channel
    OrderStatus ||--o{ Order : status
    PaymentMethod ||--o{ Payment : type
    Order ||--o{ Payment : has
    Order ||--|{ OrderItem : contains
    Product ||--|{ OrderItem : ordered
    Product ||--o{ ProductVariant : offers
    Product ||--o{ ProductModifier : optional
    ProductVariant ||--|{ OrderItem : selected
    Order ||--o{ AuditLog : records

    Shop {
        int Id PK
        string Name
        string Address
        string Phone
        string Email
        string OpeningHours
        string Description
    }

    Category {
        int Id PK
        string Name
        string Description
    }

    Product {
        int Id PK
        int ShopId FK
        int CategoryId FK
        string Name
        string Description
        decimal Price
        decimal VatRate
        bool IsAvailable
        string ImageUrl
        int StockQuantity
    }

    Customer {
        int Id PK
        string Name
        string Phone
        string Email
        string Address
    }

    OrderChannel {
        int Id PK
        string Name
        string Description
    }

    OrderStatus {
        int Id PK
        string Name
        string Description
    }

    PaymentMethod {
        int Id PK
        string Name
        string Description
    }

    Payment {
        int Id PK
        int OrderId FK
        int PaymentMethodId FK
        decimal Amount
        datetime PaymentDate
        string Status
    }

    Order {
        int Id PK
        int ShopId FK
        int CustomerId FK
        int OrderChannelId FK
        int OrderStatusId FK
        datetime OrderDate
        datetime CreatedAt
        decimal TotalAmount
        string OrderCode
        string DeliveryAddress
        string Notes
    }

    OrderItem {
        int Id PK
        int OrderId FK
        int ProductId FK
        int ProductVariantId FK
        int Quantity
        decimal UnitPrice
        decimal Subtotal
        string Modifiers
        string VariantName
    }

    ProductVariant {
        int Id PK
        int ProductId FK
        string Name
        decimal Price
        decimal VatRate
        bool IsDefault
        int StockQuantity
    }

    ProductModifier {
        int Id PK
        int ProductId FK
        string Name
        decimal Price
        decimal VatRate
    }

    AuditLog {
        int Id PK
        int OrderId FK
        string EventType
        datetime CreatedAt
        string Payload
    }
```
