using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BireyselHesaplar.Data;

namespace BireyselHesaplar.Controllers
{
    public class HomeController : Controller
    {
        private readonly AppDbContext _context;

        public HomeController(AppDbContext context)
        {
            _context = context;
        }

        public IActionResult Index()
        {
            var userId = HttpContext.Session.GetInt32("UserId");

            if (userId == null)
                return RedirectToAction("Login", "Account");

            ViewBag.TotalBlogCount = _context.BlogPosts.Count();
            ViewBag.MyBlogCount = _context.BlogPosts.Count(x => x.UserId == userId.Value);
            ViewBag.TotalCommentCount = _context.Comments.Count();

            var recentBlogs = _context.BlogPosts
                .Include(x => x.User)
                .OrderByDescending(x => x.CreatedAt)
                .Take(5)
                .ToList();

            return View(recentBlogs);
        }
    }
}