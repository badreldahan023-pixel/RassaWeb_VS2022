using Microsoft.AspNetCore.Mvc;
using RassaWeb.Models;

namespace RassaWeb.Controllers
{
    public class HomeController : Controller
    {
        private readonly RassaDb _db;

        public HomeController(RassaDb db)
        {
            _db = db;
        }

        public IActionResult Index()
        {
            var vm = new HomeViewModel
            {
                Stats = _db.GetDashboardStats(),
                Categories = _db.GetCategories(),
                FeaturedWarehouses = _db.GetWarehouses().Take(6).ToList(),
                FeaturedProducts = _db.GetProducts().Take(8).ToList()
            };
            return View(vm);
        }

        // صفحة من نحن – رِصّة
        public IActionResult About()
        {
            ViewBag.Stats = _db.GetDashboardStats();
            return View();
        }
    }
}
