using BireyselHesaplar.Data;
using BireyselHesaplar.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace BireyselHesaplar.Controllers
{
    public class AdminController : Controller
    {
        private readonly AppDbContext _context;

        public AdminController(AppDbContext context)
        {
            _context = context;
        }

        public IActionResult Index(string? logSearch, string? logAction, DateTime? logFrom, DateTime? logTo, int page = 1, string? panel = null)
        {
            if (!IsAdmin())
                return RedirectToAction("Login", "Account");

            var currentUserId = HttpContext.Session.GetInt32("UserId");

            ViewBag.UserCount = _context.Users.Count();
            ViewBag.PostCount = _context.BlogPosts.Count();
            ViewBag.CommentCount = _context.Comments.Count();
            ViewBag.BlockCount = _context.UserBans.Count(x => x.ExpiresAt > DateTime.Now);
            ViewBag.CurrentUserId = currentUserId;
            ViewBag.ActiveAdminPage = NormalizeAdminPanel(panel);

            ViewBag.Users = _context.Users
                .OrderByDescending(x => x.CreatedAt)
                .Take(200)
                .ToList();

            ViewBag.Posts = _context.BlogPosts
                .Include(x => x.User)
                .Include(x => x.Comments)
                .OrderByDescending(x => x.CreatedAt)
                .Take(200)
                .ToList();

            ViewBag.Comments = _context.Comments
                .Include(x => x.User)
                .Include(x => x.BlogPost)
                .OrderByDescending(x => x.CreatedAt)
                .Take(300)
                .ToList();

            var categories = _context.Categories
                .OrderBy(x => x.ParentCategoryId.HasValue ? 1 : 0)
                .ThenBy(x => x.Name)
                .ToList();

            var categoryCounts = _context.BlogPosts
                .GroupBy(x => string.IsNullOrWhiteSpace(x.CategorySlug) ? "yasam" : x.CategorySlug)
                .Select(x => new { Slug = x.Key, Count = x.Count() })
                .ToDictionary(
                    x => x.Slug,
                    x => x.Count,
                    StringComparer.OrdinalIgnoreCase);

            ViewBag.Categories = categories;
            ViewBag.CategoryCounts = categoryCounts;

            var userMap = _context.Users
                .ToDictionary(x => x.Id, x => x.UserName);

            ViewBag.UserMap = userMap;
            var logsQuery = _context.AdminActionLogs.AsQueryable();
            var trimmedSearch = (logSearch ?? string.Empty).Trim();

            if (!string.IsNullOrWhiteSpace(trimmedSearch))
            {
                var matchingUserIds = _context.Users
                    .Where(x => x.UserName.Contains(trimmedSearch))
                    .Select(x => x.Id);

                logsQuery = logsQuery.Where(x =>
                    x.ActionType.Contains(trimmedSearch) ||
                    (x.Reason != null && x.Reason.Contains(trimmedSearch)) ||
                    matchingUserIds.Contains(x.ActorUserId) ||
                    (x.TargetUserId.HasValue && matchingUserIds.Contains(x.TargetUserId.Value)));
            }

            if (!string.IsNullOrWhiteSpace(logAction))
                logsQuery = logsQuery.Where(x => x.ActionType == logAction);

            if (logFrom.HasValue)
                logsQuery = logsQuery.Where(x => x.CreatedAt >= logFrom.Value.Date);

            if (logTo.HasValue)
            {
                var toEnd = logTo.Value.Date.AddDays(1).AddTicks(-1);
                logsQuery = logsQuery.Where(x => x.CreatedAt <= toEnd);
            }

            const int pageSize = 30;
            var totalLogs = logsQuery.Count();
            var totalPages = totalLogs == 0 ? 1 : (int)Math.Ceiling(totalLogs / (double)pageSize);
            if (page < 1) page = 1;
            if (page > totalPages) page = totalPages;

            var logs = logsQuery
                .OrderByDescending(x => x.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            ViewBag.LogSearch = trimmedSearch;
            ViewBag.LogAction = logAction ?? string.Empty;
            ViewBag.LogFrom = logFrom?.ToString("yyyy-MM-dd") ?? string.Empty;
            ViewBag.LogTo = logTo?.ToString("yyyy-MM-dd") ?? string.Empty;
            ViewBag.LogPage = page;
            ViewBag.LogTotalPages = totalPages;
            ViewBag.LogActions = _context.AdminActionLogs
                .Select(x => x.ActionType)
                .Distinct()
                .OrderBy(x => x)
                .ToList();

            ViewData["FullWidth"] = true;
            ViewData["HideChrome"] = true;

            return View(logs);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult CreateSystemAnnouncement(string title, string message)
        {
            if (!IsAdmin())
                return RedirectToAction("Login", "Account");

            var actorUserId = HttpContext.Session.GetInt32("UserId");
            if (actorUserId == null)
                return RedirectToAction("Login", "Account");

            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(message))
            {
                TempData["ErrorMessage"] = "Bildirim başlığı ve içeriği zorunludur.";
                return RedirectToCurrentPanel();
            }

            var summary = $"{title.Trim()} - {message.Trim()}";
            if (summary.Length > 350)
                summary = summary.Substring(0, 350) + "...";

            _context.AdminActionLogs.Add(new AdminActionLog
            {
                ActorUserId = actorUserId.Value,
                ActionType = "SystemAnnouncementCreated",
                Reason = summary,
                CreatedAt = DateTime.Now
            });

            _context.SaveChanges();
            TempData["SuccessMessage"] = "Sistem bildirimi oluşturuldu.";
            return RedirectToCurrentPanel();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult AddModerator(int userId)
        {
            if (!IsAdmin())
                return RedirectToAction("Login", "Account");

            if (!IsSuperAdmin())
            {
                TempData["ErrorMessage"] = "Bu islemi sadece admin yapabilir.";
                return RedirectToCurrentPanel("system");
            }

            var actorUserId = HttpContext.Session.GetInt32("UserId");
            if (actorUserId == null)
                return RedirectToAction("Login", "Account");

            var user = _context.Users.FirstOrDefault(x => x.Id == userId);
            if (user == null)
                return NotFound();

            if (!IsSuperAdmin() && string.Equals(user.Role, "Admin", StringComparison.OrdinalIgnoreCase))
            {
                TempData["ErrorMessage"] = "Moderatorler admin kullanicilar uzerinde bu islemi yapamaz.";
                return RedirectToCurrentPanel("users");
            }

            if (string.Equals(user.Role, "Admin", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(user.Role, "Moderator", StringComparison.OrdinalIgnoreCase))
            {
                TempData["ErrorMessage"] = "Secilen kullanici zaten yetkili.";
                return RedirectToCurrentPanel("system");
            }

            user.Role = "Moderator";

            _context.AdminActionLogs.Add(new AdminActionLog
            {
                ActorUserId = actorUserId.Value,
                TargetUserId = userId,
                ActionType = "ModeratorAdded",
                Reason = "Hızlı eylem: Moderatör Ekle",
                CreatedAt = DateTime.Now
            });

            _context.SaveChanges();
            TempData["SuccessMessage"] = $"{user.UserName} moderatör olarak atandı.";
            return RedirectToCurrentPanel();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult RemoveModerator(int userId)
        {
            if (!IsAdmin())
                return RedirectToAction("Login", "Account");

            if (!IsSuperAdmin())
            {
                TempData["ErrorMessage"] = "Bu islemi sadece admin yapabilir.";
                return RedirectToCurrentPanel("system");
            }

            var actorUserId = HttpContext.Session.GetInt32("UserId");
            if (actorUserId == null)
                return RedirectToAction("Login", "Account");

            if (actorUserId.Value == userId)
            {
                TempData["ErrorMessage"] = "Kendi moderatör yetkini bu ekrandan kaldıramazsın.";
                return RedirectToCurrentPanel();
            }

            var user = _context.Users.FirstOrDefault(x => x.Id == userId);
            if (user == null)
                return NotFound();

            if (!IsSuperAdmin() && string.Equals(user.Role, "Admin", StringComparison.OrdinalIgnoreCase))
            {
                TempData["ErrorMessage"] = "Moderatorler admin kullanicilar uzerinde bu islemi yapamaz.";
                return RedirectToAction("UserProfile", "Account", new { username = user.UserName });
            }

            if (!string.Equals(user.Role, "Moderator", StringComparison.OrdinalIgnoreCase))
            {
                TempData["ErrorMessage"] = "Seçilen kullanıcı zaten moderatör değil.";
                return RedirectToCurrentPanel();
            }

            user.Role = "User";

            _context.AdminActionLogs.Add(new AdminActionLog
            {
                ActorUserId = actorUserId.Value,
                TargetUserId = userId,
                ActionType = "ModeratorRemoved",
                Reason = "Hızlı eylem: Moderatör yetkisi geri alındı",
                CreatedAt = DateTime.Now
            });

            _context.SaveChanges();
            TempData["SuccessMessage"] = $"{user.UserName} kullanıcısının moderatör yetkisi kaldırıldı.";
            return RedirectToCurrentPanel();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult OpenFeaturedEditor(int postId)
        {
            if (!IsAdmin())
                return RedirectToAction("Login", "Account");

            var actorUserId = HttpContext.Session.GetInt32("UserId");
            if (actorUserId == null)
                return RedirectToAction("Login", "Account");

            var post = _context.BlogPosts.FirstOrDefault(x => x.Id == postId);
            if (post == null)
            {
                TempData["ErrorMessage"] = "Seçilen gönderi bulunamadı.";
                return RedirectToCurrentPanel();
            }

            _context.AdminActionLogs.Add(new AdminActionLog
            {
                ActorUserId = actorUserId.Value,
                BlogPostId = postId,
                TargetUserId = post.UserId,
                ActionType = "FeaturedPostEditOpened",
                Reason = "Hızlı eylem: Öne çıkan yazı düzenleme",
                CreatedAt = DateTime.Now
            });

            _context.SaveChanges();
            TempData["SuccessMessage"] = "Öne çıkan içerik düzenleme ekranına yönlendiriliyorsun.";
            return RedirectToAction("Edit", "Blog", new { id = postId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult AddCategory(string name, int? parentCategoryId)
        {
            if (!IsAdmin())
                return RedirectToAction("Login", "Account");

            var actorUserId = HttpContext.Session.GetInt32("UserId");
            if (actorUserId == null)
                return RedirectToAction("Login", "Account");

            var trimmedName = (name ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(trimmedName))
            {
                TempData["ErrorMessage"] = "Kategori adi bos olamaz.";
                return RedirectToCurrentPanel("categories");
            }

            if (!parentCategoryId.HasValue)
            {
                var delimiterIndex = trimmedName.IndexOf('/');
                if (delimiterIndex <= 0)
                    delimiterIndex = trimmedName.IndexOf('>');

                if (delimiterIndex > 0)
                {
                    var parentName = trimmedName.Substring(0, delimiterIndex).Trim();
                    var childName = trimmedName.Substring(delimiterIndex + 1).Trim();
                    if (!string.IsNullOrWhiteSpace(parentName) && !string.IsNullOrWhiteSpace(childName))
                    {
                        var parentSlug = NormalizeCategorySlug(parentName);
                        var matchedParent = _context.Categories.FirstOrDefault(x => x.Slug == parentSlug);
                        if (matchedParent != null)
                        {
                            parentCategoryId = matchedParent.Id;
                            trimmedName = childName;
                        }
                    }
                }
            }

            var slug = NormalizeCategorySlug(trimmedName);
            if (string.IsNullOrWhiteSpace(slug))
            {
                TempData["ErrorMessage"] = "Kategori adi gecerli karakterler icermiyor.";
                return RedirectToCurrentPanel("categories");
            }

            if (parentCategoryId.HasValue && !_context.Categories.Any(x => x.Id == parentCategoryId.Value))
            {
                TempData["ErrorMessage"] = "Secilen ana kategori bulunamadi.";
                return RedirectToCurrentPanel("categories");
            }

            var exists = _context.Categories.Any(x => x.Slug == slug);
            if (exists)
            {
                TempData["ErrorMessage"] = "Bu kategori zaten var.";
                return RedirectToCurrentPanel("categories");
            }

            var category = new Category
            {
                Name = trimmedName,
                Slug = slug,
                ParentCategoryId = parentCategoryId,
                CreatedAt = DateTime.Now
            };

            _context.Categories.Add(category);
            _context.AdminActionLogs.Add(new AdminActionLog
            {
                ActorUserId = actorUserId.Value,
                ActionType = "CategoryCreated",
                Reason = parentCategoryId.HasValue
                    ? $"Kategori eklendi: {trimmedName} (ust: {parentCategoryId.Value})"
                    : $"Kategori eklendi: {trimmedName}",
                CreatedAt = DateTime.Now
            });

            _context.SaveChanges();

            TempData["SuccessMessage"] = "Kategori eklendi.";
            return RedirectToCurrentPanel("categories");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteCategory(int categoryId)
        {
            if (!IsAdmin())
                return RedirectToAction("Login", "Account");

            var actorUserId = HttpContext.Session.GetInt32("UserId");
            if (actorUserId == null)
                return RedirectToAction("Login", "Account");

            var category = _context.Categories.FirstOrDefault(x => x.Id == categoryId);
            if (category == null)
            {
                TempData["ErrorMessage"] = "Kategori bulunamadi.";
                return RedirectToCurrentPanel("categories");
            }

            var allCategories = _context.Categories
                .OrderBy(x => x.Name)
                .ToList();

            if (allCategories.Count <= 1)
            {
                TempData["ErrorMessage"] = "Son kalan kategori silinemez.";
                return RedirectToCurrentPanel("categories");
            }

            var fallbackCategory = category.ParentCategoryId.HasValue
                ? allCategories.FirstOrDefault(x => x.Id == category.ParentCategoryId.Value)
                : allCategories.FirstOrDefault(x => x.Id != categoryId);
            if (fallbackCategory == null)
            {
                TempData["ErrorMessage"] = "Yedek kategori bulunamadi.";
                return RedirectToCurrentPanel("categories");
            }

            var childCategories = _context.Categories
                .Where(x => x.ParentCategoryId == categoryId)
                .ToList();

            foreach (var child in childCategories)
            {
                child.ParentCategoryId = null;
            }

            var affectedPosts = _context.BlogPosts
                .Where(x => x.CategorySlug == category.Slug)
                .ToList();

            foreach (var post in affectedPosts)
            {
                post.CategorySlug = fallbackCategory.Slug;
            }

            _context.Categories.Remove(category);
            _context.AdminActionLogs.Add(new AdminActionLog
            {
                ActorUserId = actorUserId.Value,
                ActionType = "CategoryDeleted",
                Reason = $"Kategori silindi: {category.Name}. Tasinan gonderi: {affectedPosts.Count}",
                CreatedAt = DateTime.Now
            });

            _context.SaveChanges();

            TempData["SuccessMessage"] = "Kategori silindi.";
            return RedirectToCurrentPanel("categories");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult UpdateCategoryParent(int categoryId, int? parentCategoryId)
        {
            if (!IsAdmin())
                return RedirectToAction("Login", "Account");

            var actorUserId = HttpContext.Session.GetInt32("UserId");
            if (actorUserId == null)
                return RedirectToAction("Login", "Account");

            var category = _context.Categories.FirstOrDefault(x => x.Id == categoryId);
            if (category == null)
            {
                TempData["ErrorMessage"] = "Kategori bulunamadi.";
                return RedirectToCurrentPanel("categories");
            }

            if (parentCategoryId.HasValue)
            {
                if (parentCategoryId.Value == categoryId)
                {
                    TempData["ErrorMessage"] = "Kategori kendisinin altina tasinamaz.";
                    return RedirectToCurrentPanel("categories");
                }

                var parent = _context.Categories.FirstOrDefault(x => x.Id == parentCategoryId.Value);
                if (parent == null)
                {
                    TempData["ErrorMessage"] = "Secilen ana kategori bulunamadi.";
                    return RedirectToCurrentPanel("categories");
                }

                if (parent.ParentCategoryId == categoryId)
                {
                    TempData["ErrorMessage"] = "Karsilikli alt kategori iliskisi olusturulamaz.";
                    return RedirectToCurrentPanel("categories");
                }
            }

            category.ParentCategoryId = parentCategoryId;

            _context.AdminActionLogs.Add(new AdminActionLog
            {
                ActorUserId = actorUserId.Value,
                ActionType = "CategoryParentChanged",
                Reason = parentCategoryId.HasValue
                    ? $"Kategori tasindi: {category.Name} -> {parentCategoryId.Value}"
                    : $"Kategori ust baglantisi kaldirildi: {category.Name}",
                CreatedAt = DateTime.Now
            });

            _context.SaveChanges();
            TempData["SuccessMessage"] = "Kategori konumu guncellendi.";
            return RedirectToCurrentPanel("categories");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ChangeUserRole(int userId, string role, string reason)
        {
            if (!IsAdmin())
                return RedirectToAction("Login", "Account");

            if (!IsSuperAdmin())
            {
                TempData["ErrorMessage"] = "Rol degistirme islemini sadece admin yapabilir.";
                return RedirectToCurrentPanel("users");
            }

            var actorUserId = HttpContext.Session.GetInt32("UserId");
            if (actorUserId == null)
                return RedirectToAction("Login", "Account");

            var finalReason = string.IsNullOrWhiteSpace(reason) ? "Admin işlemi" : reason.Trim();

            var normalizedRole = "User";
            if (string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase))
                normalizedRole = "Admin";
            else if (string.Equals(role, "Moderator", StringComparison.OrdinalIgnoreCase))
                normalizedRole = "Moderator";
            var user = _context.Users.FirstOrDefault(x => x.Id == userId);
            if (user == null)
                return NotFound();

            if (!IsSuperAdmin() && string.Equals(user.Role, "Admin", StringComparison.OrdinalIgnoreCase))
            {
                TempData["ErrorMessage"] = "Moderatorler admin kullanicilar uzerinde bu islemi yapamaz.";
                return RedirectToAction("UserProfile", "Account", new { username = user.UserName });
            }

            if (user.Id == actorUserId.Value && !string.Equals(normalizedRole, "Admin", StringComparison.OrdinalIgnoreCase))
            {
                TempData["ErrorMessage"] = "Kendi admin rolünü kaldıramazsın.";
                return RedirectToCurrentPanel();
            }

            user.Role = normalizedRole;

            _context.AdminActionLogs.Add(new AdminActionLog
            {
                ActorUserId = actorUserId.Value,
                TargetUserId = user.Id,
                ActionType = "UserRoleChanged",
                Reason = finalReason,
                CreatedAt = DateTime.Now
            });

            _context.SaveChanges();

            TempData["SuccessMessage"] = "Kullanıcı rolü güncellendi.";
            return RedirectToCurrentPanel();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DeletePost(int postId, string reason)
        {
            if (!IsAdmin())
                return RedirectToAction("Login", "Account");

            var actorUserId = HttpContext.Session.GetInt32("UserId");
            if (actorUserId == null)
                return RedirectToAction("Login", "Account");

            var finalReason = string.IsNullOrWhiteSpace(reason) ? "Admin işlemi" : reason.Trim();

            var post = _context.BlogPosts.FirstOrDefault(x => x.Id == postId);
            if (post == null)
                return NotFound();

            _context.AdminActionLogs.Add(new AdminActionLog
            {
                ActorUserId = actorUserId.Value,
                TargetUserId = post.UserId,
                BlogPostId = postId,
                ActionType = "PostDeletedByAdmin",
                Reason = finalReason,
                CreatedAt = DateTime.Now
            });

            _context.BlogPosts.Remove(post);
            _context.SaveChanges();

            TempData["SuccessMessage"] = "Gönderi silindi.";
            return RedirectToCurrentPanel("content");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteComment(int commentId, string reason)
        {
            if (!IsAdmin())
                return RedirectToAction("Login", "Account");

            var actorUserId = HttpContext.Session.GetInt32("UserId");
            if (actorUserId == null)
                return RedirectToAction("Login", "Account");

            var finalReason = string.IsNullOrWhiteSpace(reason) ? "Admin işlemi" : reason.Trim();

            var targetComment = _context.Comments.FirstOrDefault(x => x.Id == commentId);
            if (targetComment == null)
                return NotFound();

            var commentRows = _context.Comments
                .Select(x => new { x.Id, x.ParentCommentId })
                .ToList();

            var idsToDelete = new HashSet<int> { commentId };
            var queue = new Queue<int>();
            queue.Enqueue(commentId);

            while (queue.Count > 0)
            {
                var currentId = queue.Dequeue();
                var children = commentRows
                    .Where(x => x.ParentCommentId == currentId)
                    .Select(x => x.Id)
                    .ToList();

                foreach (var childId in children)
                {
                    if (idsToDelete.Add(childId))
                        queue.Enqueue(childId);
                }
            }

            var commentsToDelete = _context.Comments
                .Where(x => idsToDelete.Contains(x.Id))
                .ToList();

            _context.AdminActionLogs.Add(new AdminActionLog
            {
                ActorUserId = actorUserId.Value,
                TargetUserId = targetComment.UserId,
                BlogPostId = targetComment.BlogPostId,
                CommentId = commentId,
                ActionType = "CommentDeletedByAdmin",
                Reason = finalReason,
                CreatedAt = DateTime.Now
            });

            _context.Comments.RemoveRange(commentsToDelete);
            _context.SaveChanges();

            TempData["SuccessMessage"] = "Yorum silindi.";
            return RedirectToCurrentPanel("content");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteUser(int userId, string reason)
        {
            if (!IsAdmin())
                return RedirectToAction("Login", "Account");

            var actorUserId = HttpContext.Session.GetInt32("UserId");
            if (actorUserId == null)
                return RedirectToAction("Login", "Account");

            var finalReason = string.IsNullOrWhiteSpace(reason) ? "Admin işlemi" : reason.Trim();

            if (actorUserId.Value == userId)
            {
                TempData["ErrorMessage"] = "Kendini silemezsin.";
                return RedirectToCurrentPanel();
            }

            var user = _context.Users.FirstOrDefault(x => x.Id == userId);
            if (user == null)
                return NotFound();

            if (!IsSuperAdmin() && string.Equals(user.Role, "Admin", StringComparison.OrdinalIgnoreCase))
            {
                TempData["ErrorMessage"] = "Moderatorler admin kullanicilar uzerinde bu islemi yapamaz.";
                return RedirectToCurrentPanel("users");
            }

            var commentRows = _context.Comments
                .Select(x => new { x.Id, x.ParentCommentId, x.UserId })
                .ToList();

            var idsToDelete = commentRows
                .Where(x => x.UserId == userId)
                .Select(x => x.Id)
                .ToHashSet();

            var queue = new Queue<int>(idsToDelete);
            while (queue.Count > 0)
            {
                var currentId = queue.Dequeue();
                var children = commentRows
                    .Where(x => x.ParentCommentId == currentId)
                    .Select(x => x.Id)
                    .ToList();

                foreach (var childId in children)
                {
                    if (idsToDelete.Add(childId))
                        queue.Enqueue(childId);
                }
            }

            var commentsToDelete = _context.Comments
                .Where(x => idsToDelete.Contains(x.Id))
                .ToList();

            var postsToDelete = _context.BlogPosts.Where(x => x.UserId == userId).ToList();
            var userBlocks = _context.UserBlocks.Where(x => x.BlockerUserId == userId || x.BlockedUserId == userId).ToList();
            var blogLikes = _context.BlogLikes.Where(x => x.UserId == userId).ToList();
            var commentLikes = _context.CommentLikes.Where(x => x.UserId == userId).ToList();

            _context.AdminActionLogs.Add(new AdminActionLog
            {
                ActorUserId = actorUserId.Value,
                TargetUserId = userId,
                ActionType = "UserDeletedByAdmin",
                Reason = finalReason,
                CreatedAt = DateTime.Now
            });

            _context.CommentLikes.RemoveRange(commentLikes);
            _context.BlogLikes.RemoveRange(blogLikes);
            _context.UserBlocks.RemoveRange(userBlocks);
            _context.Comments.RemoveRange(commentsToDelete);
            _context.BlogPosts.RemoveRange(postsToDelete);
            _context.Users.Remove(user);

            _context.SaveChanges();

            TempData["SuccessMessage"] = "Kullanıcı ve ilişkili verileri silindi.";
            return RedirectToCurrentPanel("users");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult BanUser(int userId, int durationDays, string reason)
        {
            if (!IsAdmin())
                return RedirectToAction("Login", "Account");

            var actorUserId = HttpContext.Session.GetInt32("UserId");
            if (actorUserId == null)
                return RedirectToAction("Login", "Account");

            if (string.IsNullOrWhiteSpace(reason))
            {
                TempData["ErrorMessage"] = "Ban için açıklama zorunludur.";
                return RedirectToAction("UserProfile", "Account", new { username = _context.Users.Where(x => x.Id == userId).Select(x => x.UserName).FirstOrDefault() });
            }

            if (durationDays <= 0)
            {
                TempData["ErrorMessage"] = "Ban süresi geçersiz.";
                return RedirectToAction("UserProfile", "Account", new { username = _context.Users.Where(x => x.Id == userId).Select(x => x.UserName).FirstOrDefault() });
            }

            var user = _context.Users.FirstOrDefault(x => x.Id == userId);
            if (user == null)
                return NotFound();

            if (!IsSuperAdmin() && string.Equals(user.Role, "Admin", StringComparison.OrdinalIgnoreCase))
            {
                TempData["ErrorMessage"] = "Moderatorler admin kullanicilar uzerinde bu islemi yapamaz.";
                return RedirectToAction("UserProfile", "Account", new { username = user.UserName });
            }

            var activeBans = _context.UserBans.Where(x => x.UserId == userId && x.ExpiresAt > DateTime.Now).ToList();
            if (activeBans.Any())
                _context.UserBans.RemoveRange(activeBans);

            var expiresAt = DateTime.Now.AddDays(durationDays);
            _context.UserBans.Add(new UserBan
            {
                UserId = userId,
                AdminUserId = actorUserId.Value,
                Reason = reason.Trim(),
                ExpiresAt = expiresAt,
                CreatedAt = DateTime.Now
            });

            _context.AdminActionLogs.Add(new AdminActionLog
            {
                ActorUserId = actorUserId.Value,
                TargetUserId = userId,
                ActionType = "UserBanned",
                Reason = reason.Trim(),
                CreatedAt = DateTime.Now
            });

            _context.SaveChanges();
            TempData["SuccessMessage"] = "Kullanıcı banlandı.";
            return RedirectToAction("UserProfile", "Account", new { username = user.UserName });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult UnbanUser(int userId)
        {
            if (!IsAdmin())
                return RedirectToAction("Login", "Account");

            var actorUserId = HttpContext.Session.GetInt32("UserId");
            if (actorUserId == null)
                return RedirectToAction("Login", "Account");

            var user = _context.Users.FirstOrDefault(x => x.Id == userId);
            if (user == null)
                return NotFound();

            if (!IsSuperAdmin() && string.Equals(user.Role, "Admin", StringComparison.OrdinalIgnoreCase))
            {
                TempData["ErrorMessage"] = "Moderatorler admin kullanicilar uzerinde bu islemi yapamaz.";
                return RedirectToAction("UserProfile", "Account", new { username = user.UserName });
            }

            var activeBans = _context.UserBans.Where(x => x.UserId == userId && x.ExpiresAt > DateTime.Now).ToList();
            if (activeBans.Any())
                _context.UserBans.RemoveRange(activeBans);

            _context.AdminActionLogs.Add(new AdminActionLog
            {
                ActorUserId = actorUserId.Value,
                TargetUserId = userId,
                ActionType = "UserUnbanned",
                Reason = "Ban kaldırıldı",
                CreatedAt = DateTime.Now
            });

            _context.SaveChanges();
            TempData["SuccessMessage"] = "Ban kaldırıldı.";
            return RedirectToAction("UserProfile", "Account", new { username = user.UserName });
        }

        private IActionResult RedirectToCurrentPanel(string fallbackPanel = "overview")
        {
            var panel = Request.Query["panel"].ToString();
            if (string.IsNullOrWhiteSpace(panel) && Request.HasFormContentType)
                panel = Request.Form["panel"].ToString();

            if (string.IsNullOrWhiteSpace(panel))
                panel = fallbackPanel;

            return RedirectToAction("Index", new { panel = NormalizeAdminPanel(panel) });
        }

        private static string NormalizeAdminPanel(string? panel)
        {
            if (string.Equals(panel, "users", StringComparison.OrdinalIgnoreCase))
                return "users";
            if (string.Equals(panel, "content", StringComparison.OrdinalIgnoreCase))
                return "content";
            if (string.Equals(panel, "categories", StringComparison.OrdinalIgnoreCase))
                return "categories";
            if (string.Equals(panel, "analytics", StringComparison.OrdinalIgnoreCase))
                return "analytics";
            if (string.Equals(panel, "system", StringComparison.OrdinalIgnoreCase))
                return "system";
            return "overview";
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

        private bool IsAdmin()
        {
            var role = HttpContext.Session.GetString("UserRole");
            return string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase)
                || string.Equals(role, "Moderator", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsSuperAdmin()
        {
            var role = HttpContext.Session.GetString("UserRole");
            return string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase);
        }
    }
}
