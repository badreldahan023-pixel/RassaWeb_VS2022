using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using RassaWeb.Models;

namespace RassaWeb.Controllers
{
    public class WarehouseController : Controller
    {
        private readonly RassaDb _db;

        public WarehouseController(RassaDb db)
        {
            _db = db;
        }

        public IActionResult Index(int? warehouseId)
        {
            int managerId = HttpContext.Session.GetInt32("UserID") ?? 2;
            var allWarehouses = _db.GetWarehouses();
            var currentWarehouse = _db.GetWarehouseByManager(managerId, warehouseId) ?? allWarehouses.FirstOrDefault();

            int whId = currentWarehouse?.WarehouseID ?? 1;

            ViewBag.AllWarehouses = allWarehouses;
            ViewBag.CurrentWarehouse = currentWarehouse;
            ViewBag.Categories = _db.GetCategories();
            ViewBag.WarehouseProducts = _db.GetProducts(warehouseId: whId);
            ViewBag.WarehouseOrders = _db.GetOrders(warehouseId: whId);
            ViewBag.RestaurantClients = _db.GetRestaurantClients();

            return View();
        }

        // ميزة التوريد المباشر: صاحب المخزن يقدر يسجل طلب توريد لأي صاحب مطعم أو كافيه مسجل على المنصة
        [HttpPost]
        public IActionResult SupplyToRestaurant(int warehouseId, int clientUserId, int productId, int quantity)
        {
            var prod = _db.GetProductById(productId);
            var client = _db.GetUserById(clientUserId);
            if (prod != null && client != null && quantity > 0)
            {
                var items = new List<CartItem>
                {
                    new CartItem
                    {
                        ProductID = prod.ProductID,
                        ProductName = prod.ProductName,
                        SizeUnit = prod.SizeUnit,
                        UnitPrice = prod.Price,
                        Quantity = quantity,
                        WarehouseID = warehouseId,
                        WarehouseName = prod.WarehouseName
                    }
                };
                int newOrderId = _db.CreateOrderWithBlockchain(client.UserID, warehouseId, client.LocationZone, items);
                TempData["Success"] = $"تم تسجيل أمر توريد مباشر من مخزنك إلى ({client.FullName} - {client.BusinessType}) وتوثيقه في Blockchain!";
                return RedirectToAction("Track", "Store", new { id = newOrderId });
            }
            return RedirectToAction("Index", new { warehouseId });
        }

        [HttpPost]
        public IActionResult AddProduct(Product model, int initialQty, int minThreshold)
        {
            if (!string.IsNullOrWhiteSpace(model.ProductName) && model.Price >= 0)
            {
                _db.AddProductWithInventory(model, initialQty, minThreshold);
                TempData["Success"] = $"تمت إضافة المنتج ({model.ProductName}) وتحديث المخزون بنجاح!";
            }
            return RedirectToAction("Index", new { warehouseId = model.WarehouseID });
        }

        [HttpPost]
        public IActionResult UpdateStock(int productId, int warehouseId, decimal price, int availableQty)
        {
            _db.UpdateProductStockAndPrice(productId, price, availableQty);
            TempData["Success"] = "تم تحديث سعر المنتج والكمية المتاحة في المخزن بنجاح!";
            return RedirectToAction("Index", new { warehouseId });
        }

        [HttpPost]
        public IActionResult UpdateOrderStatus(int orderId, int warehouseId, string newStatus)
        {
            _db.UpdateOrderStatusWithBlockchain(orderId, newStatus);
            TempData["Success"] = $"تم تحديث حالة الطلب إلى ({newStatus}) وإضافة كتلة توثيق جديدة في Blockchain!";
            return RedirectToAction("Index", new { warehouseId });
        }
    }
}
