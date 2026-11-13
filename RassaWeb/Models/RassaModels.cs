using System;
using System.Collections.Generic;

namespace RassaWeb.Models
{
    public class User
    {
        public int UserID { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public string Role { get; set; } = "Client"; // Admin, WarehouseManager, Client
        public string BusinessType { get; set; } = "مطعم"; // مطعم، كافيه، مخبز، أخرى
        public string LocationZone { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; }
    }

    public class Category
    {
        public int CategoryID { get; set; }
        public string CategoryName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string IconClass { get; set; } = "bi-box-seam";
        public int ProductCount { get; set; }
    }

    public class Warehouse
    {
        public int WarehouseID { get; set; }
        public string WarehouseName { get; set; } = string.Empty;
        public string Specialty { get; set; } = string.Empty;
        public string LocationZone { get; set; } = string.Empty;
        public decimal DistanceKm { get; set; }
        public string DeliveryStatus { get; set; } = "متاح الآن";
        public int ManagerUserID { get; set; }
        public string ManagerName { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
        public int ProductsCount { get; set; }
    }

    public class Product
    {
        public int ProductID { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string SizeUnit { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public string ExpectedDeliveryTime { get; set; } = "اليوم - 06:00 مساءً";
        public string ImageUrl { get; set; } = string.Empty;
        public int CategoryID { get; set; }
        public string CategoryName { get; set; } = string.Empty;
        public string CategoryIcon { get; set; } = "bi-box-seam";
        public int WarehouseID { get; set; }
        public string WarehouseName { get; set; } = string.Empty;
        public string WarehouseZone { get; set; } = string.Empty;
        public decimal WarehouseDistanceKm { get; set; }
        public string WarehouseDeliveryStatus { get; set; } = "متاح الآن";
        public int AvailableQuantity { get; set; }
        public int MinThreshold { get; set; }
    }

    public class CartItem
    {
        public int ProductID { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string SizeUnit { get; set; } = string.Empty;
        public decimal UnitPrice { get; set; }
        public int Quantity { get; set; }
        public int WarehouseID { get; set; }
        public string WarehouseName { get; set; } = string.Empty;
        public decimal SubTotal => UnitPrice * Quantity;
    }

    public class Order
    {
        public int OrderID { get; set; }
        public string OrderNumber { get; set; } = string.Empty;
        public int ClientUserID { get; set; }
        public string ClientName { get; set; } = string.Empty;
        public string ClientBusinessType { get; set; } = string.Empty;
        public int WarehouseID { get; set; }
        public string WarehouseName { get; set; } = string.Empty;
        public DateTime OrderDate { get; set; }
        public decimal TotalAmount { get; set; }
        public string OrderStatus { get; set; } = string.Empty;
        public int EstimatedArrivalMinutes { get; set; }
        public string DeliveryAddress { get; set; } = string.Empty;
        public bool IsChainValid { get; set; } = true;
        public List<OrderItem> Items { get; set; } = new List<OrderItem>();
        public List<BlockchainBlock> LedgerBlocks { get; set; } = new List<BlockchainBlock>();
    }

    public class OrderItem
    {
        public int OrderItemID { get; set; }
        public int OrderID { get; set; }
        public int ProductID { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string SizeUnit { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal SubTotal { get; set; }
    }

    public class BlockchainBlock
    {
        public int BlockID { get; set; }
        public int OrderID { get; set; }
        public int BlockIndex { get; set; }
        public string EventType { get; set; } = string.Empty;
        public string EventDescription { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public string PreviousHash { get; set; } = string.Empty;
        public string CurrentHash { get; set; } = string.Empty;
    }

    public class DashboardStats
    {
        public int TotalClients { get; set; }
        public int TotalWarehouses { get; set; }
        public int TotalCategories { get; set; }
        public int TotalProducts { get; set; }
        public int TotalOrders { get; set; }
        public decimal TotalSales { get; set; }
        public int LowStockCount { get; set; }
        public int TotalBlockchainBlocks { get; set; }
    }

    public class HomeViewModel
    {
        public DashboardStats Stats { get; set; } = new DashboardStats();
        public List<Category> Categories { get; set; } = new List<Category>();
        public List<Warehouse> FeaturedWarehouses { get; set; } = new List<Warehouse>();
        public List<Product> FeaturedProducts { get; set; } = new List<Product>();
    }
}
