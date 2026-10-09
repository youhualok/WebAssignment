using Microsoft.EntityFrameworkCore;
using WebAssignment.Models;

namespace WebAssignment.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        public DbSet<User> Users { get; set; }
        public DbSet<Movie> Movies { get; set; }
        public DbSet<Cinema> Cinemas { get; set; }
        public DbSet<Hall> Halls { get; set; }
        public DbSet<Seat> Seats { get; set; }
        public DbSet<Showtime> Showtimes { get; set; }

        public DbSet<Review> Reviews { get; set; }

        public DbSet<Booking> Bookings { get; set; }
        public DbSet<BookingDetail> BookingDetails { get; set; }
        public DbSet<BookingSnack> BookingSnacks { get; set; }

        public DbSet<Snack> Snacks { get; set; }
        public DbSet<PromoCode> PromoCodes { get; set; }

        // ==========================================================
        // THIS WAS MISSING -> ADD IT NOW
        // ==========================================================
        public DbSet<UserReward> UserRewards { get; set; }
        // ==========================================================
        public DbSet<Watchlist> Watchlists { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<User>().HasIndex(x => x.Username).IsUnique();
            modelBuilder.Entity<User>().HasIndex(x => x.Email).IsUnique();
            modelBuilder.Entity<Seat>().HasIndex(x => new { x.HallId, x.Row, x.Number }).IsUnique();
            modelBuilder.Entity<Watchlist>().HasIndex(x => new { x.UserId, x.MovieId }).IsUnique();
            modelBuilder.Entity<PromoCode>().HasIndex(x => x.Code).IsUnique();
            modelBuilder.Entity<UserReward>().HasIndex(x => x.PromoCode).IsUnique();
            modelBuilder.Entity<BookingDetail>().HasIndex(x => new{x.ShowtimeId, x.SeatId}).IsUnique().HasFilter("[ShowtimeId] IS NOT NULL"); 
        }
    }
}
