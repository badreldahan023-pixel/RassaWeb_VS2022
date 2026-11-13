using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using RassaWeb.Models;

namespace RassaWeb.Controllers
{
    public class StoreController : Controller
    {
        private readonly RassaDb _db;
        private const string CartSessionKey = "RassaCart";

        public StoreController(RassaDb db)
        {
            _db = db;
        }

        // صفحة المخازن القريبة (شريحة 5)
        public IActionResult Warehouses(string? zone, string? q)
        {
            ViewBag.CurrentZone = zone;
            ViewBag.CurrentQuery = q;
            var warehouses = _db.GetWarehouses(zone, q);
            return View(warehouses);
        }

        // صفحة المنتجات والبحث والتصفية والمقارنة
        public IActionResult Products(int? categoryId, int? warehouseId, string? search, string? sortBy)
        {
            ViewBag.Categories = _db.GetCategories();
            ViewBag.Warehouses = _db.GetWarehouses();
            ViewBag.SelectedCategory = categoryId;
            ViewBag.SelectedWarehouse = warehouseId;
            ViewBag.SearchQuery = search;
            ViewBag.SortBy = sortBy;

            var products = _db.GetProducts(categoryId, warehouseId, search, sortBy);
            return View(products);
        }

        // صفحة تفاصيل المنتج (شريحة 6)
        public IActionResult Details(int id)
        {
            var product = _db.GetProductById(id);
            if (product == null) return RedirectToAction("Products");

            // جلب منتجات أخرى من نفس المخزن أو القسم للمقارنة
            ViewBag.RelatedProducts = _db.GetProducts(categoryId: product.CategoryID)
                                         .Where(p => p.ProductID != id)
                                         .Take(4).ToList();
            return View(product);
        }

        // عرض سلة المشتريات
        public IActionResult Cart()
        {
            var cart = GetCartFromSession();
            return View(cart);
        }

        // إضافة منتج إلى السلة
        [HttpPost]
        public IActionResult AddToCart(int productId, int quantity = 1)
        {
            var prod = _db.GetProductById(productId);
            if (prod == null) return RedirectToAction("Products");

            if (quantity < 1) quantity = 1;
            if (quantity > prod.AvailableQuantity && prod.AvailableQuantity > 0)
                quantity = prod.AvailableQuantity;

            var cart = GetCartFromSession();
            var existing = cart.FirstOrDefault(c => c.ProductID == productId);
            if (existing != null)
            {
                existing.Quantity += quantity;
            }
            else
            {
                cart.Add(new CartItem
                {
                    ProductID = prod.ProductID,
                    ProductName = prod.ProductName,
                    SizeUnit = prod.SizeUnit,
                    UnitPrice = prod.Price,
                    Quantity = quantity,
                    WarehouseID = prod.WarehouseID,
                    WarehouseName = prod.WarehouseName
                });
            }

            SaveCartToSession(cart);
            TempData["Success"] = $"تمت إضافة ({prod.ProductName}) إلى سلة المشتريات بنجاح!";
            return RedirectToAction("Cart");
        }

        [HttpPost]
        public IActionResult UpdateCartQuantity(int productId, int quantity)
        {
            var cart = GetCartFromSession();
            var item = cart.FirstOrDefault(c => c.ProductID == productId);
            if (item != null)
            {
                if (quantity <= 0)
                    cart.Remove(item);
                else
                    item.Quantity = quantity;
                SaveCartToSession(cart);
            }
            return RedirectToAction("Cart");
        }

        [HttpPost]
        public IActionResult RemoveFromCart(int productId)
        {
            var cart = GetCartFromSession();
            cart.RemoveAll(c => c.ProductID == productId);
            SaveCartToSession(cart);
            return RedirectToAction("Cart");
        }

        // إتمام الطلب وتوثيقه في سلسلة كتل البلوك تشين
        [HttpPost]
        public IActionResult Checkout(string deliveryAddress)
        {
            var cart = GetCartFromSession();
            if (cart.Count == 0)
            {
                TempData["Error"] = "سلة المشتريات فارغة!";
                return RedirectToAction("Products");
            }

            int clientUserId = HttpContext.Session.GetInt32("UserID") ?? 5; // افتراضياً كافيه أرابيكا للتجربة السريعة
            int warehouseId = cart.First().WarehouseID;

            if (string.IsNullOrWhiteSpace(deliveryAddress))
            {
                deliveryAddress = HttpContext.Session.GetString("LocationZone") ?? "القاهرة - مدينة نصر - شارع عباس العقاد";
            }

            int newOrderId = _db.CreateOrderWithBlockchain(clientUserId, warehouseId, deliveryAddress, cart);

            // تفريغ السلة بعد نجاح الطلب
            HttpContext.Session.Remove(CartSessionKey);
            HttpContext.Session.SetInt32("CartCount", 0);

            TempData["Success"] = "تم إنشاء الطلب بنجاح وتوثيق الكتل (01، 02، 03) في سجل Blockchain!";
            return RedirectToAction("Track", new { id = newOrderId });
        }

        // قائمة الطلبات
        public IActionResult Orders()
        {
            int? userId = HttpContext.Session.GetInt32("UserID");
            string? role = HttpContext.Session.GetString("Role");

            List<Order> orders;
            if (userId.HasValue && role == "Client")
                orders = _db.GetOrders(clientUserId: userId.Value);
            else
                orders = _db.GetOrders();

            return View(orders);
        }

        // شاشة تتبع الطلب لحظياً وسجل Blockchain (شريحة 6 و7)
        public IActionResult Track(int id = 1)
        {
            var order = _db.GetOrderDetails(id);
            if (order == null)
            {
                var all = _db.GetOrders();
                if (all.Any())
                    order = _db.GetOrderDetails(all.First().OrderID);
            }
            return View(order);
        }

        private List<CartItem> GetCartFromSession()
        {
            string? json = HttpContext.Session.GetString(CartSessionKey);
            if (string.IsNullOrEmpty(json)) return new List<CartItem>();
            return JsonSerializer.Deserialize<List<CartItem>>(json) ?? new List<CartItem>();
        }

        private void SaveCartToSession(List<CartItem> cart)
        {
            string json = JsonSerializer.Serialize(cart);
            HttpContext.Session.SetString(CartSessionKey, json);
            HttpContext.Session.SetInt32("CartCount", cart.Sum(i => i.Quantity));
        }
    }
}
