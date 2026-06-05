using System.ComponentModel.DataAnnotations.Schema;

namespace BireyselHesaplar.Models
{
    public class BlogLike
    {
        public int Id { get; set; }

        [ForeignKey("BlogPost")]
        public int BlogPostId { get; set; }
        public BlogPost BlogPost { get; set; } = null!;

        [ForeignKey("User")]
        public int UserId { get; set; }
        public User User { get; set; } = null!;

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
