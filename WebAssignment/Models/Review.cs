using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WebAssignment.Models
{
    public class Review
    {
        [Key]
        public int ReviewId { get; set; }

        [Range(1, 5)]
        public int Rating { get; set; }

        [Required]
        [StringLength(500)]
        public string Comment { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // --- NEW FIELDS FOR ADMIN REPLY ---
        public string? Reply { get; set; } // Nullable, as not all reviews have replies
        public DateTime? ReplyDate { get; set; }
        // ----------------------------------

        // Relationships
        public int UserId { get; set; }
        [ForeignKey("UserId")]
        public User? User { get; set; }

        public int MovieId { get; set; }
        [ForeignKey("MovieId")]
        public Movie? Movie { get; set; }
    }
}