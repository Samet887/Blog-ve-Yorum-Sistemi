using Microsoft.AspNetCore.Mvc;
using BireyselHesaplar.Data;
using BireyselHesaplar.Models;
using BireyselHesaplar.ViewModels;
using Microsoft.AspNetCore.Identity;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace BireyselHesaplar.Controllers
{
    public class AccountController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _environment;
        private readonly PasswordHasher<User> _passwordHasher = new();
        private static readonly HashSet<string> AllowedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".webp", ".gif"
        };
        private const long MaxProfileImageSizeBytes = 5 * 1024 * 1024;
        private const int PasswordResetTokenMinutes = 30;

        public AccountController(AppDbContext context, IWebHostEnvironment environment)
        {
            _context = context;
            _environment = environment;
        }

        public IActionResult Register()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Register(User user)
        {
            if (!ModelState.IsValid)
                return View(user);

            user.FullName = (user.FullName ?? string.Empty).Trim();
            user.UserName = (user.UserName ?? string.Empty).Trim();
            user.Email = (user.Email ?? string.Empty).Trim();
            user.PasswordHash = (user.PasswordHash ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(user.FullName) ||
                string.IsNullOrWhiteSpace(user.UserName) ||
                string.IsNullOrWhiteSpace(user.Email) ||
                string.IsNullOrWhiteSpace(user.PasswordHash))
            {
                ModelState.AddModelError(string.Empty, "Tum alanlari doldurman gerekiyor.");
                return View(user);
            }

            if (_context.Users.Any(x => x.UserName == user.UserName))
            {
                ModelState.AddModelError(nameof(user.UserName), "Bu kullanici adi zaten alinmis.");
                return View(user);
            }

            if (_context.Users.Any(x => x.Email == user.Email))
            {
                ModelState.AddModelError(nameof(user.Email), "Bu e-posta zaten kayitli.");
                return View(user);
            }

            user.PasswordHash = HashPassword(user, user.PasswordHash);
            user.Role = "User";

            try
            {
                _context.Users.Add(user);
                _context.SaveChanges();
            }
            catch (DbUpdateException)
            {
                ModelState.AddModelError(string.Empty, "Kayit olusturulurken bir hata olustu. Lutfen tekrar dene.");
                return View(user);
            }

            return RedirectToAction("Login");
        }

        public IActionResult Login()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Login(string username, string password, bool rememberMe)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                ViewBag.Error = "Kullanıcı adı veya şifre hatalı";
                return View();
            }

            var normalizedUserName = username.Trim();
            var user = _context.Users
                .FirstOrDefault(x => x.UserName == normalizedUserName);

            if (user == null || !VerifyPassword(user, password, out var shouldRehash))
            {
                ViewBag.Error = "Kullanıcı adı veya şifre hatalı";
                return View();
            }

            if (shouldRehash)
            {
                user.PasswordHash = HashPassword(user, password);
                _context.SaveChanges();
            }

            var userRole = string.IsNullOrWhiteSpace(user.Role) ? "User" : user.Role;

            HttpContext.Session.SetInt32("UserId", user.Id);
            HttpContext.Session.SetString("UserName", user.UserName);
            HttpContext.Session.SetString("UserRole", userRole);

            if (rememberMe)
            {
                var cookieOptions = new CookieOptions
                {
                    Expires = DateTimeOffset.Now.AddDays(30),
                    HttpOnly = true,
                    IsEssential = true,
                    SameSite = SameSiteMode.Lax,
                    Secure = HttpContext.Request.IsHttps
                };

                Response.Cookies.Append("remember_user_id", user.Id.ToString(), cookieOptions);
                Response.Cookies.Append("remember_user_name", user.UserName, cookieOptions);
                Response.Cookies.Append("remember_user_role", userRole, cookieOptions);
            }
            else
            {
                Response.Cookies.Delete("remember_user_id");
                Response.Cookies.Delete("remember_user_name");
                Response.Cookies.Delete("remember_user_role");
            }

            var activeBan = GetActiveBan(user.Id);
            if (activeBan != null)
            {
                TempData["ErrorMessage"] = $"Yönetici tarafından \"{activeBan.Reason}\" nedeniyle {activeBan.ExpiresAt:dd.MM.yyyy HH:mm} tarihine kadar engellendiniz.";
            }

            if (string.Equals(userRole, "Admin", StringComparison.OrdinalIgnoreCase)
                || string.Equals(userRole, "Moderator", StringComparison.OrdinalIgnoreCase))
                return RedirectToAction("Index", "Admin");

            return RedirectToAction("Index", "Blog");
        }

        [HttpGet]
        public IActionResult ForgotPassword()
        {
            ViewData["HideChrome"] = true;
            return View(new ForgotPasswordViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ForgotPassword(ForgotPasswordViewModel model)
        {
            ViewData["HideChrome"] = true;

            if (!ModelState.IsValid)
                return View(model);

            var userName = (model.UserName ?? string.Empty).Trim();
            var email = (model.Email ?? string.Empty).Trim();
            var user = _context.Users.FirstOrDefault(x => x.UserName == userName && x.Email == email);

            if (user == null)
            {
                ViewBag.Error = "Bu bilgilerle eşleşen bir hesap bulunamadı.";
                return View(model);
            }

            var now = DateTime.Now;
            var activeTokens = _context.PasswordResetTokens
                .Where(x => x.UserId == user.Id && x.UsedAt == null && x.ExpiresAt > now)
                .ToList();

            foreach (var token in activeTokens)
            {
                token.UsedAt = now;
            }

            var rawToken = GeneratePasswordResetToken();
            var tokenHash = ComputePasswordResetTokenHash(rawToken);

            _context.PasswordResetTokens.Add(new PasswordResetToken
            {
                UserId = user.Id,
                TokenHash = tokenHash,
                ExpiresAt = now.AddMinutes(PasswordResetTokenMinutes),
                CreatedAt = now
            });
            _context.SaveChanges();

            var resetLink = Url.Action(
                "ResetPassword",
                "Account",
                new { uid = user.Id, token = rawToken },
                Request.Scheme);

            ViewBag.Success = "Şifre sıfırlama bağlantısı oluşturuldu.";
            ViewBag.ResetLink = resetLink ?? string.Empty;
            return View(new ForgotPasswordViewModel());
        }

        [HttpGet]
        public IActionResult ResetPassword(int uid, string? token)
        {
            ViewData["HideChrome"] = true;

            if (uid <= 0 || string.IsNullOrWhiteSpace(token))
            {
                ViewBag.Error = "Şifre sıfırlama bağlantısı geçersiz.";
                return View(new ResetPasswordViewModel());
            }

            var tokenEntity = FindValidResetToken(uid, token);
            if (tokenEntity == null)
            {
                ViewBag.Error = "Bağlantı geçersiz veya süresi dolmuş.";
                return View(new ResetPasswordViewModel());
            }

            return View(new ResetPasswordViewModel
            {
                UserId = uid,
                Token = token
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ResetPassword(ResetPasswordViewModel model)
        {
            ViewData["HideChrome"] = true;

            if (!ModelState.IsValid)
                return View(model);

            if (!string.Equals(model.NewPassword, model.ConfirmPassword, StringComparison.Ordinal))
            {
                ModelState.AddModelError(nameof(model.ConfirmPassword), "Yeni şifre ve tekrar şifresi aynı olmalı.");
                return View(model);
            }

            if (model.NewPassword.Length < 6)
            {
                ModelState.AddModelError(nameof(model.NewPassword), "Yeni şifre en az 6 karakter olmalı.");
                return View(model);
            }

            var tokenEntity = FindValidResetToken(model.UserId, model.Token);
            if (tokenEntity == null)
            {
                ViewBag.Error = "Bağlantı geçersiz veya süresi dolmuş.";
                return View(model);
            }

            var user = _context.Users.FirstOrDefault(x => x.Id == model.UserId);
            if (user == null)
            {
                ViewBag.Error = "Kullanıcı bulunamadı.";
                return View(model);
            }

            var now = DateTime.Now;
            user.PasswordHash = HashPassword(user, model.NewPassword);
            tokenEntity.UsedAt = now;

            var otherActiveTokens = _context.PasswordResetTokens
                .Where(x => x.UserId == user.Id && x.Id != tokenEntity.Id && x.UsedAt == null && x.ExpiresAt > now)
                .ToList();

            foreach (var token in otherActiveTokens)
            {
                token.UsedAt = now;
            }

            _context.SaveChanges();
            TempData["SuccessMessage"] = "Şifren başarıyla güncellendi. Yeni şifrenle giriş yapabilirsin.";
            return RedirectToAction("Login");
        }

        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            Response.Cookies.Delete("remember_user_id");
            Response.Cookies.Delete("remember_user_name");
            Response.Cookies.Delete("remember_user_role");
            return RedirectToAction("Index", "Blog");
        }

        public IActionResult Profile()
        {
            var userName = HttpContext.Session.GetString("UserName");
            if (string.IsNullOrWhiteSpace(userName))
                return RedirectToAction("Login");

            var currentUser = _context.Users.FirstOrDefault(x => x.UserName == userName);
            if (currentUser != null)
            {
                var dbRole = string.IsNullOrWhiteSpace(currentUser.Role) ? "User" : currentUser.Role;
                HttpContext.Session.SetString("UserRole", dbRole);
            }

            return RedirectToAction("UserProfile", new { username = userName });
        }

        [HttpGet]
        public IActionResult UserProfile(string username)
        {
            var currentUserId = HttpContext.Session.GetInt32("UserId");
            if (currentUserId == null)
                return RedirectToAction("Login");

            if (string.IsNullOrWhiteSpace(username))
                return NotFound();

            var user = _context.Users.FirstOrDefault(x => x.UserName == username);
            if (user == null)
                return NotFound();

            var posts = _context.BlogPosts
                .Include(x => x.Comments)
                .Include(x => x.Likes)
                .Where(x => x.UserId == user.Id)
                .OrderByDescending(x => x.CreatedAt)
                .ToList();

            var isOwnProfile = user.Id == currentUserId.Value;
            if (isOwnProfile)
            {
                var dbRole = string.IsNullOrWhiteSpace(user.Role) ? "User" : user.Role;
                HttpContext.Session.SetString("UserRole", dbRole);
            }

            var sessionRole = HttpContext.Session.GetString("UserRole");
            var isAdmin = string.Equals(sessionRole, "Admin", StringComparison.OrdinalIgnoreCase)
                || string.Equals(sessionRole, "Moderator", StringComparison.OrdinalIgnoreCase);
            var isTargetPrivileged = IsPrivilegedRole(user.Role);
            var isBlocked = !isOwnProfile && !isTargetPrivileged && _context.UserBlocks.Any(x =>
                x.BlockerUserId == currentUserId.Value && x.BlockedUserId == user.Id);
            var activeBan = _context.UserBans
                .Where(x => x.UserId == user.Id && x.ExpiresAt > DateTime.Now)
                .OrderByDescending(x => x.ExpiresAt)
                .FirstOrDefault();

            ViewBag.ProfilePosts = posts;
            ViewBag.IsOwnProfile = isOwnProfile;
            ViewBag.IsAdmin = isAdmin;
            ViewBag.IsBlocked = isBlocked;
            ViewBag.ActiveBan = activeBan;

            return View("Profile", user);
        }

        [HttpGet]
        public IActionResult SearchUsers(string? q)
        {
            var currentUserId = HttpContext.Session.GetInt32("UserId");
            if (currentUserId == null)
                return RedirectToAction("Login");

            var searchText = (q ?? string.Empty).Trim();
            var users = new List<User>();

            if (!string.IsNullOrWhiteSpace(searchText))
            {
                users = _context.Users
                    .Where(x =>
                        x.UserName.Contains(searchText) ||
                        (x.FullName != null && x.FullName.Contains(searchText)) ||
                        (x.Email != null && x.Email.Contains(searchText)))
                    .OrderBy(x => x.UserName)
                    .Take(50)
                    .ToList();
            }

            ViewBag.SearchText = searchText;
            return View(users);
        }

        [HttpGet]
        public IActionResult SearchUsersApi(string? q)
        {
            var currentUserId = HttpContext.Session.GetInt32("UserId");
            if (currentUserId == null)
                return Unauthorized();

            var searchText = (q ?? string.Empty).Trim();
            if (searchText.Length < 2)
                return Json(Array.Empty<object>());

            var users = _context.Users
                .Where(x =>
                    x.UserName.Contains(searchText) ||
                    (x.FullName != null && x.FullName.Contains(searchText)))
                .OrderBy(x => x.UserName)
                .Take(8)
                .Select(x => new
                {
                    x.UserName,
                    x.FullName,
                    x.ProfileImageUrl
                })
                .ToList();

            return Json(users);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ToggleBlock(int blockedUserId)
        {
            var currentUserId = HttpContext.Session.GetInt32("UserId");
            if (currentUserId == null)
                return RedirectToAction("Login");

            if (blockedUserId == currentUserId.Value)
                return RedirectToAction("Profile");

            var blockedUser = _context.Users.FirstOrDefault(x => x.Id == blockedUserId);
            if (blockedUser == null)
                return NotFound();

            var currentUserRole = HttpContext.Session.GetString("UserRole");
            var currentUserIsAdmin = string.Equals(currentUserRole, "Admin", StringComparison.OrdinalIgnoreCase);
            var blockedUserIsPrivileged = IsPrivilegedRole(blockedUser.Role);

            if (!currentUserIsAdmin && blockedUserIsPrivileged)
            {
                var existingPrivilegedBlock = _context.UserBlocks
                    .FirstOrDefault(x => x.BlockerUserId == currentUserId.Value && x.BlockedUserId == blockedUserId);

                if (existingPrivilegedBlock != null)
                {
                    _context.UserBlocks.Remove(existingPrivilegedBlock);
                    _context.SaveChanges();
                }

                TempData["ErrorMessage"] = "Yetkili kullanicilar engellenemez.";
                return RedirectToAction("UserProfile", new { username = blockedUser.UserName });
            }

            var existing = _context.UserBlocks
                .FirstOrDefault(x => x.BlockerUserId == currentUserId.Value && x.BlockedUserId == blockedUserId);

            if (existing == null)
            {
                _context.UserBlocks.Add(new UserBlock
                {
                    BlockerUserId = currentUserId.Value,
                    BlockedUserId = blockedUserId,
                    CreatedAt = DateTime.Now
                });
                TempData["SuccessMessage"] = "Kullanıcı engellendi.";
            }
            else
            {
                _context.UserBlocks.Remove(existing);
                TempData["SuccessMessage"] = "Kullanıcı engeli kaldırıldı.";
            }

            _context.SaveChanges();
            return RedirectToAction("UserProfile", new { username = blockedUser.UserName });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ChangePassword(string currentPassword, string newPassword, string confirmNewPassword)
        {
            var currentUserId = HttpContext.Session.GetInt32("UserId");
            if (currentUserId == null)
                return RedirectToAction("Login");

            if (string.IsNullOrWhiteSpace(currentPassword) ||
                string.IsNullOrWhiteSpace(newPassword) ||
                string.IsNullOrWhiteSpace(confirmNewPassword))
            {
                TempData["ErrorMessage"] = "Tüm şifre alanlarını doldurmalısın.";
                return RedirectToAction("Profile");
            }

            if (!string.Equals(newPassword, confirmNewPassword, StringComparison.Ordinal))
            {
                TempData["ErrorMessage"] = "Yeni şifre ve tekrar şifresi aynı olmalı.";
                return RedirectToAction("Profile");
            }

            if (newPassword.Length < 6)
            {
                TempData["ErrorMessage"] = "Yeni şifre en az 6 karakter olmalı.";
                return RedirectToAction("Profile");
            }

            var user = _context.Users.FirstOrDefault(x => x.Id == currentUserId.Value);
            if (user == null)
                return RedirectToAction("Login");

            if (!VerifyPassword(user, currentPassword, out _))
            {
                TempData["ErrorMessage"] = "Mevcut şifren yanlış.";
                return RedirectToAction("Profile");
            }

            user.PasswordHash = HashPassword(user, newPassword);
            _context.SaveChanges();

            TempData["SuccessMessage"] = "Şifren başarıyla değiştirildi.";
            return RedirectToAction("Profile");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ChangeProfileImage(IFormFile? profileImage)
        {
            var currentUserId = HttpContext.Session.GetInt32("UserId");
            if (currentUserId == null)
                return RedirectToAction("Login");

            if (profileImage == null)
            {
                TempData["ErrorMessage"] = "Lütfen bir görsel seç.";
                return RedirectToAction("Profile");
            }

            var user = _context.Users.FirstOrDefault(x => x.Id == currentUserId.Value);
            if (user == null)
                return RedirectToAction("Login");

            if (!TrySaveProfileImage(profileImage, out var relativePath, out var errorMessage))
            {
                TempData["ErrorMessage"] = errorMessage;
                return RedirectToAction("Profile");
            }

            DeleteProfileImageIfLocal(user.ProfileImageUrl);
            user.ProfileImageUrl = relativePath;
            _context.SaveChanges();

            TempData["SuccessMessage"] = "Profil fotoğrafı güncellendi.";
            return RedirectToAction("Profile");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult RemoveProfileImage()
        {
            var currentUserId = HttpContext.Session.GetInt32("UserId");
            if (currentUserId == null)
                return RedirectToAction("Login");

            var user = _context.Users.FirstOrDefault(x => x.Id == currentUserId.Value);
            if (user == null)
                return RedirectToAction("Login");

            if (string.IsNullOrWhiteSpace(user.ProfileImageUrl))
            {
                TempData["ErrorMessage"] = "Kaldırılacak profil fotoğrafı bulunamadı.";
                return RedirectToAction("Profile");
            }

            DeleteProfileImageIfLocal(user.ProfileImageUrl);
            user.ProfileImageUrl = null;
            _context.SaveChanges();

            TempData["SuccessMessage"] = "Profil fotoğrafı kaldırıldı.";
            return RedirectToAction("Profile");
        }

        private string HashPassword(User user, string password)
        {
            return _passwordHasher.HashPassword(user, password);
        }

        private bool VerifyPassword(User user, string password, out bool shouldRehash)
        {
            shouldRehash = false;

            if (string.IsNullOrWhiteSpace(user.PasswordHash))
                return false;

            var verifyResult = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
            if (verifyResult == PasswordVerificationResult.Success)
                return true;

            if (verifyResult == PasswordVerificationResult.SuccessRehashNeeded)
            {
                shouldRehash = true;
                return true;
            }

            var legacyHash = HashPasswordLegacy(password);
            if (string.Equals(user.PasswordHash, legacyHash, StringComparison.Ordinal))
            {
                shouldRehash = true;
                return true;
            }

            return false;
        }

        private PasswordResetToken? FindValidResetToken(int userId, string? rawToken)
        {
            if (userId <= 0 || string.IsNullOrWhiteSpace(rawToken))
                return null;

            var tokenHash = ComputePasswordResetTokenHash(rawToken);
            var now = DateTime.Now;

            return _context.PasswordResetTokens
                .FirstOrDefault(x =>
                    x.UserId == userId &&
                    x.TokenHash == tokenHash &&
                    x.UsedAt == null &&
                    x.ExpiresAt > now);
        }

        private static string GeneratePasswordResetToken()
        {
            var bytes = RandomNumberGenerator.GetBytes(48);
            return Convert.ToBase64String(bytes)
                .Replace("+", "-")
                .Replace("/", "_")
                .TrimEnd('=');
        }

        private static string ComputePasswordResetTokenHash(string rawToken)
        {
            using var sha = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(rawToken);
            var hashBytes = sha.ComputeHash(bytes);
            return Convert.ToHexString(hashBytes);
        }

        private static string HashPasswordLegacy(string password)
        {
            using var sha = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(password);
            var hash = sha.ComputeHash(bytes);
            return Convert.ToBase64String(hash);
        }

        private bool TrySaveProfileImage(IFormFile file, out string relativePath, out string errorMessage)
        {
            relativePath = string.Empty;
            errorMessage = string.Empty;

            if (file.Length <= 0)
            {
                errorMessage = "Seçilen dosya boş olamaz.";
                return false;
            }

            if (file.Length > MaxProfileImageSizeBytes)
            {
                errorMessage = "Profil fotoğrafı en fazla 5 MB olabilir.";
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

            var profileImagesDirectory = Path.Combine(webRootPath, "uploads", "profiles");
            Directory.CreateDirectory(profileImagesDirectory);

            var fileName = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
            var fullPath = Path.Combine(profileImagesDirectory, fileName);

            using var stream = new FileStream(fullPath, FileMode.Create);
            file.CopyTo(stream);

            relativePath = $"/uploads/profiles/{fileName}";
            return true;
        }

        private void DeleteProfileImageIfLocal(string? imageUrl)
        {
            if (string.IsNullOrWhiteSpace(imageUrl))
                return;

            if (!imageUrl.StartsWith("/uploads/profiles/", StringComparison.OrdinalIgnoreCase))
                return;

            var webRootPath = _environment.WebRootPath;
            if (string.IsNullOrWhiteSpace(webRootPath))
                webRootPath = Path.Combine(_environment.ContentRootPath, "wwwroot");

            var profileRoot = Path.GetFullPath(Path.Combine(webRootPath, "uploads", "profiles"));
            var relativePath = imageUrl.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            var fullPath = Path.GetFullPath(Path.Combine(webRootPath, relativePath));

            if (!fullPath.StartsWith(profileRoot, StringComparison.OrdinalIgnoreCase))
                return;

            if (System.IO.File.Exists(fullPath))
                System.IO.File.Delete(fullPath);
        }

        private static bool IsPrivilegedRole(string? role)
        {
            return string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(role, "Moderator", StringComparison.OrdinalIgnoreCase);
        }

        private UserBan? GetActiveBan(int userId)
        {
            return _context.UserBans
                .Where(x => x.UserId == userId && x.ExpiresAt > DateTime.Now)
                .OrderByDescending(x => x.ExpiresAt)
                .FirstOrDefault();
        }
    }
}
