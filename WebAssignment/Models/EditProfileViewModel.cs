using System.ComponentModel.DataAnnotations;

namespace WebAssignment.Models
{
    public class EditProfileViewModel
    {
        public int UserId { get; set; }

        // Added back so the View can use it for labels/avatars
        public string? Username { get; set; }

        [Required(ErrorMessage = "Full Name is required")]
        public string FullName { get; set; }

        [Required(ErrorMessage = "Email is required")]
        [EmailAddress(ErrorMessage = "Invalid email address")]
        public string Email { get; set; }

        [RegularExpression(@"^\+[1-9]\d{7,14}$", ErrorMessage = "Use international format, for example +60123456789.")]
        public string? PhoneNumber { get; set; }

        // Added back so the View can display your current picture
        public string? ProfilePhotoPath { get; set; }
    }
}
