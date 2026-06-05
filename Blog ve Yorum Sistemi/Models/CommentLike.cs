using System.ComponentModel.DataAnnotations.Schema;

namespace BireyselHesaplar.Models
{
    public class CommentLike
    {
        public int Id { get; set; }

        [ForeignKey("Comment")]
        public int CommentId { get; set; }
        public Comment Comment { get; set; } = null!;

        [ForeignKey("User")]
        public int UserId { get; set; }
        public User User { get; set; } = null!;

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
