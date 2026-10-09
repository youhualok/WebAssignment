using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims; // Required for User.HasClaim
using WebAssignment.Data;
using WebAssignment.Models;

namespace WebAssignment.Controllers
{
    // Only Admins or SuperAdmins can access this
    public class ReviewsController : Controller
    {
        private readonly AppDbContext _context;

        public ReviewsController(AppDbContext context)
        {
            _context = context;
        }

        // =============================================================
        // 1. LIST ALL REVIEWS (Dashboard)
        // =============================================================
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Index()
        {
            // SECURITY CHECK: Super Admin Only
            if (!IsSuperAdmin()) return RedirectToAction("AccessDenied", "Account");

            var reviews = await _context.Reviews
                .Include(r => r.Movie)
                .Include(r => r.User)
                .OrderByDescending(r => r.CreatedAt) // Newest first
                .ToListAsync();

            return View(reviews);
        }

        // =============================================================
        // 2. USER SUBMITS A NEW RATING/REVIEW (CUSTOMER FUNCTION)
        // =============================================================
        [HttpPost]
        [Authorize(Roles = "User")] // Typically only logged-in Users can submit a review
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubmitReview(int MovieId, int RatingValue, string? Comment)
        {
            // 1. Get UserId
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out int userId)) return RedirectToAction("Login", "Account");

            // 2. Input Validation
            if (RatingValue < 1 || RatingValue > 5)
            {
                // This shouldn't happen if the JS fix on the frontend is implemented
                TempData["Error"] = "Invalid rating value submitted.";
                return RedirectToAction("Details", "Movies", new { id = MovieId });
            }

            Comment = Comment?.Trim();
            if (string.IsNullOrWhiteSpace(Comment) || Comment.Length > 1000 || !await _context.Movies.AnyAsync(m => m.MovieId == MovieId))
                return BadRequest("Invalid movie or comment.");

            // Optional: Check if the user has already reviewed this movie
            var existingReview = await _context.Reviews
                .FirstOrDefaultAsync(r => r.MovieId == MovieId && r.UserId == userId);

            if (existingReview != null)
            {
                // Update existing review
                existingReview.Rating = RatingValue;
                existingReview.Comment = Comment;
                existingReview.CreatedAt = DateTime.Now; // Update timestamp
                _context.Update(existingReview);
                TempData["Success"] = "Your review has been updated.";
            }
            else
            {
                // Create new review
                var newReview = new Review
                {
                    MovieId = MovieId,
                    UserId = userId,
                    Rating = RatingValue,
                    Comment = Comment,
                    CreatedAt = DateTime.Now
                };
                _context.Reviews.Add(newReview);
                TempData["Success"] = "Your review has been submitted!";
            }

            await _context.SaveChangesAsync();

            // Redirect back to the movie details page (assuming that's where the form lives)
            return RedirectToAction("Details", "Home", new { id = MovieId });
        }


        // =============================================================
        // 4. HANDLE THE ADMIN REPLY SUBMISSION
        // =============================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> AddReply(int reviewId, string replyText)
        {
            // SECURITY CHECK: Super Admin Only
            if (!IsSuperAdmin()) return RedirectToAction("AccessDenied", "Account");

            var review = await _context.Reviews.FindAsync(reviewId);

            if (review != null && !string.IsNullOrWhiteSpace(replyText) && replyText.Trim().Length <= 1000)
            {
                review.Reply = replyText;
                review.ReplyDate = DateTime.Now;

                _context.Update(review);
                await _context.SaveChangesAsync();
            }

            return RedirectToAction(nameof(Index));
        }

        // =============================================================
        // 5. DELETE REVIEW (Optional, for moderation)
        // =============================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(int id)
        {
            // SECURITY CHECK: Super Admin Only
            if (!IsSuperAdmin()) return RedirectToAction("AccessDenied", "Account");

            var review = await _context.Reviews.FindAsync(id);
            if (review != null)
            {
                _context.Reviews.Remove(review);
                await _context.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Index));
        }

        // =============================================================
        // HELPER: CHECK SUPER ADMIN
        // =============================================================
        private bool IsSuperAdmin()
        {
            // If the user has "ManagedCinemaId" claim, they are a BRANCH ADMIN.
            // We want to return TRUE only if they are NOT a branch admin.
            return !User.HasClaim(c => c.Type == "ManagedCinemaId");
        }
    }
}
