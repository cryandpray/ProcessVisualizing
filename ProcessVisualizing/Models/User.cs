using System;
using System.ComponentModel.DataAnnotations;

namespace ProcessVisualizing.Models
{
    public class User
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Логин обязателен")]
        [StringLength(50, MinimumLength = 3, ErrorMessage = "Логин должен быть от 3 до 50 символов")]
        public string Login { get; set; }

        public string PasswordHash { get; set; }

        public DateTime? RegistrationDate { get; set; }
    }
}