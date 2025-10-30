namespace Takeaway.Api.Contracts.Menu;

public record MenuResponse(IReadOnlyCollection<MenuCategoryDto> Categories);

public record MenuCategoryDto(int Id, string Name, string? Description, IReadOnlyCollection<MenuProductDto> Products);

public record MenuProductDto(
    int Id,
    string Name,
    string? Description,
    decimal BasePrice,
    decimal VatRate,
    bool IsAvailable,
    int StockQuantity,
    IReadOnlyCollection<MenuProductVariantDto> Variants,
    IReadOnlyCollection<MenuProductModifierDto> Modifiers);

public record MenuProductVariantDto(int Id, string Name, decimal Price, decimal VatRate, bool IsDefault, int StockQuantity);

public record MenuProductModifierDto(int Id, string Name, decimal Price, decimal VatRate);
