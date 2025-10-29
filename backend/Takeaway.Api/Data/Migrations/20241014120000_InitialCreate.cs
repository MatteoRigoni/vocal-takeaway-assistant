using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Takeaway.Api.Data.Migrations;

public partial class InitialCreate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Categories",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                Name = table.Column<string>(type: "TEXT", nullable: false),
                Description = table.Column<string>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Categories", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "OrderChannels",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                Name = table.Column<string>(type: "TEXT", nullable: false),
                Description = table.Column<string>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_OrderChannels", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "OrderStatuses",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                Name = table.Column<string>(type: "TEXT", nullable: false),
                Description = table.Column<string>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_OrderStatuses", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "PaymentMethods",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                Name = table.Column<string>(type: "TEXT", nullable: false),
                Description = table.Column<string>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PaymentMethods", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Shops",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                Name = table.Column<string>(type: "TEXT", nullable: false),
                Address = table.Column<string>(type: "TEXT", nullable: false),
                Phone = table.Column<string>(type: "TEXT", nullable: false),
                Email = table.Column<string>(type: "TEXT", nullable: false),
                OpeningHours = table.Column<string>(type: "TEXT", nullable: false),
                Description = table.Column<string>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Shops", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Customers",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                Name = table.Column<string>(type: "TEXT", nullable: false),
                Phone = table.Column<string>(type: "TEXT", nullable: false),
                Email = table.Column<string>(type: "TEXT", nullable: false),
                Address = table.Column<string>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Customers", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Products",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                ShopId = table.Column<int>(type: "INTEGER", nullable: false),
                CategoryId = table.Column<int>(type: "INTEGER", nullable: false),
                Name = table.Column<string>(type: "TEXT", nullable: false),
                Description = table.Column<string>(type: "TEXT", nullable: true),
                Price = table.Column<decimal>(type: "TEXT", nullable: false),
                IsAvailable = table.Column<bool>(type: "INTEGER", nullable: false),
                ImageUrl = table.Column<string>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Products", x => x.Id);
                table.ForeignKey(
                    name: "FK_Products_Categories_CategoryId",
                    column: x => x.CategoryId,
                    principalTable: "Categories",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Products_Shops_ShopId",
                    column: x => x.ShopId,
                    principalTable: "Shops",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "Orders",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                ShopId = table.Column<int>(type: "INTEGER", nullable: false),
                CustomerId = table.Column<int>(type: "INTEGER", nullable: true),
                OrderChannelId = table.Column<int>(type: "INTEGER", nullable: false),
                OrderStatusId = table.Column<int>(type: "INTEGER", nullable: false),
                OrderDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                TotalAmount = table.Column<decimal>(type: "TEXT", nullable: false),
                DeliveryAddress = table.Column<string>(type: "TEXT", nullable: false),
                Notes = table.Column<string>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Orders", x => x.Id);
                table.ForeignKey(
                    name: "FK_Orders_Customers_CustomerId",
                    column: x => x.CustomerId,
                    principalTable: "Customers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "FK_Orders_OrderChannels_OrderChannelId",
                    column: x => x.OrderChannelId,
                    principalTable: "OrderChannels",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_Orders_OrderStatuses_OrderStatusId",
                    column: x => x.OrderStatusId,
                    principalTable: "OrderStatuses",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_Orders_Shops_ShopId",
                    column: x => x.ShopId,
                    principalTable: "Shops",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "Payments",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                OrderId = table.Column<int>(type: "INTEGER", nullable: false),
                PaymentMethodId = table.Column<int>(type: "INTEGER", nullable: false),
                Amount = table.Column<decimal>(type: "TEXT", nullable: false),
                PaymentDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                Status = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Payments", x => x.Id);
                table.ForeignKey(
                    name: "FK_Payments_Orders_OrderId",
                    column: x => x.OrderId,
                    principalTable: "Orders",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_Payments_PaymentMethods_PaymentMethodId",
                    column: x => x.PaymentMethodId,
                    principalTable: "PaymentMethods",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "OrderItems",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                OrderId = table.Column<int>(type: "INTEGER", nullable: false),
                ProductId = table.Column<int>(type: "INTEGER", nullable: false),
                Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                UnitPrice = table.Column<decimal>(type: "TEXT", nullable: false),
                Subtotal = table.Column<decimal>(type: "TEXT", nullable: false),
                Modifiers = table.Column<string>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_OrderItems", x => x.Id);
                table.ForeignKey(
                    name: "FK_OrderItems_Orders_OrderId",
                    column: x => x.OrderId,
                    principalTable: "Orders",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_OrderItems_Products_ProductId",
                    column: x => x.ProductId,
                    principalTable: "Products",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.InsertData(
            table: "Categories",
            columns: new[] { "Id", "Description", "Name" },
            values: new object[,]
            {
                { 1, "Traditional Italian pizzas", "Pizza" },
                { 2, "Soft drinks and beverages", "Beverage" }
            });

        migrationBuilder.InsertData(
            table: "OrderChannels",
            columns: new[] { "Id", "Description", "Name" },
            values: new object[,]
            {
                { 1, "Voice assistant", "Voice" },
                { 2, "Phone call", "Phone" },
                { 3, "Mobile application", "App" }
            });

        migrationBuilder.InsertData(
            table: "OrderStatuses",
            columns: new[] { "Id", "Description", "Name" },
            values: new object[,]
            {
                { 1, "Order received", "Received" },
                { 2, "Order is being prepared", "InPreparation" },
                { 3, "Order completed", "Completed" },
                { 4, "Order cancelled", "Cancelled" }
            });

        migrationBuilder.InsertData(
            table: "PaymentMethods",
            columns: new[] { "Id", "Description", "Name" },
            values: new object[,]
            {
                { 1, "Cash payment", "Cash" },
                { 2, "Credit card payment", "CreditCard" },
                { 3, "Digital wallet", "Digital" }
            });

        migrationBuilder.InsertData(
            table: "Shops",
            columns: new[] { "Id", "Address", "Description", "Email", "Name", "OpeningHours", "Phone" },
            values: new object[] { 1, "123 Voice Lane", "Demo shop for the voice assistant", "contact@vocaltakeaway.example", "Vocal Takeaway", "Mon-Sun 11:00-23:00", "+39 055 1234567" });

        migrationBuilder.InsertData(
            table: "Products",
            columns: new[] { "Id", "CategoryId", "Description", "ImageUrl", "IsAvailable", "Name", "Price", "ShopId" },
            values: new object[,]
            {
                { 1, 1, "Classic pizza with tomato, mozzarella and basil", null, true, "Margherita", 7.50m, 1 },
                { 2, 1, "Spicy salami pizza with mozzarella", null, true, "Diavola", 8.50m, 1 },
                { 3, 2, "Chilled 33cl can", null, true, "Coca-Cola", 2.50m, 1 }
            });

        migrationBuilder.CreateIndex(
            name: "IX_OrderItems_OrderId",
            table: "OrderItems",
            column: "OrderId");

        migrationBuilder.CreateIndex(
            name: "IX_OrderItems_ProductId",
            table: "OrderItems",
            column: "ProductId");

        migrationBuilder.CreateIndex(
            name: "IX_Orders_CustomerId",
            table: "Orders",
            column: "CustomerId");

        migrationBuilder.CreateIndex(
            name: "IX_Orders_OrderChannelId",
            table: "Orders",
            column: "OrderChannelId");

        migrationBuilder.CreateIndex(
            name: "IX_Orders_OrderStatusId",
            table: "Orders",
            column: "OrderStatusId");

        migrationBuilder.CreateIndex(
            name: "IX_Orders_ShopId",
            table: "Orders",
            column: "ShopId");

        migrationBuilder.CreateIndex(
            name: "IX_Payments_OrderId",
            table: "Payments",
            column: "OrderId");

        migrationBuilder.CreateIndex(
            name: "IX_Payments_PaymentMethodId",
            table: "Payments",
            column: "PaymentMethodId");

        migrationBuilder.CreateIndex(
            name: "IX_Products_CategoryId",
            table: "Products",
            column: "CategoryId");

        migrationBuilder.CreateIndex(
            name: "IX_Products_ShopId",
            table: "Products",
            column: "ShopId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "OrderItems");

        migrationBuilder.DropTable(
            name: "Payments");

        migrationBuilder.DropTable(
            name: "Products");

        migrationBuilder.DropTable(
            name: "Orders");

        migrationBuilder.DropTable(
            name: "Categories");

        migrationBuilder.DropTable(
            name: "Customers");

        migrationBuilder.DropTable(
            name: "PaymentMethods");

        migrationBuilder.DropTable(
            name: "OrderChannels");

        migrationBuilder.DropTable(
            name: "OrderStatuses");

        migrationBuilder.DropTable(
            name: "Shops");
    }
}
