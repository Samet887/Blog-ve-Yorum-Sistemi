using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BireyselHesaplar.Models
{
    public class BlogPost
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Başlık zorunludur.")]
        [StringLength(150)]
        public string Title { get; set; } = string.Empty;

        [Required(ErrorMessage = "İçerik zorunludur.")]
        public string Content { get; set; } = string.Empty;

        [Required]
        [StringLength(80)]
        public string CategorySlug { get; set; } = "yasam";

        public string? ImageUrl { get; set; }
        public int ImageWidthPercent { get; set; } = 100;
        public string ImagePlacement { get; set; } = "Top";

        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime? UpdatedAt { get; set; }

        [ForeignKey("User")]
        public int UserId { get; set; }

        public User? User { get; set; }

        public List<Comment> Comments { get; set; } = new();
        public List<BlogLike> Likes { get; set; } = new();
    }
}
