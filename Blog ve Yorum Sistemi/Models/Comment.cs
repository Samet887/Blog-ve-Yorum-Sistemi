using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BireyselHesaplar.Models
{
    public class Comment
    {
        public int Id { get; set; }

        [Required]
        public string Content { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [ForeignKey("BlogPost")]
        public int BlogPostId { get; set; }
        public BlogPost BlogPost { get; set; } = null!;

        [ForeignKey("User")]
        public int UserId { get; set; }
        public User User { get; set; } = null!;

        [ForeignKey("ParentComment")]
        public int? ParentCommentId { get; set; }
        public Comment? ParentComment { get; set; }

        public List<Comment> Replies { get; set; } = new List<Comment>();
        public List<CommentLike> Likes { get; set; } = new List<CommentLike>();
    }
}
