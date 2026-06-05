using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace BireyselHesaplar.ViewModels
{
    public class BlogCreateViewModel
    {
        [Required(ErrorMessage = "Başlık zorunludur.")]
        [StringLength(150)]
        public string Title { get; set; } = string.Empty;

        [Required(ErrorMessage = "İçerik zorunludur.")]
        public string Content { get; set; } = string.Empty;

        public IFormFile? ImageFile { get; set; }

        [Range(25, 100, ErrorMessage = "Görsel boyutu 25 ile 100 arasında olmalı.")]
        public int ImageWidthPercent { get; set; } = 60;

        [Required]
        public string ImagePlacement { get; set; } = "Right";

        public string? MetaHidden { get; set; }
    }
}
