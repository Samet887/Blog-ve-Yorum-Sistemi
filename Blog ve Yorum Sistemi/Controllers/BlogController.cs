using BireyselHesaplar.Data;
using BireyselHesaplar.Models;
using BireyselHesaplar.Utilities;
using BireyselHesaplar.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Text.Json;

namespace BireyselHesaplar.Controllers
{
    public class BlogController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _environment;
        private static readonly HashSet<string> AllowedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".webp", ".gif"
        };
        private static readonly List<Category> DefaultCategories = new()
        {
            new Category { Name = "Teknoloji", Slug = "teknoloji" },
            new Category { Name = "Yazilim", Slug = "yazilim" },
            new Category { Name = "Tasarim", Slug = "tasarim" },
            new Category { Name = "Yasam", Slug = "yasam" },
            new Category { Name = "Kariyer/Is", Slug = "is" }
        };
        private const long MaxImageSizeBytes = 20 * 1024 * 1024;

        public BlogController(AppDbContext context, IWebHostEnvironment environment)
        {
            _context = context;
            _environment = environment;
        }

        public IActionResult Index(string? q, string? author, DateTime? from, DateTime? to, bool? mine, string? sort, string? quick, string? category)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            var isAuthenticated = userId.HasValue;
            var isAdmin = isAuthenticated && IsAdmin();
            var activeBan = isAuthenticated ? GetActiveBan(userId!.Value) : null;

            var blockedUserIds = isAuthenticated && !isAdmin
                ? _context.UserBlocks
                    .Where(x => x.BlockerUserId == userId!.Value &&
                                x.BlockedUser.Role != "Admin" &&
                                x.BlockedUser.Role != "Moderator")
                    .Select(x => x.BlockedUserId)
                    .ToList()
                : new List<int>();

            var query = _context.BlogPosts
                .Include(x => x.User)
                .Include(x => x.Comments)
                .Include(x => x.Likes)
                .AsSplitQuery()
                .Where(x => !blockedUserIds.Contains(x.UserId));

            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim();
                query = query.Where(x =>
                    x.Title.Contains(term) ||
                    x.Content.Contains(term));
            }

            if (!string.IsNullOrWhiteSpace(author))
            {
                var authorTerm = author.Trim();
                query = query.Where(x => x.User != null && x.User.UserName.Contains(authorTerm));
            }

            if (from.HasValue)
                query = query.Where(x => x.CreatedAt >= from.Value);

            if (to.HasValue)
                query = query.Where(x => x.CreatedAt <= to.Value);

            if (isAuthenticated && mine.HasValue && mine.Value)
                query = query.Where(x => x.UserId == userId.GetValueOrDefault());

            var filterMine = isAuthenticated && mine.HasValue && mine.Value;
            var categories = GetCategoriesForUi();
            var categorySlugs = categories
                .Select(x => x.Slug)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var defaultCategorySlug = categories.FirstOrDefault()?.Slug ?? "yasam";

            var normalizedQuick = NormalizeQuickFilter(quick);
            var now = DateTime.Now;
            if (normalizedQuick == "24h")
            {
                var start = now.AddHours(-24);
                query = query.Where(x => x.CreatedAt >= start);
            }
            else if (normalizedQuick == "week")
            {
                var start = now.AddDays(-7);
                query = query.Where(x => x.CreatedAt >= start);
            }
            else if (normalizedQuick == "flagged")
            {
                query = query.Where(x =>
                    x.Comments.Count >= 8 ||
                    x.Title.Contains("spam") || x.Content.Contains("spam") ||
                    x.Title.Contains("hakaret") || x.Content.Contains("hakaret"));
            }

            var queryBeforeCategoryFilter = query;
            var normalizedCategory = NormalizeCategory(category, categorySlugs, "all");
            if (!string.Equals(normalizedCategory, "all", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(x => (x.CategorySlug ?? defaultCategorySlug) == normalizedCategory);
            }

            var normalizedSort = NormalizeFeedSort(sort);
            query = normalizedSort switch
            {
                "popular" => query
                    .OrderByDescending(x => x.Likes.Count)
                    .ThenByDescending(x => x.Comments.Count)
                    .ThenByDescending(x => x.CreatedAt),
                "commented" => query
                    .OrderByDescending(x => x.Comments.Count)
                    .ThenByDescending(x => x.Likes.Count)
                    .ThenByDescending(x => x.CreatedAt),
                "old" => query.OrderBy(x => x.CreatedAt),
                _ => query.OrderByDescending(x => x.CreatedAt)
            };

            var blogs = query.ToList();
            var featuredPost = blogs
                .OrderByDescending(x => (x.Likes?.Count ?? 0) + (x.Comments?.Count ?? 0))
                .ThenByDescending(x => x.CreatedAt)
                .FirstOrDefault();

            var risingPosts = blogs
                .Where(x => x.CreatedAt >= now.AddDays(-3))
                .OrderByDescending(x =>
                {
                    var ageHours = Math.Max((now - x.CreatedAt).TotalHours, 1);
                    var score = (x.Likes?.Count ?? 0) + ((x.Comments?.Count ?? 0) * 2);
                    return score / ageHours;
                })
                .ThenByDescending(x => x.CreatedAt)
                .Take(4)
                .ToList();

            var categoryCounts = queryBeforeCategoryFilter
                .Select(x => x.CategorySlug)
                .ToList()
                .GroupBy(x => NormalizeCategory(x, categorySlugs, defaultCategorySlug))
                .ToDictionary(x => x.Key, x => x.Count(), StringComparer.OrdinalIgnoreCase);

            ViewBag.UserId = userId;
            ViewBag.IsAuthenticated = isAuthenticated;
            ViewBag.IsAdmin = isAdmin;
            ViewBag.FilterQuery = q ?? string.Empty;
            ViewBag.FilterAuthor = author ?? string.Empty;
            ViewBag.FilterFrom = from?.ToString("yyyy-MM-dd") ?? string.Empty;
            ViewBag.FilterTo = to?.ToString("yyyy-MM-dd") ?? string.Empty;
            ViewBag.FilterMine = filterMine;
            ViewBag.FilterSort = normalizedSort;
            ViewBag.FilterQuick = normalizedQuick;
            ViewBag.FilterCategory = normalizedCategory;
            ViewBag.ActiveBan = activeBan;
            ViewBag.FeaturedPost = featuredPost;
            ViewBag.RisingPosts = risingPosts;
            ViewBag.CategoryCounts = categoryCounts;
            ViewBag.Categories = categories;
            ViewBag.LikedBlogIds = isAuthenticated
                ? _context.BlogLikes
                    .Where(x => x.UserId == userId!.Value)
                    .Select(x => x.BlogPostId)
                    .ToHashSet()
                : new HashSet<int>();

            return View(blogs);
        }

        public IActionResult Details(int id, string? sort, string? returnUrl = null)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            var isAdmin = IsAdmin();

            if (userId == null)
                return RedirectToAction("Login", "Account");
            var activeBan = GetActiveBan(userId.Value);

            var blockedUserIds = isAdmin
                ? new List<int>()
                : _context.UserBlocks
                    .Where(x => x.BlockerUserId == userId.Value &&
                                x.BlockedUser.Role != "Admin" &&
                                x.BlockedUser.Role != "Moderator")
                    .Select(x => x.BlockedUserId)
                    .ToList();

            var blog = _context.BlogPosts
                .Include(x => x.User)
                .Include(x => x.Comments)
                .ThenInclude(x => x.User)
                .Include(x => x.Comments)
                .ThenInclude(x => x.Likes)
                .Include(x => x.Likes)
                .AsSplitQuery()
                .FirstOrDefault(x => x.Id == id);

            if (blog == null)
                return NotFound();

            if (blockedUserIds.Contains(blog.UserId))
            {
                TempData["ErrorMessage"] = "Bu kullanıcıyı engellediğin için gönderiyi göremezsin.";
                return RedirectToAction("Index");
            }

            var filteredComments = (blog.Comments ?? new List<Comment>())
                .Where(x => !blockedUserIds.Contains(x.UserId));

            if (string.Equals(sort, "old", StringComparison.OrdinalIgnoreCase))
            {
                filteredComments = filteredComments.OrderBy(x => x.CreatedAt);
            }
            else if (string.Equals(sort, "top", StringComparison.OrdinalIgnoreCase))
            {
                filteredComments = filteredComments
                    .OrderByDescending(x => x.Likes != null ? x.Likes.Count : 0)
                    .ThenByDescending(x => x.CreatedAt);
            }
            else
            {
                filteredComments = filteredComments.OrderByDescending(x => x.CreatedAt);
            }

            blog.Comments = filteredComments.ToList();

            ViewBag.UserId = userId.Value;
            ViewBag.IsAdmin = isAdmin;
            ViewBag.CurrentUserId = userId.Value;
            ViewBag.CommentSort = string.Equals(sort, "old", StringComparison.OrdinalIgnoreCase)
                ? "old"
                : (string.Equals(sort, "top", StringComparison.OrdinalIgnoreCase) ? "top" : "new");
            ViewBag.HasLiked = blog.Likes != null && blog.Likes.Any(x => x.UserId == userId.Value);
            ViewBag.ActiveBan = activeBan;
            ViewBag.LikedCommentIds = _context.CommentLikes
                .Where(x => x.UserId == userId.Value)
                .Select(x => x.CommentId)
                .ToHashSet();
            ViewBag.CanModerateComments = blog.UserId == userId.Value || isAdmin;
            ViewBag.ReturnUrl = !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)
                ? returnUrl
                : Url.Action("Index", "Blog");

            return View(blog);
        }

        [HttpGet]
        public IActionResult SearchSuggestions(string? term)
        {
            if (string.IsNullOrWhiteSpace(term))
                return Json(Array.Empty<object>());

            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null)
                return Json(Array.Empty<object>());

            var value = term.Trim();
            if (value.Length < 2)
                return Json(Array.Empty<object>());

            var posts = _context.BlogPosts
                .Include(x => x.User)
                .Where(x =>
                    x.Title.Contains(value) ||
                    x.Content.Contains(value) ||
                    (x.User != null && x.User.UserName.Contains(value)))
                .OrderByDescending(x => x.CreatedAt)
                .Take(8)
                .ToList();

            var result = posts.Select(x => new
            {
                id = x.Id,
                title = x.Title,
                author = x.User?.UserName ?? "Kullanici",
                category = x.CategorySlug
            });

            return Json(result);
        }

        [HttpGet]
        public IActionResult Create()
        {
            var userId = HttpContext.Session.GetInt32("UserId");

            if (userId == null)
                return RedirectToAction("Login", "Account");

            var currentUser = GetCurrentSessionUser(userId.Value);
            if (currentUser == null)
                return ClearInvalidSessionAndRedirectToLogin();

            var activeBan = GetActiveBan(currentUser.Id);

            if (activeBan != null)
            {
                TempData["ErrorMessage"] = BuildBanMessage(activeBan, "yeni gönderi paylaşamazsın");
                return RedirectToAction("Index");
            }

            PopulateCategoryViewData();
            return View(new BlogCreateViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Create(BlogCreateViewModel model)
        {
            var userId = HttpContext.Session.GetInt32("UserId");

            if (userId == null)
                return RedirectToAction("Login", "Account");

            var currentUser = GetCurrentSessionUser(userId.Value);
            if (currentUser == null)
                return ClearInvalidSessionAndRedirectToLogin();

            var activeBan = GetActiveBan(currentUser.Id);

            if (activeBan != null)
            {
                TempData["ErrorMessage"] = BuildBanMessage(activeBan, "yeni gönderi paylaşamazsın");
                return RedirectToAction("Index");
            }

            var categories = GetCategoriesForUi();
            var categorySlugs = categories
                .Select(x => x.Slug)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var defaultCategorySlug = categories.FirstOrDefault()?.Slug ?? "yasam";
            PopulateCategoryViewData(categories);

            model.Title = (model.Title ?? string.Empty).Trim();
            model.Content = (model.Content ?? string.Empty).Trim();

            if (model.ImageWidthPercent < 25 || model.ImageWidthPercent > 100)
            {
                ModelState.Remove(nameof(model.ImageWidthPercent));
                model.ImageWidthPercent = NormalizeImageWidth(model.ImageWidthPercent == 0 ? 60 : model.ImageWidthPercent);
            }

            if (string.IsNullOrWhiteSpace(model.ImagePlacement))
            {
                ModelState.Remove(nameof(model.ImagePlacement));
                model.ImagePlacement = "Right";
            }

            if (!ModelState.IsValid)
                return View(model);

            string? imagePath = null;
            if (model.ImageFile != null)
            {
                if (!TrySaveImage(model.ImageFile, out imagePath, out var imageError))
                {
                    ModelState.AddModelError(nameof(model.ImageFile), imageError);
                    return View(model);
                }
            }

            var selectedCategory = ResolveCategoryFromMeta(model.MetaHidden, categorySlugs, defaultCategorySlug);

            var blog = new BlogPost
            {
                Title = TextFormat.SanitizeHtml(model.Title),
                Content = TextFormat.SanitizeHtml(model.Content),
                CategorySlug = selectedCategory,
                ImageUrl = imagePath,
                ImageWidthPercent = NormalizeImageWidth(model.ImageWidthPercent),
                ImagePlacement = NormalizeImagePlacement(model.ImagePlacement),
                UserId = currentUser.Id,
                CreatedAt = DateTime.Now
            };

            _context.BlogPosts.Add(blog);
            try
            {
                _context.SaveChanges();
            }
            catch (DbUpdateException ex) when (IsSqlForeignKeyConflict(ex))
            {
                _context.ChangeTracker.Clear();
                var recoveredUser = GetCurrentSessionUser(currentUser.Id);
                if (recoveredUser != null)
                {
                    blog.UserId = recoveredUser.Id;
                    _context.BlogPosts.Add(blog);
                    try
                    {
                        _context.SaveChanges();
                        TempData["SuccessMessage"] = "Blog yazÄ±sÄ± baÅŸarÄ±yla eklendi.";
                        return RedirectToAction("Index");
                    }
                    catch (DbUpdateException retryEx) when (IsSqlForeignKeyConflict(retryEx))
                    {
                        _context.ChangeTracker.Clear();
                    }
                }

                DeleteImageIfLocal(imagePath);
                return ClearInvalidSessionAndRedirectToLogin();
            }

            TempData["SuccessMessage"] = "Blog yazısı başarıyla eklendi.";
            return RedirectToAction("Index");
        }

        [HttpGet]
        public IActionResult Edit(int id)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            var isAdmin = IsAdmin();

            if (userId == null)
                return RedirectToAction("Login", "Account");

            var blog = _context.BlogPosts
                .FirstOrDefault(x => x.Id == id && (isAdmin || x.UserId == userId.Value));

            if (blog == null)
                return NotFound();

            PopulateCategoryViewData();
            return View(blog);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Edit(BlogPost blog, IFormFile? imageFile)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            var isAdmin = IsAdmin();

            if (userId == null)
                return RedirectToAction("Login", "Account");

            var existingBlog = _context.BlogPosts
                .FirstOrDefault(x => x.Id == blog.Id && (isAdmin || x.UserId == userId.Value));

            if (existingBlog == null)
                return NotFound();

            var categories = GetCategoriesForUi();
            var categorySlugs = categories
                .Select(x => NormalizeCategorySlug(x.Slug))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var defaultCategorySlug = categories.FirstOrDefault()?.Slug ?? "yasam";
            var selectedCategory = NormalizeCategory(blog.CategorySlug, categorySlugs, defaultCategorySlug);

            if (!ModelState.IsValid)
            {
                blog.CategorySlug = selectedCategory;
                PopulateCategoryViewData(categories);
                return View(blog);
            }

            existingBlog.Title = TextFormat.SanitizeHtml(blog.Title);
            existingBlog.Content = TextFormat.SanitizeHtml(blog.Content);
            existingBlog.CategorySlug = selectedCategory;
            existingBlog.ImageWidthPercent = NormalizeImageWidth(blog.ImageWidthPercent);
            existingBlog.ImagePlacement = NormalizeImagePlacement(blog.ImagePlacement);

            if (imageFile != null)
            {
                if (!TrySaveImage(imageFile, out var newImagePath, out var imageError))
                {
                    ModelState.AddModelError("ImageUrl", imageError);
                    blog.CategorySlug = selectedCategory;
                    PopulateCategoryViewData(categories);
                    return View(blog);
                }

                DeleteImageIfLocal(existingBlog.ImageUrl);
                existingBlog.ImageUrl = newImagePath;
            }
            else
            {
                existingBlog.ImageUrl = blog.ImageUrl;
            }

            existingBlog.UpdatedAt = DateTime.Now;

            _context.SaveChanges();

            TempData["SuccessMessage"] = "Blog yazısı başarıyla güncellendi.";
            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ToggleLike(int blogPostId, string returnAction = "Index", string? returnUrl = null)
        {
            var userId = HttpContext.Session.GetInt32("UserId");

            if (userId == null)
                return RedirectToAction("Login", "Account");

            var activeBan = GetActiveBan(userId.Value);
            if (activeBan != null)
            {
                TempData["ErrorMessage"] = BuildBanMessage(activeBan, "gönderi beğenemezsin");
                if (string.Equals(returnAction, "Details", StringComparison.OrdinalIgnoreCase))
                    return RedirectToAction("Details", new { id = blogPostId });
                if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
                    return LocalRedirect(returnUrl);
                return RedirectToAction("Index", null, $"post-{blogPostId}");
            }

            var blogExists = _context.BlogPosts.Any(x => x.Id == blogPostId);
            if (!blogExists)
                return NotFound();

            var existingLike = _context.BlogLikes
                .FirstOrDefault(x => x.BlogPostId == blogPostId && x.UserId == userId.Value);

            if (existingLike == null)
            {
                _context.BlogLikes.Add(new BlogLike
                {
                    BlogPostId = blogPostId,
                    UserId = userId.Value,
                    CreatedAt = DateTime.Now
                });
            }
            else
            {
                _context.BlogLikes.Remove(existingLike);
            }

            _context.SaveChanges();

            if (string.Equals(returnAction, "Details", StringComparison.OrdinalIgnoreCase))
                return RedirectToAction("Details", new { id = blogPostId });

            if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
                return LocalRedirect(returnUrl);

            return RedirectToAction("Index", null, $"post-{blogPostId}");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ToggleLikeAjax(int blogPostId)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null)
                return Unauthorized(new { message = "Oturum bulunamadı." });

            var activeBan = GetActiveBan(userId.Value);
            if (activeBan != null)
                return BadRequest(new { message = BuildBanMessage(activeBan, "gönderi beğenemezsin") });

            var blogExists = _context.BlogPosts.Any(x => x.Id == blogPostId);
            if (!blogExists)
                return NotFound(new { message = "Gönderi bulunamadı." });

            var existingLike = _context.BlogLikes
                .FirstOrDefault(x => x.BlogPostId == blogPostId && x.UserId == userId.Value);

            var liked = false;
            if (existingLike == null)
            {
                _context.BlogLikes.Add(new BlogLike
                {
                    BlogPostId = blogPostId,
                    UserId = userId.Value,
                    CreatedAt = DateTime.Now
                });
                liked = true;
            }
            else
            {
                _context.BlogLikes.Remove(existingLike);
            }

            _context.SaveChanges();
            var likeCount = _context.BlogLikes.Count(x => x.BlogPostId == blogPostId);

            return Json(new { liked, likeCount });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Delete(int id, string? returnUrl = null)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            var isAdmin = IsAdmin();

            if (userId == null)
                return RedirectToAction("Login", "Account");

            var blog = _context.BlogPosts
                .FirstOrDefault(x => x.Id == id && (isAdmin || x.UserId == userId.Value));

            if (blog == null)
                return NotFound();

            DeleteImageIfLocal(blog.ImageUrl);

            _context.BlogPosts.Remove(blog);
            _context.SaveChanges();
            TempData["SuccessMessage"] = "Blog yazısı başarıyla silindi.";

            if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
                return LocalRedirect(returnUrl);

            var referer = Request.Headers.Referer.ToString();
            if (Uri.TryCreate(referer, UriKind.Absolute, out var refererUri))
            {
                var localFromReferer = refererUri.PathAndQuery + refererUri.Fragment;
                if (Url.IsLocalUrl(localFromReferer))
                    return LocalRedirect(localFromReferer);
            }

            return RedirectToAction("Index");
        }

        public IActionResult MyPosts()
        {
            var userId = HttpContext.Session.GetInt32("UserId");

            if (userId == null)
                return RedirectToAction("Login", "Account");

            var myBlogs = _context.BlogPosts
                .Include(x => x.Comments)
                .Include(x => x.Likes)
                .Where(x => x.UserId == userId.Value)
                .OrderByDescending(x => x.CreatedAt)
                .ToList();

            return View(myBlogs);
        }

        private bool TrySaveImage(IFormFile file, out string? relativePath, out string errorMessage)
        {
            relativePath = null;
            errorMessage = string.Empty;

            if (file.Length <= 0)
            {
                errorMessage = "Seçilen dosya boş olamaz.";
                return false;
            }

            if (file.Length > MaxImageSizeBytes)
            {
                errorMessage = "Görsel boyutu en fazla 20 MB olabilir.";
                return false;
            }

            var extension = Path.GetExtension(file.FileName);
            if (string.IsNullOrWhiteSpace(extension) || !AllowedImageExtensions.Contains(extension))
            {
                errorMessage = "Sadece jpg, jpeg, png, webp veya gif yükleyebilirsin.";
                return false;
            }

            var webRootPath = _environment.WebRootPath;
            if (string.IsNullOrWhiteSpace(webRootPath))
                webRootPath = Path.Combine(_environment.ContentRootPath, "wwwroot");

            var uploadsDirectory = Path.Combine(webRootPath, "uploads");
            Directory.CreateDirectory(uploadsDirectory);

            var fileName = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
            var fullPath = Path.Combine(uploadsDirectory, fileName);

            using var stream = new FileStream(fullPath, FileMode.Create);
            file.CopyTo(stream);

            relativePath = $"/uploads/{fileName}";
            return true;
        }

        private static int NormalizeImageWidth(int value)
        {
            if (value < 25)
                return 25;
            if (value > 100)
                return 100;
            return value;
        }

        private static string NormalizeImagePlacement(string? value)
        {
            if (string.Equals(value, "Left", StringComparison.OrdinalIgnoreCase))
                return "Left";
            if (string.Equals(value, "Right", StringComparison.OrdinalIgnoreCase))
                return "Right";
            return "Top";
        }

        private void DeleteImageIfLocal(string? imageUrl)
        {
            if (string.IsNullOrWhiteSpace(imageUrl))
                return;

            if (!imageUrl.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase))
                return;

            var webRootPath = _environment.WebRootPath;
            if (string.IsNullOrWhiteSpace(webRootPath))
                webRootPath = Path.Combine(_environment.ContentRootPath, "wwwroot");

            var uploadsRoot = Path.GetFullPath(Path.Combine(webRootPath, "uploads"));
            var relativePath = imageUrl.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            var fullPath = Path.GetFullPath(Path.Combine(webRootPath, relativePath));

            if (!fullPath.StartsWith(uploadsRoot, StringComparison.OrdinalIgnoreCase))
                return;

            if (System.IO.File.Exists(fullPath))
                System.IO.File.Delete(fullPath);
        }

        private bool IsAdmin()
        {
            var role = HttpContext.Session.GetString("UserRole");
            return string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase)
                || string.Equals(role, "Moderator", StringComparison.OrdinalIgnoreCase);
        }

        private User? GetCurrentSessionUser(int userId)
        {
            var user = _context.Users.FirstOrDefault(x => x.Id == userId);
            if (user != null)
            {
                EnsureSessionMatchesUser(user);
                return user;
            }

            var sessionUserName = HttpContext.Session.GetString("UserName");
            if (string.IsNullOrWhiteSpace(sessionUserName))
            {
                var rememberedById = TryGetRememberedUserById();
                if (rememberedById == null)
                    return null;

                EnsureSessionMatchesUser(rememberedById);
                return rememberedById;
            }

            user = _context.Users.FirstOrDefault(x => x.UserName == sessionUserName);
            if (user == null)
            {
                var rememberedById = TryGetRememberedUserById();
                if (rememberedById == null)
                    return null;

                EnsureSessionMatchesUser(rememberedById);
                return rememberedById;
            }

            EnsureSessionMatchesUser(user);
            return user;
        }

        private User? TryGetRememberedUserById()
        {
            var rememberedIdRaw = Request.Cookies["remember_user_id"];
            if (!int.TryParse(rememberedIdRaw, out var rememberedId))
                return null;

            return _context.Users.FirstOrDefault(x => x.Id == rememberedId);
        }

        private void EnsureSessionMatchesUser(User user)
        {
            HttpContext.Session.SetInt32("UserId", user.Id);
            HttpContext.Session.SetString("UserName", user.UserName);
            HttpContext.Session.SetString("UserRole", string.IsNullOrWhiteSpace(user.Role) ? "User" : user.Role);
        }

        private IActionResult ClearInvalidSessionAndRedirectToLogin()
        {
            HttpContext.Session.Clear();
            Response.Cookies.Delete("remember_user_id");
            Response.Cookies.Delete("remember_user_name");
            Response.Cookies.Delete("remember_user_role");
            TempData["ErrorMessage"] = "Oturum bilgisi yenilendi. Lutfen tekrar giris yap.";
            return RedirectToAction("Login", "Account");
        }

        private static bool IsSqlForeignKeyConflict(DbUpdateException ex)
        {
            return ex.GetBaseException() is SqlException sqlException && sqlException.Number == 547;
        }

        private UserBan? GetActiveBan(int userId)
        {
            return _context.UserBans
                .Where(x => x.UserId == userId && x.ExpiresAt > DateTime.Now)
                .OrderByDescending(x => x.ExpiresAt)
                .FirstOrDefault();
        }

        private static string BuildBanMessage(UserBan ban, string blockedAction)
        {
            return $"Bu hesap banlı olduğu için {blockedAction}. Ban nedeni: \"{ban.Reason}\". Bitiş: {ban.ExpiresAt:dd.MM.yyyy HH:mm}.";
        }

        private static string NormalizeFeedSort(string? sort)
        {
            if (string.Equals(sort, "old", StringComparison.OrdinalIgnoreCase))
                return "old";
            if (string.Equals(sort, "popular", StringComparison.OrdinalIgnoreCase))
                return "popular";
            if (string.Equals(sort, "commented", StringComparison.OrdinalIgnoreCase))
                return "commented";
            return "new";
        }

        private static string NormalizeQuickFilter(string? quick)
        {
            if (string.Equals(quick, "24h", StringComparison.OrdinalIgnoreCase))
                return "24h";
            if (string.Equals(quick, "week", StringComparison.OrdinalIgnoreCase))
                return "week";
            if (string.Equals(quick, "flagged", StringComparison.OrdinalIgnoreCase))
                return "flagged";
            return "all";
        }

        private List<Category> GetCategoriesForUi()
        {
            var categories = _context.Categories
                .AsNoTracking()
                .OrderBy(x => x.Name)
                .ToList();

            if (categories.Count > 0)
                return categories;

            return DefaultCategories
                .Select(x => new Category
                {
                    Name = x.Name,
                    Slug = x.Slug,
                    CreatedAt = DateTime.Now
                })
                .ToList();
        }

        private void PopulateCategoryViewData(List<Category>? categories = null)
        {
            ViewBag.Categories = categories ?? GetCategoriesForUi();
        }

        private static string ResolveCategoryFromMeta(string? metaHidden, ISet<string> allowedSlugs, string fallbackSlug)
        {
            if (string.IsNullOrWhiteSpace(metaHidden))
                return fallbackSlug;

            try
            {
                using var doc = JsonDocument.Parse(metaHidden);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                    return fallbackSlug;

                if (!doc.RootElement.TryGetProperty("category", out var categoryElement))
                    return fallbackSlug;

                return NormalizeCategory(categoryElement.GetString(), allowedSlugs, fallbackSlug);
            }
            catch
            {
                return fallbackSlug;
            }
        }

        private static string NormalizeCategory(string? category, ISet<string> allowedSlugs, string fallbackValue)
        {
            var normalized = NormalizeCategorySlug(category);
            if (string.IsNullOrWhiteSpace(normalized))
                return fallbackValue;

            return allowedSlugs.Contains(normalized) ? normalized : fallbackValue;
        }

        private static string NormalizeCategorySlug(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var source = value.Trim().ToLowerInvariant();
            var sb = new StringBuilder(source.Length);
            var prevHyphen = false;

            foreach (var ch in source)
            {
                var mapped = ch switch
                {
                    'ç' => 'c',
                    'ğ' => 'g',
                    'ı' => 'i',
                    'ö' => 'o',
                    'ş' => 's',
                    'ü' => 'u',
                    _ => ch
                };

                if ((mapped >= 'a' && mapped <= 'z') || (mapped >= '0' && mapped <= '9'))
                {
                    sb.Append(mapped);
                    prevHyphen = false;
                }
                else if (mapped == '-' || char.IsWhiteSpace(mapped) || mapped == '/' || mapped == '_')
                {
                    if (!prevHyphen && sb.Length > 0)
                    {
                        sb.Append('-');
                        prevHyphen = true;
                    }
                }
            }

            return sb.ToString().Trim('-');
        }
    }
}
