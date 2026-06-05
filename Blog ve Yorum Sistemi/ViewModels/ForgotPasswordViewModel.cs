using System.ComponentModel.DataAnnotations;

namespace BireyselHesaplar.ViewModels
{
    public class ForgotPasswordViewModel
    {
        [Required]
        [Display(Name = "Kullanici Adi")]
        public string UserName { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        [Display(Name = "E-posta")]
        public string Email { get; set; } = string.Empty;
    }
}
