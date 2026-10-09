using System.ComponentModel.DataAnnotations;

namespace WebAssignment.Models
{
    public class ResetPasswordViewModel
    {
        [Required(ErrorMessage = "New Password is required.")]
        [MinLength(6, ErrorMessage = "Password must be at least 6 characters.")]
        [RegularExpression(@"^(?=.*[a-zA-Z])(?=.*\d)(?=.*[@$!%*?&#])[A-Za-z\d@$!%*?&#]{6,}$",
            ErrorMessage = "Password must contain at least 1 alphabet, 1 number, and 1 symbol.")]
        public string NewPassword { get; set; }

        [Required(ErrorMessage = "Confirm Password is required.")]
        [Compare("NewPassword", ErrorMessage = "Passwords do not match.")]
        public string ConfirmPassword { get; set; }
    }
}