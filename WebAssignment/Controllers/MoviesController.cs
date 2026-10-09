using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using WebAssignment.Data;
using WebAssignment.Models;
using WebAssignment.Helpers;

namespace WebAssignment.Controllers
{
    [Authorize(Roles = "Admin, SuperAdmin")]
    public class MoviesController : Controller
    {
        private readonly AppDbContext _context;
        private readonly EmailService _emailService;

        public MoviesController(AppDbContext context, EmailService emailService)
        {
            _context = context;
            _emailService = emailService;
        }

        // =====================================================================
        // 1. EXECUTIVE DASHBOARD
        // =====================================================================
        public async Task<IActionResult> Dashboard(int? branchId, int? movieId)
        {
            // A. SETUP USER & ROLE
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out int userId)) return RedirectToAction("Login", "Account");

            var currentUser = await _context.Users.FindAsync(userId);
            bool isSuperAdmin = IsSuperAdmin();

            int? activeBranchId = branchId;
            if (!isSuperAdmin)
            {
                activeBranchId = currentUser.ManagedCinemaId;
            }

            // B. PREPARE DROPDOWNS
            if (isSuperAdmin)
            {
                var branches = await _context.Cinemas.OrderBy(c => c.Name).ToListAsync();
                branches.Insert(0, new Cinema { CinemaId = 0, Name = "All Branches" });
                ViewBag.BranchList = new SelectList(branches, "CinemaId", "Name", activeBranchId ?? 0);
                ViewBag.IsSuperAdmin = true;
            }
            else
            {
                ViewBag.IsSuperAdmin = false;
                if (activeBranchId.HasValue)
                {
                    var myBranch = await _context.Cinemas.FindAsync(activeBranchId.Value);
                    ViewBag.CurrentBranchName = myBranch?.Name ?? "My Branch";
                }
            }

            var movies = await _context.Movies.OrderBy(m => m.Title).ToListAsync();
            movies.Insert(0, new Movie { MovieId = 0, Title = "All Movies" });
            ViewBag.MovieList = new SelectList(movies, "MovieId", "Title", movieId ?? 0);


            // C. BUILD QUERIES
            var now = DateTime.Now;
            var startOfMonth = new DateTime(now.Year, now.Month, 1);

            var statsQuery = _context.Bookings
                .Include(b => b.Showtime).ThenInclude(s => s.Movie)
                .Include(b => b.Showtime).ThenInclude(s => s.Hall).ThenInclude(h => h.Cinema)
                .Include(b => b.BookingSnacks).ThenInclude(bs => bs.Snack)
                .AsQueryable();

            if (activeBranchId.HasValue && activeBranchId.Value > 0)
            {
                statsQuery = statsQuery.Where(b => b.Showtime.Hall.CinemaId == activeBranchId.Value);
            }

            var chartQuery = statsQuery;
            if (movieId.HasValue && movieId.Value > 0)
            {
                chartQuery = chartQuery.Where(b => b.Showtime.MovieId == movieId.Value);
            }

            // D. CALCULATE METRICS
            var monthlyQuery = statsQuery.Where(b => b.BookingDate >= startOfMonth);

            var monthlyRevenue = await monthlyQuery.SumAsync(b => (decimal?)b.TotalAmount) ?? 0;
            var monthlySnackSales = await monthlyQuery.SelectMany(b => b.BookingSnacks).SumAsync(bs => bs.Quantity * bs.Snack.Price);
            var monthlyTicketSales = monthlyRevenue - monthlySnackSales;
            var monthlyTransactions = await monthlyQuery.CountAsync();
            var totalUsers = await _context.Users.CountAsync();

            // E. CHART DATA
            var last7Days = Enumerable.Range(0, 7).Select(i => DateTime.Now.AddDays(-6 + i).Date).ToList();
            var chartLabels = new List<string>();
            var chartValues = new List<int>();

            foreach (var day in last7Days)
            {
                chartLabels.Add(day.ToString("dd MMM"));
                var count = await chartQuery.CountAsync(b => b.BookingDate.Date == day);
                chartValues.Add(count);
            }

            // F. LISTS
            var nowShowing = await _context.Movies.Where(m => m.Status == MovieStatus.NowShowing).OrderByDescending(m => m.ReleaseDate).ToListAsync();
            var comingSoon = await _context.Movies.Where(m => m.Status == MovieStatus.ComingSoon).OrderBy(m => m.ReleaseDate).ToListAsync();
            var recentBookings = await chartQuery.Include(b => b.User).OrderByDescending(b => b.BookingDate).Take(5).ToListAsync();

            var model = new AdminDashboardViewModel
            {
                MonthlyRevenue = monthlyRevenue,
                MonthlyTicketSales = monthlyTicketSales,
                MonthlySnackSales = monthlySnackSales,
                MonthlyTransactions = monthlyTransactions,
                ChartLabels = chartLabels.ToArray(),
                ChartValues = chartValues.ToArray(),
                NowShowing = nowShowing,
                ComingSoon = comingSoon,
                RecentBookings = recentBookings,
                TotalUsers = totalUsers,

                // Hall Status logic moved to separate action
                HallStatus = new List<HallStatusViewModel>()
            };

            return View(model);
        }

        // =====================================================================
        // 2. HALL OCCUPANCY PAGE (ADVANCED)
        // =====================================================================
        public async Task<IActionResult> HallOccupancy(int? branchId, int? movieId)
        {
            // 1. Setup User
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out int userId)) return RedirectToAction("Login", "Account");
            var currentUser = await _context.Users.FindAsync(userId);
            bool isSuperAdmin = IsSuperAdmin();

            // 2. Determine Branch
            int? activeBranchId = branchId;
            if (!isSuperAdmin) activeBranchId = currentUser.ManagedCinemaId;

            // 3. Prepare Dropdowns
            if (isSuperAdmin)
            {
                var branches = await _context.Cinemas.OrderBy(c => c.Name).ToListAsync();
                branches.Insert(0, new Cinema { CinemaId = 0, Name = "All Branches" });
                ViewBag.BranchList = new SelectList(branches, "CinemaId", "Name", activeBranchId ?? 0);
                ViewBag.IsSuperAdmin = true;
            }
            else
            {
                ViewBag.IsSuperAdmin = false;
                if (activeBranchId.HasValue)
                {
                    var myBranch = await _context.Cinemas.FindAsync(activeBranchId.Value);
                    ViewBag.CurrentBranchName = myBranch?.Name ?? "My Branch";
                }
            }

            var movies = await _context.Movies
                .Where(m => m.Status == MovieStatus.NowShowing)
                .OrderBy(m => m.Title)
                .ToListAsync();
            movies.Insert(0, new Movie { MovieId = 0, Title = "All Movies" });
            ViewBag.MovieList = new SelectList(movies, "MovieId", "Title", movieId ?? 0);

            // 4. Query Data
            var today = DateTime.Today;
            var hallQuery = _context.Showtimes
                .Include(s => s.Hall).ThenInclude(h => h.Cinema)
                .Include(s => s.Movie)
                .Where(s => s.StartTime.Date == today)
                .AsQueryable();

            if (activeBranchId.HasValue && activeBranchId.Value > 0)
            {
                hallQuery = hallQuery.Where(s => s.Hall.CinemaId == activeBranchId.Value);
            }

            if (movieId.HasValue && movieId.Value > 0)
            {
                hallQuery = hallQuery.Where(s => s.MovieId == movieId.Value);
            }

            var todayShowtimes = await hallQuery.OrderBy(s => s.StartTime).ToListAsync();
            var model = new List<HallStatusViewModel>();

            foreach (var show in todayShowtimes)
            {
                // [ADVANCED] Calculate Stats
                // Get bookings for this showtime
                var bookings = await _context.Bookings
                    .Include(b => b.BookingDetails)
                    .Where(b => b.ShowtimeId == show.ShowtimeId && b.Status == BookingStatus.Paid) // Only count PAID bookings
                    .ToListAsync();

                int soldCount = bookings.SelectMany(b => b.BookingDetails).Count();
                decimal revenue = bookings.Sum(b => b.TotalAmount); // Sum actual transaction amounts

                double percentage = (show.MaxSeats > 0) ? (double)soldCount / show.MaxSeats * 100 : 0;

                // Trending Logic
                string status = "Normal";
                var timeUntilStart = show.StartTime - DateTime.Now;

                if (timeUntilStart.TotalMinutes < 0)
                {
                    status = "Ended";
                }
                else
                {
                    if (percentage > 50 && timeUntilStart.TotalHours <= 2) status = "Hot";
                    else if (percentage < 10 && timeUntilStart.TotalHours <= 1) status = "Cold";
                }

                model.Add(new HallStatusViewModel
                {
                    ShowtimeId = show.ShowtimeId, // [NEW] Link ID
                    HallName = (isSuperAdmin ? show.Hall.Cinema.Name + " - " : "") + show.Hall.Name,
                    MovieTitle = show.Movie.Title,
                    MovieImageUrl = show.Movie.ImageUrl,
                    Time = show.StartTime.ToString("HH:mm"),
                    Capacity = show.MaxSeats,
                    Sold = soldCount,
                    Percentage = percentage,
                    CurrentRevenue = revenue, // [NEW] Revenue
                    TrendingStatus = status
                });
            }

            // ==========================================================
            // [UPDATED] SORTING LOGIC: 
            // Put halls with occupancy > 0% at the top.
            // Then sort by Time.
            // ==========================================================
            model = model
                .OrderByDescending(m => m.Percentage > 0) // True (1) comes before False (0)
                .ThenBy(m => m.Time)                    // Secondary sort by Time
                .ToList();

            return View(model);
        }

        // =====================================================================
        // 3. EXPORT REPORT TO CSV
        // =====================================================================
        public async Task<IActionResult> ExportReport(int? branchId, int? movieId)
        {
            var query = await BuildReportQuery(branchId, movieId);
            var bookings = await query.OrderByDescending(b => b.BookingDate).ToListAsync();

            var builder = new StringBuilder();
            builder.AppendLine("Booking ID,Date,Time,Customer Name,Movie Title,Branch,Hall,Total Amount (RM),Status");

            foreach (var b in bookings)
            {
                var date = b.BookingDate.ToString("yyyy-MM-dd");
                var time = b.BookingDate.ToString("HH:mm:ss");
                var customer = b.User?.FullName?.Replace(",", " ") ?? "Guest";
                var movie = b.Showtime?.Movie?.Title?.Replace(",", " ") ?? "Unknown Movie";
                var branch = b.Showtime?.Hall?.Cinema?.Name?.Replace(",", " ") ?? "Unknown Branch";
                var hall = b.Showtime?.Hall?.Name ?? "Unknown Hall";
                var amount = b.TotalAmount.ToString("F2");
                var status = b.Status.ToString();

                builder.AppendLine($"{b.BookingId},{date},{time},{customer},{movie},{branch},{hall},{amount},{status}");
            }

            string fileName = $"SalesReport_{DateTime.Now:yyyyMMdd_HHmm}.csv";
            return File(Encoding.UTF8.GetBytes(builder.ToString()), "text/csv", fileName);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendMonthlyReport(int? branchId, int? movieId)
        {
            try
            {
                var superAdmin = await _context.Users
                    .Where(u => u.Role == UserRole.Admin && u.ManagedCinemaId == null)
                    .FirstOrDefaultAsync();

                if (superAdmin == null || string.IsNullOrEmpty(superAdmin.Email))
                {
                    TempData["Error"] = "Super Admin email not found.";
                    return RedirectToAction("Dashboard");
                }

                var query = await BuildReportQuery(branchId, movieId);
                var startOfMonth = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
                query = query.Where(b => b.BookingDate >= startOfMonth);
                var bookings = await query.OrderByDescending(b => b.BookingDate).ToListAsync();

                var builder = new StringBuilder();
                builder.AppendLine("Booking ID,Date,Time,Customer,Movie,Branch,Amount,Status");

                decimal totalSales = 0;
                foreach (var b in bookings)
                {
                    builder.AppendLine($"{b.BookingId},{b.BookingDate:yyyy-MM-dd},{b.BookingDate:HH:mm},{b.User?.FullName ?? "Guest"},{b.Showtime?.Movie?.Title},{b.Showtime?.Hall?.Cinema?.Name},{b.TotalAmount},{b.Status}");
                    totalSales += b.TotalAmount;
                }

                byte[] csvBytes = Encoding.UTF8.GetBytes(builder.ToString());
                string fileName = $"MonthlyReport_{DateTime.Now:MMM_yyyy}.csv";

                var currentUser = await _context.Users.FindAsync(int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)));

                string subject = $"Monthly Sales Report - {DateTime.Now:MMMM yyyy}";
                string body = $@"
                    <h3>Monthly Sales Report</h3>
                    <p>Generated by: <b>{currentUser?.FullName}</b></p>
                    <p>Period: <b>{DateTime.Now:MMMM yyyy}</b></p>
                    <hr/>
                    <p><b>Summary:</b></p>
                    <ul>
                        <li>Total Bookings: {bookings.Count}</li>
                        <li>Total Revenue: RM {totalSales:N2}</li>
                    </ul>
                    <p>Please find the detailed CSV report attached.</p>";

                await _emailService.SendEmailWithAttachmentAsync(superAdmin.Email, subject, body, csvBytes, fileName);

                TempData["Success"] = $"Report sent successfully to {superAdmin.Email}!";
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Failed to send report: " + ex.Message;
            }

            return RedirectToAction("Dashboard", new { branchId, movieId });
        }

        public async Task<IActionResult> Index()
        {
            if (!IsSuperAdmin()) return RedirectToAction("AccessDenied", "Account");
            return View(await _context.Movies.ToListAsync());
        }

        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();
            var movie = await _context.Movies.FirstOrDefaultAsync(m => m.MovieId == id);
            if (movie == null) return NotFound();
            return View(movie);
        }

        [HttpGet]
        public IActionResult Create()
        {
            if (!IsSuperAdmin()) return RedirectToAction("AccessDenied", "Account");
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Movie movie, bool AutoSchedule, int ShowsPerDay)
        {
            if (!IsSuperAdmin()) return RedirectToAction("AccessDenied", "Account");
            ModelState.Remove("Showtimes");

            if (ModelState.IsValid)
            {
                _context.Add(movie);
                await _context.SaveChangesAsync();

                if (AutoSchedule && movie.Status == MovieStatus.NowShowing)
                {
                    await GenerateAutoSchedule(movie.MovieId, ShowsPerDay);
                }
                return RedirectToAction(nameof(Index));
            }
            return View(movie);
        }

        public async Task<IActionResult> Edit(int? id)
        {
            if (!IsSuperAdmin()) return RedirectToAction("AccessDenied", "Account");
            if (id == null) return NotFound();
            var movie = await _context.Movies.FindAsync(id);
            if (movie == null) return NotFound();
            return View(movie);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Movie movie)
        {
            if (!IsSuperAdmin()) return RedirectToAction("AccessDenied", "Account");
            if (id != movie.MovieId) return NotFound();
            ModelState.Remove("Showtimes");

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(movie);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!_context.Movies.Any(e => e.MovieId == movie.MovieId)) return NotFound();
                    else throw;
                }
                return RedirectToAction(nameof(Index));
            }
            return View(movie);
        }

        public async Task<IActionResult> Delete(int? id)
        {
            if (!IsSuperAdmin()) return RedirectToAction("AccessDenied", "Account");
            if (id == null) return NotFound();
            var movie = await _context.Movies.FirstOrDefaultAsync(m => m.MovieId == id);
            if (movie == null) return NotFound();
            return View(movie);
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            if (!IsSuperAdmin()) return RedirectToAction("AccessDenied", "Account");
            var movie = await _context.Movies.FindAsync(id);
            if (movie != null)
            {
                _context.Movies.Remove(movie);
                await _context.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Index));
        }

        private async Task<IQueryable<Booking>> BuildReportQuery(int? branchId, int? movieId)
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            int userId = int.Parse(userIdString);
            var currentUser = await _context.Users.FindAsync(userId);
            bool isSuperAdmin = currentUser.ManagedCinemaId == null;

            int? activeBranchId = branchId;
            if (!isSuperAdmin) activeBranchId = currentUser.ManagedCinemaId;

            var query = _context.Bookings.AsNoTracking()
                .Include(b => b.Showtime).ThenInclude(s => s.Movie)
                .Include(b => b.Showtime).ThenInclude(s => s.Hall).ThenInclude(h => h.Cinema)
                .Include(b => b.User)
                .AsQueryable();

            if (activeBranchId.HasValue && activeBranchId.Value > 0)
            {
                query = query.Where(b => b.Showtime.Hall.CinemaId == activeBranchId.Value);
            }

            if (movieId.HasValue && movieId.Value > 0)
            {
                query = query.Where(b => b.Showtime.MovieId == movieId.Value);
            }

            return query;
        }

        private async Task GenerateAutoSchedule(int movieId, int showsPerDay)
        {
            var allHalls = await _context.Halls.Include(h => h.Cinema).ToListAsync();
            if (!allHalls.Any()) return;
            var movie = await _context.Movies.FindAsync(movieId);
            if (movie == null) return;
            showsPerDay = Math.Clamp(showsPerDay, 1, 7);
            var existing = await _context.Showtimes.Include(x => x.Movie).ToListAsync();

            var showtimes = new List<Showtime>();
            var random = new Random();
            var today = DateTime.Today;
            int[] possibleHours = { 11, 13, 15, 17, 19, 21, 23 };

            for (int i = 0; i < 7; i++)
            {
                DateTime date = today.AddDays(i);
                var hallsByCinema = allHalls.GroupBy(h => h.CinemaId);

                foreach (var branch in hallsByCinema)
                {
                    var branchHalls = branch.ToList();
                    var selectedHours = possibleHours.OrderBy(x => random.Next()).Take(showsPerDay).OrderBy(x => x).ToList();

                    foreach (var hour in selectedHours)
                    {
                        var proposedStart = date.AddHours(hour);
                        var proposedEnd = proposedStart.AddMinutes(movie.DurationMinutes + 20);
                        var hall = branchHalls.FirstOrDefault(candidate =>
                            !existing.Concat(showtimes).Any(x => x.HallId == candidate.HallId &&
                                proposedStart < x.StartTime.AddMinutes(x.Movie?.DurationMinutes ?? movie.DurationMinutes).AddMinutes(20) &&
                                proposedEnd > x.StartTime));
                        if (hall == null || proposedStart <= DateTime.Now) continue;
                        decimal hallSurcharge = 0;
                        if (hall.Name.Contains("IMAX")) hallSurcharge = 12.00m;
                        else if (hall.Name.Contains("Indulge")) hallSurcharge = 45.00m;

                        bool isWeekend = (date.DayOfWeek == DayOfWeek.Friday || date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday);
                        if (isWeekend) hallSurcharge += 4.00m;

                        showtimes.Add(new Showtime
                        {
                            MovieId = movieId,
                            HallId = hall.HallId,
                            StartTime = proposedStart,
                            TicketPrice = movie.Price + hallSurcharge,
                            MaxSeats = hall.MaxSeats,
                            AvailableSeats = hall.MaxSeats,
                            ShowtimeDate = date
                        });
                    }
                }
            }

            if (showtimes.Any())
            {
                _context.Showtimes.AddRange(showtimes);
                await _context.SaveChangesAsync();
            }
        }

        private bool IsSuperAdmin()
        {
            return !User.HasClaim(c => c.Type == "ManagedCinemaId");
        }

        // =====================================================================
        // [ADVANCED] 4. AJAX SEAT MAP DATA
        // =====================================================================
        [HttpGet]
        public async Task<IActionResult> GetSeatData(int showtimeId)
        {
            var showtime = await _context.Showtimes
                .Include(s => s.Hall).ThenInclude(h => h.Seats)
                .FirstOrDefaultAsync(s => s.ShowtimeId == showtimeId);

            if (showtime == null) return NotFound();

            // Get IDs of all sold seats for this showtime
            var takenSeatIds = await _context.Bookings
                .Where(b => b.ShowtimeId == showtimeId && b.Status == BookingStatus.Paid)
                .SelectMany(b => b.BookingDetails)
                .Select(bd => bd.SeatId)
                .ToListAsync();

            // Map to simple JSON for the frontend
            var seatMap = showtime.Hall.Seats
                .OrderBy(s => s.Row).ThenBy(s => s.Number)
                .Select(s => new {
                    Row = s.Row,
                    Number = s.Number,
                    Type = s.Type.ToString(),
                    IsTaken = takenSeatIds.Contains(s.SeatId)
                })
                .ToList();

            return Json(seatMap);
        }

        // =====================================================================
        // [ADVANCED] 5. EXPORT PASSENGER MANIFEST (GUEST LIST)
        // =====================================================================
        public async Task<IActionResult> ExportManifest(int showtimeId)
        {
            var showtime = await _context.Showtimes
                .Include(s => s.Movie)
                .Include(s => s.Hall).ThenInclude(h => h.Cinema)
                .FirstOrDefaultAsync(s => s.ShowtimeId == showtimeId);

            if (showtime == null) return NotFound();

            var bookings = await _context.Bookings
                .Where(b => b.ShowtimeId == showtimeId && b.Status == BookingStatus.Paid)
                .Include(b => b.User)
                .Include(b => b.BookingDetails).ThenInclude(bd => bd.Seat)
                .ToListAsync();

            var builder = new StringBuilder();
            builder.AppendLine($"MANIFEST FOR: {showtime.Movie.Title} ({showtime.StartTime:HH:mm})");
            builder.AppendLine($"HALL: {showtime.Hall.Cinema.Name} - {showtime.Hall.Name}");
            builder.AppendLine("--------------------------------------------------");
            builder.AppendLine("Seat,Customer Name,Ticket Type,Price (RM)");

            foreach (var b in bookings)
            {
                foreach (var detail in b.BookingDetails)
                {
                    var customer = b.User?.FullName ?? "Guest";
                    var seat = $"{detail.Seat.Row}-{detail.Seat.Number}";
                    var type = detail.Seat.Type.ToString();
                    // Estimate price based on total/count or base price
                    var price = (b.TotalAmount / b.BookingDetails.Count).ToString("F2");

                    builder.AppendLine($"{seat},{customer},{type},{price}");
                }
            }

            string fileName = $"Manifest_{showtime.Movie.Title}_{showtime.StartTime:HHmm}.csv";
            return File(Encoding.UTF8.GetBytes(builder.ToString()), "text/csv", fileName);
        }

        // =====================================================================
        // 6. VIEW GUEST MANIFEST (HTML PAGE)
        // =====================================================================
        public async Task<IActionResult> GuestManifest(int showtimeId)
        {
            var showtime = await _context.Showtimes
                .Include(s => s.Movie)
                .Include(s => s.Hall).ThenInclude(h => h.Cinema)
                .FirstOrDefaultAsync(s => s.ShowtimeId == showtimeId);

            if (showtime == null) return NotFound();

            var bookings = await _context.Bookings
                .Where(b => b.ShowtimeId == showtimeId && b.Status == BookingStatus.Paid)
                .Include(b => b.User)
                .Include(b => b.BookingDetails).ThenInclude(bd => bd.Seat)
                .Include(b => b.BookingSnacks).ThenInclude(bs => bs.Snack)
                .OrderByDescending(b => b.BookingDate)
                .ToListAsync();

            ViewBag.Showtime = showtime;
            return View(bookings);
        }

    } // Closes the Class
} // Closes the Namespace
