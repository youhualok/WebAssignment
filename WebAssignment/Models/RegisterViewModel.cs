using System.ComponentModel.DataAnnotations;

namespace WebAssignment.Models
{
    public class RegisterViewModel
    {
        [Required]
        public string FullName { get; set; }

        [Required]
        public string Username { get; set; }

        [Required]
        [EmailAddress]
        // This Regex enforces a real-looking email structure
        [RegularExpression(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", ErrorMessage = "Please enter a valid email address.")]
        public string Email { get; set; }

        [RegularExpression(@"^\+[1-9]\d{7,14}$", ErrorMessage = "Use international format, for example +60123456789.")]
        public string? PhoneNumber { get; set; }

        [Required]
        [MinLength(6)]
        [RegularExpression(@"^(?=.*[a-zA-Z])(?=.*\d)(?=.*[@$!%*?&#])[A-Za-z\d@$!%*?&#]{6,}$",
            ErrorMessage = "Password must have 1 letter, 1 number, and 1 symbol.")]
        public string Password { get; set; }

        [Compare("Password", ErrorMessage = "Passwords do not match.")]
        public string ConfirmPassword { get; set; }
    }
}
