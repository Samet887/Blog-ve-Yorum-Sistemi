using System.ComponentModel.DataAnnotations;

namespace BireyselHesaplar.ViewModels
{
    public class ResetPasswordViewModel
    {
        [Required]
        public int UserId { get; set; }

        [Required]
        public string Token { get; set; } = string.Empty;

        [Required]
        [MinLength(6)]
        [Display(Name = "Yeni Sifre")]
        public string NewPassword { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Yeni Sifre (Tekrar)")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
