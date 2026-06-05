using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BireyselHesaplar.Models
{
    public class UserBan
    {
        public int Id { get; set; }

        [ForeignKey("User")]
        public int UserId { get; set; }
        public User User { get; set; } = null!;

        [ForeignKey("AdminUser")]
        public int AdminUserId { get; set; }
        public User AdminUser { get; set; } = null!;

        [Required]
        [StringLength(300)]
        public string Reason { get; set; } = string.Empty;

        public DateTime ExpiresAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
