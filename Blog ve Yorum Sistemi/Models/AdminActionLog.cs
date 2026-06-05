namespace BireyselHesaplar.Models
{
    public class AdminActionLog
    {
        public int Id { get; set; }
        public int ActorUserId { get; set; }
        public int? TargetUserId { get; set; }
        public int? BlogPostId { get; set; }
        public int? CommentId { get; set; }
        public string ActionType { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
