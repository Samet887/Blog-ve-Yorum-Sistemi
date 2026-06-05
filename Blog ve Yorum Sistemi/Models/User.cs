using System.ComponentModel.DataAnnotations;

namespace BireyselHesaplar.Models
{
    public class User
    {
        public int Id { get; set; }

        [Required]
        [StringLength(100)]
        public string FullName { get; set; } = string.Empty;

        [Required]
        [StringLength(50)]
        public string UserName { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        public string PasswordHash { get; set; } = string.Empty;
        public string Role { get; set; } = "User";
        public string? ProfileImageUrl { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public List<BlogPost> BlogPosts { get; set; } = new List<BlogPost>();
        public List<Comment> Comments { get; set; } = new List<Comment>();
        public List<BlogLike> Likes { get; set; } = new List<BlogLike>();
        public List<CommentLike> CommentLikes { get; set; } = new List<CommentLike>();
        public List<UserBlock> BlockedUsers { get; set; } = new List<UserBlock>();
        public List<UserBlock> BlockedByUsers { get; set; } = new List<UserBlock>();
        public List<UserBan> Bans { get; set; } = new List<UserBan>();
        public List<UserBan> BansIssued { get; set; } = new List<UserBan>();
    }
}
