using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using RassaWeb.Models;

namespace RassaWeb.Controllers
{
    public class AccountController : Controller
    {
        private readonly RassaDb _db;

        public AccountController(RassaDb db)
        {
            _db = db;
        }

        [HttpGet]
        public IActionResult Login()
        {
            return View();
        }

        [HttpPost]
        public IActionResult Login(string emailOrPhone, string password)
        {
            if (string.IsNullOrWhiteSpace(emailOrPhone) || string.IsNullOrWhiteSpace(password))
            {
                ViewBag.Error = "يرجى إدخال البريد الإلكتروني أو رقم الهاتف وكلمة المرور.";
                return View();
            }

            var user = _db.AuthenticateUser(emailOrPhone, password);
            if (user == null)
            {
                ViewBag.Error = "بيانات الدخول غير صحيحة أو الحساب غير مفعل.";
                return View();
            }

            HttpContext.Session.SetInt32("UserID", user.UserID);
            HttpContext.Session.SetString("FullName", user.FullName);
            HttpContext.Session.SetString("Role", user.Role);
            HttpContext.Session.SetString("BusinessType", user.BusinessType);
            HttpContext.Session.SetString("LocationZone", user.LocationZone);

            TempData["Success"] = $"أهلاً بك يا {user.FullName} في منصة رِصّة!";

            if (user.Role == "Admin")
                return RedirectToAction("Index", "Admin");
            if (user.Role == "WarehouseManager")
                return RedirectToAction("Index", "Warehouse");

            return RedirectToAction("Warehouses", "Store");
        }

        [HttpGet]
        public IActionResult Register()
        {
            return View();
        }

        [HttpPost]
        public IActionResult Register(User model, string accountRoleType, string? warehouseName, string? warehouseSpecialty)
        {
            if (string.IsNullOrWhiteSpace(model.FullName) || string.IsNullOrWhiteSpace(model.Email) || string.IsNullOrWhiteSpace(model.PasswordHash))
            {
                ViewBag.Error = "يرجى ملء جميع الحقول الأساسية المطلوبة.";
                return View(model);
            }

            try
            {
                if (accountRoleType == "WarehouseManager")
                {
                    model.Role = "WarehouseManager";
                    model.BusinessType = "مخزن توريد";
                }
                else
                {
                    model.Role = "Client";
                    if (string.IsNullOrWhiteSpace(model.BusinessType)) model.BusinessType = "مطعم";
                }

                if (string.IsNullOrWhiteSpace(model.LocationZone))
                    model.LocationZone = "القاهرة - مدينة نصر";

                int newUserId = _db.RegisterUser(model, warehouseName, warehouseSpecialty);

                HttpContext.Session.SetInt32("UserID", newUserId);
                HttpContext.Session.SetString("FullName", model.FullName);
                HttpContext.Session.SetString("Role", model.Role);
                HttpContext.Session.SetString("BusinessType", model.BusinessType);
                HttpContext.Session.SetString("LocationZone", model.LocationZone);

                if (model.Role == "WarehouseManager")
                {
                    TempData["Success"] = "تم تسجيل حسابك كصاحب مخزن وإنشاء مخزنك بـ 15 منتجاً جاهزاً! يمكنك الآن التوريد لأي مطعم أو كافيه مسجل.";
                    return RedirectToAction("Index", "Warehouse");
                }

                TempData["Success"] = "تم إنشاء حساب نشاطك التجاري بنجاح في منصة رِصّة! استكشف الـ 30 مخزناً واطلب الآن.";
                return RedirectToAction("Warehouses", "Store");
            }
            catch
            {
                ViewBag.Error = "البريد الإلكتروني مسجل مسبقاً، يرجى استخدام بريد آخر أو تسجيل الدخول.";
                return View(model);
            }
        }

        public IActionResult Profile()
        {
            int? userId = HttpContext.Session.GetInt32("UserID");
            if (!userId.HasValue) return RedirectToAction("Login");

            var user = _db.GetUserById(userId.Value);
            ViewBag.MyOrders = _db.GetOrders(clientUserId: userId.Value);
            return View(user);
        }

        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            TempData["Success"] = "تم تسجيل الخروج بنجاح.";
            return RedirectToAction("Index", "Home");
        }
    }
}
