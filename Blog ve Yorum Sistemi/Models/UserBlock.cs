using System.ComponentModel.DataAnnotations.Schema;

namespace BireyselHesaplar.Models
{
    public class UserBlock
    {
        public int Id { get; set; }

        [ForeignKey("BlockerUser")]
        public int BlockerUserId { get; set; }
        public User BlockerUser { get; set; } = null!;

        [ForeignKey("BlockedUser")]
        public int BlockedUserId { get; set; }
        public User BlockedUser { get; set; } = null!;

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
