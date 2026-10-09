using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using WebAssignment.Data;
using WebAssignment.Models;
using WebAssignment.Helpers;
using Microsoft.AspNetCore.Authorization; // Required for [Authorize]

namespace WebAssignment.Controllers
{
    public class HomeController : Controller
    {
        private readonly AppDbContext _context;
        private readonly EmailService _emailService;
        private readonly SmsService _smsService;
        private readonly ILogger<HomeController> _logger;

        public HomeController(AppDbContext context, EmailService emailService, SmsService smsService, ILogger<HomeController> logger)
        {
            _context = context;
            _emailService = emailService;
            _smsService = smsService;
            _logger = logger;
        }

        public async Task<IActionResult> Index()
        {
            if (User.IsInRole("Admin"))
            {
                return RedirectToAction("Dashboard", "Movies");
            }

            var movies = await _context.Movies
                .Include(m => m.Showtimes)
                .ToListAsync();

            var myWatchlistIds = new List<int>();
            if (User.Identity.IsAuthenticated)
            {
                var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (int.TryParse(userIdString, out int userId))
                {
                    myWatchlistIds = await _context.Watchlists
                        .Where(w => w.UserId == userId)
                        .Select(w => w.MovieId)
                        .ToListAsync();
                }
            }
            ViewBag.WatchlistMovieIds = myWatchlistIds;

            return View(movies);
        }

        // ==========================================
        // [NEW] WATCHLIST METHODS (MISSING IN YOUR CODE)
        // ==========================================
        [Authorize]
        public async Task<IActionResult> MyWatchlist()
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out int userId)) return RedirectToAction("Login", "Account");

            var watchlist = await _context.Watchlists
                .Include(w => w.Movie)
                .Where(w => w.UserId == userId)
                .OrderByDescending(w => w.DateAdded)
                .Select(w => w.Movie)
                .ToListAsync();

            return View(watchlist);
        }

        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleWatchlist(int movieId)
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out int userId)) return Unauthorized();

            var existingItem = await _context.Watchlists
                .FirstOrDefaultAsync(w => w.UserId == userId && w.MovieId == movieId);

            bool isAdded;

            if (existingItem != null)
            {
                _context.Watchlists.Remove(existingItem);
                isAdded = false;
            }
            else
            {
                var newItem = new Watchlist { UserId = userId, MovieId = movieId, DateAdded = DateTime.Now };
                _context.Watchlists.Add(newItem);
                isAdded = true;
            }

            await _context.SaveChangesAsync();
            return Json(new { success = true, isAdded = isAdded });
        }
        // ==========================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SetLanguage(string culture, string returnUrl)
        {
            var allowed = new[] { "en", "ms", "zh" };
            if (!allowed.Contains(culture)) culture = "en";
            Response.Cookies.Append("UserLanguage", culture, new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddYears(1),
                HttpOnly = true,
                IsEssential = true
            });
            return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl : "/");
        }

        public async Task<IActionResult> Details(int id, DateTime? date)
        {
            var movie = await _context.Movies
                .Include(m => m.Showtimes).ThenInclude(s => s.Hall.Cinema)
                .FirstOrDefaultAsync(m => m.MovieId == id);
            if (movie == null) return NotFound();
            var reviews = await _context.Reviews
                .Include(r => r.User)
                .Where(r => r.MovieId == id)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();
            ViewBag.Reviews = reviews;
            ViewBag.ReviewCount = reviews.Count;
            ViewBag.AverageRating = reviews.Any() ? reviews.Average(r => r.Rating) : 0;
            ViewBag.SelectedDate = date.HasValue ? date.Value.Date : DateTime.Today;
            return View(movie);
        }

        public async Task<IActionResult> Snacks()
        {
            var snacks = await _context.Snacks.ToListAsync();
            return View(snacks);
        }

        public IActionResult Locations()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemindMe(int id)
        {
            if (!User.Identity.IsAuthenticated) return RedirectToAction("Login", "Account");

            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdString)) return RedirectToAction("Login", "Account");

            var userId = int.Parse(userIdString);
            var user = await _context.Users.FindAsync(userId);
            var movie = await _context.Movies.FindAsync(id);

            if (user == null || movie == null) return NotFound();

            string subject = $"Reminder: {movie.Title}";
            string body = $@"
                <div style='font-family: Arial, sans-serif; color: #333; max-width: 600px; margin: 0 auto; border: 1px solid #ddd; padding: 20px; border-radius: 10px;'>
                    <h2 style='color: #d4af37; text-align: center;'>Movie Reminder 🎬</h2>
                    <hr style='border: 0; border-top: 1px solid #eee; margin: 20px 0;'>
                    <p style='font-size: 16px;'>Hi <strong>{user.FullName}</strong>,</p>
                    <p style='font-size: 16px;'>This is a reminder for <strong>{movie.Title}</strong>.</p>
                    <br>
                    <p style='font-size: 14px; color: #888; text-align: center;'>See you at Waliu Cinema!</p>
                </div>";

            try
            {
                await _emailService.SendEmailAsync(user.Email, subject, body);
            }
            catch
            {
                TempData["Error"] = "Could not send email.";
            }

            if (!string.IsNullOrWhiteSpace(user.PhoneNumber))
            {
                try
                {
                    await _smsService.SendSmsAsync(
                        user.PhoneNumber,
                        $"[Waliu Cinema] Reminder: {movie.Title} is showing! Book now.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "SMS reminder failed for user {UserId}.", user.UserId);
                    TempData["SmsError"] = "Email sent, but SMS could not be sent.";
                }
            }

            TempData["Success"] = $"Reminder set for {movie.Title}!";
            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddReview(int movieId, int rating, string comment)
        {
            if (!User.Identity.IsAuthenticated) return RedirectToAction("Login", "Account");
            var username = User.Identity.Name;
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);
            if (user == null) return RedirectToAction("Login", "Account");
            if (rating < 1 || rating > 5 || string.IsNullOrWhiteSpace(comment) || comment.Trim().Length > 1000)
                return BadRequest("Rating must be 1-5 and comment must be 1-1000 characters.");
            if (!await _context.Movies.AnyAsync(m => m.MovieId == movieId)) return NotFound();
            var review = await _context.Reviews.FirstOrDefaultAsync(r => r.MovieId == movieId && r.UserId == user.UserId);
            if (review == null)
                _context.Reviews.Add(new Review { MovieId = movieId, UserId = user.UserId, Rating = rating, Comment = comment.Trim(), CreatedAt = DateTime.Now });
            else
            {
                review.Rating = rating;
                review.Comment = comment.Trim();
                review.CreatedAt = DateTime.Now;
            }
            await _context.SaveChangesAsync();
            return RedirectToAction("Details", new { id = movieId });
        }

        public IActionResult Privacy() => View();

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = System.Diagnostics.Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
