using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WebAssignment.Models
{
    // ==========================================
    // 1. USER & ROLES
    // ==========================================
    public class User
    {
        [Key]
        public int UserId { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public string Email { get; set; }
        [MaxLength(20)]
        public string? PhoneNumber { get; set; }
        public string FullName { get; set; }

        public UserRole Role { get; set; }
        public string? ProfilePhotoPath { get; set; }

        public int FailedLoginAttempts { get; set; } = 0;
        public DateTime? LockoutEnd { get; set; }

        public int Points { get; set; } = 0;
        public ICollection<UserReward>? UserRewards { get; set; }
        public ICollection<Watchlist>? WatchlistItems { get; set; }

        public string ThemeColor { get; set; } = "Gold";

        // === BRANCH ADMIN FIELD ===
        public int? ManagedCinemaId { get; set; } // Null = Super Admin
        [ForeignKey("ManagedCinemaId")]
        public Cinema? ManagedCinema { get; set; }
        // ==========================
    }

    public class UserReward
    {
        [Key]
        public int UserRewardId { get; set; }
        public string RewardName { get; set; }
        public string PromoCode { get; set; }
        public int PointsCost { get; set; }
        public DateTime RedeemedDate { get; set; } = DateTime.Now;

        public bool IsUsed { get; set; } = false; // Default is Unused (False)

        public int UserId { get; set; }
        [ForeignKey("UserId")]
        public User? User { get; set; }
    }

    public enum UserRole { User, Admin }

    // ==========================================
    // 2. MOVIES
    // ==========================================
    public class Movie
    {
        [Key]
        public int MovieId { get; set; }
        public string Title { get; set; }
        public string Genre { get; set; }
        public int DurationMinutes { get; set; }
        public DateTime ReleaseDate { get; set; }
        public string Description { get; set; }
        public string ImageUrl { get; set; }
        public string TrailerUrl { get; set; }
        public MovieStatus Status { get; set; }

        [Column(TypeName = "decimal(18, 2)")]
        public decimal Price { get; set; }

        public ICollection<Showtime>? Showtimes { get; set; }
    }

    public enum MovieStatus { NowShowing, ComingSoon }

    // ==========================================
    // 3. CINEMA & HALL
    // ==========================================
    public class Cinema
    {
        [Key]
        public int CinemaId { get; set; }
        public string Name { get; set; }
        public string Location { get; set; }
        public string Address { get; set; }
        public ICollection<Hall>? Halls { get; set; }
    }

    public class Hall
    {
        [Key]
        public int HallId { get; set; }
        public string Name { get; set; }
        public int TotalSeats { get; set; }
        public int MaxSeats { get; set; }

        [ForeignKey("Cinema")]
        public int CinemaId { get; set; }
        public Cinema? Cinema { get; set; }
        public ICollection<Seat>? Seats { get; set; }
    }

    // ==========================================
    // 4. SEATS & SHOWTIME
    // ==========================================
    public class Seat
    {
        [Key]
        public int SeatId { get; set; }
        public string Row { get; set; }
        public int Number { get; set; }
        public bool IsAvailable { get; set; } = true;
        public SeatType Type { get; set; }

        [ForeignKey("Hall")]
        public int HallId { get; set; }
        public Hall? Hall { get; set; }
    }

    public enum SeatType { Standard, Couple, VIP }

    public class Showtime
    {
        [Key]
        public int ShowtimeId { get; set; }
        public DateTime StartTime { get; set; }




        // --- THE FIX ---
        [Column(TypeName = "decimal(18, 2)")]
        public decimal TicketPrice { get; set; }

        public int MaxSeats { get; set; }
        public int AvailableSeats { get; set; }
        public DateTime ShowtimeDate { get; set; }
        [ForeignKey("Movie")]
        public int MovieId { get; set; }
        public Movie? Movie { get; set; }

        [ForeignKey("Hall")]
        public int HallId { get; set; }
        public Hall? Hall { get; set; }
    }

    // ==========================================
    // 5. BOOKING SYSTEM
    // ==========================================
    public class Booking
    {
        public Booking()
        {
            BookingDetails = new List<BookingDetail>();
            BookingSnacks = new List<BookingSnack>();
        }

        [Key]
        public int BookingId { get; set; }
        public DateTime BookingDate { get; set; }
        public BookingStatus Status { get; set; }

        [Column(TypeName = "decimal(18, 2)")]
        public decimal TotalAmount { get; set; }

        [Column(TypeName = "decimal(18, 2)")]
        public decimal DiscountAmount { get; set; } = 0;
        public string? AppliedPromoCode { get; set; }

        [ForeignKey("User")]
        public int UserId { get; set; }
        public User? User { get; set; }

        public int? ShowtimeId { get; set; }
        [ForeignKey("ShowtimeId")]
        public Showtime? Showtime { get; set; }

        public ICollection<BookingDetail>? BookingDetails { get; set; }
        public ICollection<BookingSnack>? BookingSnacks { get; set; }
    }

    public class BookingDetail
    {
        [Key]
        public int BookingDetailId { get; set; }

        [ForeignKey("Booking")]
        public int BookingId { get; set; }
        public Booking? Booking { get; set; }

        [ForeignKey("Seat")]
        public int SeatId { get; set; }
        public Seat? Seat { get; set; }

        public int? ShowtimeId { get; set; }

        [ForeignKey("ShowtimeId")]
        public Showtime? Showtime { get; set; }
    }

    public enum BookingStatus { Pending, Paid, Cancelled }

    // ==========================================
    // 6. SNACKS & PROMO
    // ==========================================
    public class Snack
    {
        [Key]
        public int SnackId { get; set; }
        public string Name { get; set; }

        [Column(TypeName = "decimal(18, 2)")]
        public decimal Price { get; set; }
        public string ImageUrl { get; set; }
    }

    // Inside CinemaModels.cs

    public class BookingSnack
    {
        [Key]
        public int BookingSnackId { get; set; }

        [ForeignKey("Booking")]
        public int BookingId { get; set; }
        public Booking? Booking { get; set; }

        [ForeignKey("Snack")]
        public int SnackId { get; set; }
        public Snack? Snack { get; set; }

        public int Quantity { get; set; }

        // [NEW] Stores details like "Size: Large, Add-on: Butter"
        public string? Customization { get; set; }
    }
    // ==========================================
    // 7. Watchlist
    // ==========================================
    public class PromoCode
    {
        [Key]
        public int PromoCodeId { get; set; }
        public string Code { get; set; }

        [Column(TypeName = "decimal(18, 2)")]
        public decimal DiscountAmount { get; set; }

        public bool IsActive { get; set; } = true;

        public string? Description { get; set; }
    }

    public class Watchlist
    {
        [Key]
        public int WatchlistId { get; set; }

        public DateTime DateAdded { get; set; } = DateTime.Now;

        public int UserId { get; set; }
        [ForeignKey("UserId")]
        public User? User { get; set; }

        public int MovieId { get; set; }
        [ForeignKey("MovieId")]
        public Movie? Movie { get; set; }
    }
}
