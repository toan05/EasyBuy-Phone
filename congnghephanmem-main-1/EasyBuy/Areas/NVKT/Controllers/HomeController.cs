using EasyBuy.Attributes;
using Microsoft.AspNetCore.Mvc;

namespace EasyBuy.Areas.NVKT.Controllers
{
    [Area("NVKT")]
    [AuthorizeRole("NVKT")]
    public class HomeController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
