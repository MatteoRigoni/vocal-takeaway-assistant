# vocal-takeaway-assistant
Reusable take-away ordering system with voice/text orders

erDiagram
    %% Tabella dei negozi/ristoranti
    Shop {
        int Id PK
        string Name
        string Address
        string Phone
        string Email
        string OpeningHours
        string Description
    }

    %% Categorie di prodotti (es. Pizza, Bevanda)
    Category {
        int Id PK
        string Name
        string Description
    }

    %% Prodotti del menu
    Product {
        int Id PK
        int ShopId FK
        int CategoryId FK
        string Name
        string Description
        decimal Price
        bool IsAvailable
        string ImageUrl
    }

    %% Clienti (opzionale, ma utile per ordini ricorrenti)
    Customer {
        int Id PK
        string Name
        string Phone
        string Email
        string Address
    }

    %% Canale di ordinazione (voce, telefono, app, dine‑in, ecc.)
    OrderChannel {
        int Id PK
        string Name
        string Description
    }

    %% Stati dell'ordine (inserito, in preparazione, consegnato, annullato, ecc.)
    OrderStatus {
        int Id PK
        string Name
        string Description
    }

    %% Metodi di pagamento (contanti, carta di credito, digitale, ecc.)
    PaymentMethod {
        int Id PK
        string Name
        string Description
    }

    %% Pagamenti per gli ordini
    Payment {
        int Id PK
        int OrderId FK
        int PaymentMethodId FK
        decimal Amount
        datetime PaymentDate
        string Status
    }

    %% Ordini
    "Order" {
        int Id PK
        int ShopId FK
        int CustomerId FK
        int OrderChannelId FK
        int OrderStatusId FK
        datetime OrderDate
        decimal TotalAmount
        string DeliveryAddress
        string Notes
    }

    %% Dettagli delle righe d'ordine
    OrderItem {
        int Id PK
        int OrderId FK
        int ProductId FK
        int Quantity
        decimal UnitPrice
        decimal Subtotal
        string Modifiers
    }

    %% Relazioni tra le entità
    Shop ||--o{ Product : has
    Shop ||--o{ "Order" : receives
    Product }o--|| Category : categorized
    Customer ||--o{ "Order" : places
    OrderChannel ||--o{ "Order" : channel
    OrderStatus ||--o{ "Order" : status
    PaymentMethod ||--o{ Payment : type
    "Order" ||--o{ Payment : has
    "Order" ||--|{ OrderItem : contains
    Product ||--|{ OrderItem : ordered
