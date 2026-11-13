using Microsoft.AspNetCore.Mvc;
using RassaWeb.Models;

namespace RassaWeb.Controllers
{
    public class AdminController : Controller
    {
        private readonly RassaDb _db;

        public AdminController(RassaDb db)
        {
            _db = db;
        }

        public IActionResult Index()
        {
            ViewBag.Stats = _db.GetDashboardStats();
            ViewBag.Users = _db.GetAllUsers();
            ViewBag.Warehouses = _db.GetWarehouses();
            ViewBag.Categories = _db.GetCategories();
            ViewBag.Orders = _db.GetOrders();
            ViewBag.Products = _db.GetProducts();
            return View();
        }

        [HttpPost]
        public IActionResult AddWarehouse(Warehouse model)
        {
            if (!string.IsNullOrWhiteSpace(model.WarehouseName))
            {
                _db.AddWarehouse(model);
                TempData["Success"] = $"تمت إضافة المخزن ({model.WarehouseName}) بنجاح!";
            }
            return RedirectToAction("Index");
        }

        [HttpPost]
        public IActionResult AddCategory(string categoryName, string description, string iconClass)
        {
            if (!string.IsNullOrWhiteSpace(categoryName))
            {
                _db.AddCategory(categoryName, description, iconClass);
                TempData["Success"] = $"تمت إضافة القسم ({categoryName}) بنجاح!";
            }
            return RedirectToAction("Index");
        }

        [HttpPost]
        public IActionResult ToggleUser(int userId)
        {
            _db.ToggleUserStatus(userId);
            TempData["Success"] = "تم تحديث حالة حساب المستخدم بنجاح.";
            return RedirectToAction("Index");
        }

        [HttpPost]
        public IActionResult DeleteProduct(int productId)
        {
            _db.DeleteProduct(productId);
            TempData["Success"] = "تم حذف المنتج من النظام بنجاح.";
            return RedirectToAction("Index");
        }
    }
}
