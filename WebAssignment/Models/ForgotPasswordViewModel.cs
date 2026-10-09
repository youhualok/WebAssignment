using System.ComponentModel.DataAnnotations;

namespace WebAssignment.Models
{
    public class ForgotPasswordViewModel
    {
        [Required(ErrorMessage = "Email is required.")]
        [EmailAddress(ErrorMessage = "Invalid Email Format.")]
        // Same strict Regex as Register page
        [RegularExpression(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", ErrorMessage = "Please enter a valid email address.")]
        public string Email { get; set; }
    }
}