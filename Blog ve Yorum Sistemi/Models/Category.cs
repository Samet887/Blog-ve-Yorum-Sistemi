using System.ComponentModel.DataAnnotations;

namespace BireyselHesaplar.Models
{
    public class Category
    {
        public int Id { get; set; }

        [Required]
        [StringLength(80)]
        public string Name { get; set; } = string.Empty;

        [Required]
        [StringLength(80)]
        public string Slug { get; set; } = string.Empty;

        public int? ParentCategoryId { get; set; }
        public Category? ParentCategory { get; set; }
        public List<Category> SubCategories { get; set; } = new();

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
