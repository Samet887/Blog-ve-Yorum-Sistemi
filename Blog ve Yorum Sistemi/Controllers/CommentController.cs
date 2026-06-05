using Microsoft.AspNetCore.Mvc;
using BireyselHesaplar.Data;
using BireyselHesaplar.Models;

namespace BireyselHesaplar.Controllers
{
    public class CommentController : Controller
    {
        private readonly AppDbContext _context;
        private const int MaxReplyDepth = 3;

        public CommentController(AppDbContext context)
        {
            _context = context;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Create(int blogPostId, string content, int? parentCommentId = null, string? returnUrl = null)
        {
            var userId = HttpContext.Session.GetInt32("UserId");

            if (userId == null)
                return RedirectToAction("Login", "Account");

            var activeBan = GetActiveBan(userId.Value);
            if (activeBan != null)
            {
                TempData["ErrorMessage"] = BuildBanMessage(activeBan, "yorum yapamazsın");
                return RedirectToBlogDetails(blogPostId, returnUrl);
            }

            if (string.IsNullOrWhiteSpace(content))
            {
                TempData["ErrorMessage"] = "Yorum boş olamaz.";
                return RedirectToBlogDetails(blogPostId, returnUrl);
            }

            if (parentCommentId.HasValue)
            {
                var parentComment = _context.Comments
                    .FirstOrDefault(x => x.Id == parentCommentId.Value && x.BlogPostId == blogPostId);

                if (parentComment == null)
                {
                    TempData["ErrorMessage"] = "Yanıtlanacak yorum bulunamadı.";
                    return RedirectToBlogDetails(blogPostId, returnUrl);
                }

                var parentDepth = GetCommentDepth(parentComment);
                if (parentDepth >= MaxReplyDepth)
                {
                    TempData["ErrorMessage"] = $"En fazla {MaxReplyDepth} seviye iç içe yanıt verebilirsin.";
                    return RedirectToBlogDetails(blogPostId, returnUrl);
                }
            }

            var comment = new Comment
            {
                BlogPostId = blogPostId,
                UserId = userId.Value,
                Content = content,
                ParentCommentId = parentCommentId,
                CreatedAt = DateTime.Now
            };

            _context.Comments.Add(comment);
            _context.SaveChanges();

            TempData["SuccessMessage"] = parentCommentId.HasValue
                ? "Yanıt başarıyla eklendi."
                : "Yorum başarıyla eklendi.";
            return RedirectToBlogDetails(blogPostId, returnUrl);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ToggleLike(int blogPostId, int commentId, string? returnUrl = null)
        {
            var userId = HttpContext.Session.GetInt32("UserId");

            if (userId == null)
                return RedirectToAction("Login", "Account");

            var activeBan = GetActiveBan(userId.Value);
            if (activeBan != null)
            {
                TempData["ErrorMessage"] = BuildBanMessage(activeBan, "yorum beğenemezsin");
                return RedirectToBlogDetails(blogPostId, returnUrl);
            }

            var comment = _context.Comments.FirstOrDefault(x => x.Id == commentId && x.BlogPostId == blogPostId);
            if (comment == null)
                return NotFound();

            var existingLike = _context.CommentLikes
                .FirstOrDefault(x => x.CommentId == commentId && x.UserId == userId.Value);

            if (existingLike == null)
            {
                _context.CommentLikes.Add(new CommentLike
                {
                    CommentId = commentId,
                    UserId = userId.Value,
                    CreatedAt = DateTime.Now
                });
            }
            else
            {
                _context.CommentLikes.Remove(existingLike);
            }

            _context.SaveChanges();

            return RedirectToBlogDetails(blogPostId, returnUrl);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Delete(int blogPostId, int commentId, string reason, string? returnUrl = null)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            var role = HttpContext.Session.GetString("UserRole");
            var isAdmin = string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase)
                || string.Equals(role, "Moderator", StringComparison.OrdinalIgnoreCase);
            if (userId == null)
                return RedirectToAction("Login", "Account");

            if (!isAdmin && string.IsNullOrWhiteSpace(reason))
            {
                TempData["ErrorMessage"] = "Yorum silmek için açıklama yazmalısın.";
                return RedirectToBlogDetails(blogPostId, returnUrl);
            }

            var blog = _context.BlogPosts.FirstOrDefault(x => x.Id == blogPostId);
            if (blog == null)
                return NotFound();

            var isCommentOwner = _context.Comments.Any(x => x.Id == commentId && x.UserId == userId.Value && x.BlogPostId == blogPostId);
            if (blog.UserId != userId.Value && !isAdmin && !isCommentOwner)
                return Forbid();

            var targetComment = _context.Comments
                .FirstOrDefault(x => x.Id == commentId && x.BlogPostId == blogPostId);
            if (targetComment == null)
                return NotFound();

            var commentRows = _context.Comments
                .Where(x => x.BlogPostId == blogPostId)
                .Select(x => new { x.Id, x.ParentCommentId, x.UserId })
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
                ActorUserId = userId.Value,
                TargetUserId = targetComment.UserId,
                BlogPostId = blogPostId,
                CommentId = commentId,
                ActionType = isAdmin
                    ? "CommentDeleteByAdmin"
                    : (blog.UserId == userId.Value ? "CommentDeleteByPostOwner" : "CommentDeleteByCommentOwner"),
                Reason = string.IsNullOrWhiteSpace(reason) ? "Yönetici işlemi" : reason.Trim(),
                CreatedAt = DateTime.Now
            });

            _context.Comments.RemoveRange(commentsToDelete);
            _context.SaveChanges();

            TempData["SuccessMessage"] = "Yorum silindi";
            return RedirectToBlogDetails(blogPostId, returnUrl);
        }

        private IActionResult RedirectToBlogDetails(int blogPostId, string? returnUrl = null)
        {
            if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
                return LocalRedirect(returnUrl);

            return RedirectToAction("Details", "Blog", new { id = blogPostId }, "commentsSection");
        }

        private UserBan? GetActiveBan(int userId)
        {
            return _context.UserBans
                .Where(x => x.UserId == userId && x.ExpiresAt > DateTime.Now)
                .OrderByDescending(x => x.ExpiresAt)
                .FirstOrDefault();
        }

        private int GetCommentDepth(Comment comment)
        {
            var depth = 0;
            var currentParentId = comment.ParentCommentId;

            while (currentParentId.HasValue)
            {
                depth++;
                var parent = _context.Comments
                    .Where(x => x.Id == currentParentId.Value)
                    .Select(x => new { x.ParentCommentId })
                    .FirstOrDefault();

                if (parent == null)
                    break;

                currentParentId = parent.ParentCommentId;
            }

            return depth;
        }

        private static string BuildBanMessage(UserBan ban, string blockedAction)
        {
            return $"Bu hesap banlı olduğu için {blockedAction}. Ban nedeni: \"{ban.Reason}\". Bitiş: {ban.ExpiresAt:dd.MM.yyyy HH:mm}.";
        }
    }
}
