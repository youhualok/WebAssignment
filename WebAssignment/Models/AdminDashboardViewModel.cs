using System.Collections.Generic;

namespace WebAssignment.Models
{
    public class AdminDashboardViewModel
    {
        // ==========================================
        // 1. KEY PERFORMANCE INDICATORS (STATS)
        // ==========================================
        public decimal MonthlyRevenue { get; set; }
        public decimal MonthlyTicketSales { get; set; }
        public decimal MonthlySnackSales { get; set; }
        public int MonthlyTransactions { get; set; }

        public int TotalUsers { get; set; }
        public decimal TotalRevenue { get; set; }

        public string TopBranchName { get; set; }
        public decimal TopBranchRevenue { get; set; }

        public string TopMovieName { get; set; }
        public decimal TopMovieRevenue { get; set; }

        // ==========================================
        // 2. MAIN CHART DATA (Line Chart)
        // ==========================================
        public string[] ChartLabels { get; set; }
        public int[] ChartValues { get; set; }

        // ==========================================
        // 3. DATA LISTS
        // ==========================================
        public List<Movie> NowShowing { get; set; } = new List<Movie>();
        public List<Movie> ComingSoon { get; set; } = new List<Movie>();
        public List<Booking> RecentBookings { get; set; } = new List<Booking>();

        // ==========================================
        // 4. HALL OCCUPANCY DATA (New Feature)
        // ==========================================
        public List<HallStatusViewModel> HallStatus { get; set; } = new List<HallStatusViewModel>();
    }

    // Helper Class for the Hall Status Card
    public class HallStatusViewModel
    {
        public int ShowtimeId { get; set; } // [NEW] To link to details
        public string HallName { get; set; }
        public string MovieTitle { get; set; }
        public string MovieImageUrl { get; set; }
        public string Time { get; set; }
        public int Capacity { get; set; }
        public int Sold { get; set; }
        public double Percentage { get; set; }
        public decimal CurrentRevenue { get; set; } // [NEW] Revenue tracking
        public string TrendingStatus { get; set; }
    }
}