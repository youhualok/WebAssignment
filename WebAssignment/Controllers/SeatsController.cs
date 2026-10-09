using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using WebAssignment.Data;
using WebAssignment.Models;

namespace WebAssignment.Controllers
{
    [Authorize(Roles = "Admin")] // Allows both Admin types to enter
    public class SeatsController : Controller
    {
        private readonly AppDbContext _context;

        public SeatsController(AppDbContext context)
        {
            _context = context;
        }

        // 1. LIST CINEMAS (FILTERED BY PERMISSION)
        public async Task<IActionResult> Index()
        {
            // Get Current User Info to check their Branch
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out int userId)) return RedirectToAction("Login", "Account");

            var user = await _context.Users.FindAsync(userId);
            if (user == null) return Unauthorized();
            if (user == null) return Unauthorized();
            if (user == null) return RedirectToAction("Login", "Account");

            // Start query
            IQueryable<Cinema> cinemasQuery = _context.Cinemas.Include(c => c.Halls);

            // [AUTHORIZATION LOGIC]
            // If user has a ManagedCinemaId, FILTER the list.
            // If null, they are Super Admin (Show All).
            if (user.ManagedCinemaId != null)
            {
                cinemasQuery = cinemasQuery.Where(c => c.CinemaId == user.ManagedCinemaId);
            }

            var cinemas = await cinemasQuery.ToListAsync();
            return View(cinemas);
        }

        // 2. MANAGE SPECIFIC HALL
        public async Task<IActionResult> Manage(int hallId)
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out int userId)) return RedirectToAction("Login", "Account");
            var user = await _context.Users.FindAsync(userId);
            if (user == null) return Unauthorized();

            var hall = await _context.Halls
                .Include(h => h.Cinema)
                .Include(h => h.Seats)
                .FirstOrDefaultAsync(h => h.HallId == hallId);

            if (hall == null) return NotFound();

            // [SECURITY CHECK]
            // If user is a Branch Admin, ensure they own this Cinema.
            if (user.ManagedCinemaId != null && user.ManagedCinemaId != hall.CinemaId)
            {
                // Redirect to Index with error if they try to access another branch
                TempData["Error"] = "You are not authorized to manage this cinema.";
                return RedirectToAction("Index");
            }

            return View(hall);
        }

        // 3. TOGGLE SEAT STATUS (AJAX)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleStatus(int seatId)
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out int userId)) return Unauthorized();
            var user = await _context.Users.FindAsync(userId);

            var seat = await _context.Seats
                .Include(s => s.Hall)
                .FirstOrDefaultAsync(s => s.SeatId == seatId);

            if (seat == null) return NotFound();

            // [SECURITY CHECK]
            // Prevent Branch Admins from hacking the AJAX call to edit other cinemas
            if (user.ManagedCinemaId != null && user.ManagedCinemaId != seat.Hall.CinemaId)
            {
                return Json(new { success = false, message = "Unauthorized: This seat belongs to another branch." });
            }

            // Flip the status
            seat.IsAvailable = !seat.IsAvailable;
            await _context.SaveChangesAsync();

            return Json(new
            {
                success = true,
                isAvailable = seat.IsAvailable,
                message = seat.IsAvailable ? "Seat Restored" : "Seat Blocked"
            });
        }
    }
}
