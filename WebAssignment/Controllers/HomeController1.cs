using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using WebAssignment.Data;
using WebAssignment.Models;
using WebAssignment.Helpers;

namespace WebAssignment.Controllers
{
    [Authorize(Roles = "Admin")]
    public class StaffController : Controller
    {
        private readonly AppDbContext _context;

        public StaffController(AppDbContext context)
        {
            _context = context;
        }

        // 1. LIST ALL ADMINS
        public async Task<IActionResult> Index()
        {
            // Security Check: Only Super Admin can see this
            if (!IsSuperAdmin()) return RedirectToAction("Dashboard", "Movies");

            var admins = await _context.Users
                .Where(u => u.Role == UserRole.Admin)
                .Include(u => u.ManagedCinema)
                .ToListAsync();

            return View(admins);
        }

        // 2. CREATE NEW ADMIN (GET)
        public IActionResult Create()
        {
            if (!IsSuperAdmin()) return RedirectToAction("Dashboard", "Movies");

            ViewBag.Cinemas = new SelectList(_context.Cinemas, "CinemaId", "Name");
            return View();
        }

        // 3. CREATE NEW ADMIN (POST)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(User user, int? SelectedCinemaId)
        {
            if (!IsSuperAdmin()) return RedirectToAction("Dashboard", "Movies");

            // Manually handle validation for fields we don't need
            ModelState.Remove("UserRewards");

            // Check duplicates
            if (_context.Users.Any(u => u.Username == user.Username || u.Email == user.Email))
            {
                ViewBag.Error = "Username taken.";
                ViewBag.Cinemas = new SelectList(_context.Cinemas, "CinemaId", "Name");
                return View(user);
            }

            // Set Admin Properties
            user.Role = UserRole.Admin;
            user.ManagedCinemaId = SelectedCinemaId; // Null = Super Admin
            user.ProfilePhotoPath = "https://ui-avatars.com/api/?name=" + user.Username + "&background=d4af37&color=000";
            user.Points = 0;
            user.Password = PasswordHelper.HashPassword(user.Password);

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        // 4. EDIT ADMIN (GET)
        public async Task<IActionResult> Edit(int? id)
        {
            if (!IsSuperAdmin()) return RedirectToAction("Dashboard", "Movies");
            if (id == null) return NotFound();

            var user = await _context.Users.FindAsync(id);
            if (user == null) return NotFound();

            ViewBag.Cinemas = new SelectList(_context.Cinemas, "CinemaId", "Name", user.ManagedCinemaId);
            return View(user);
        }

        // 5. EDIT ADMIN (POST)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, User updatedUser, int? SelectedCinemaId)
        {
            if (!IsSuperAdmin()) return Forbid();
            var userInDb = await _context.Users.FindAsync(id);
            if (userInDb == null || userInDb.Role != UserRole.Admin) return NotFound();
            if (await _context.Users.AnyAsync(u => u.UserId != id && u.Email == updatedUser.Email))
            {
                ModelState.AddModelError("Email", "Email is already registered.");
                ViewBag.Cinemas = new SelectList(_context.Cinemas, "CinemaId", "Name", SelectedCinemaId);
                return View(updatedUser);
            }

            userInDb.FullName = updatedUser.FullName;
            userInDb.Email = updatedUser.Email;

            // Only update password if they typed something
            if (!string.IsNullOrEmpty(updatedUser.Password))
            {
                userInDb.Password = PasswordHelper.HashPassword(updatedUser.Password);
            }

            userInDb.ManagedCinemaId = SelectedCinemaId;

            _context.Update(userInDb);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        // 6. DELETE ADMIN
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            if (!IsSuperAdmin()) return RedirectToAction("Dashboard", "Movies");

            var currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            if (id == currentUserId)
            {
                TempData["Error"] = "You cannot delete your own account.";
                return RedirectToAction(nameof(Index));
            }
            var user = await _context.Users.FindAsync(id);
            if (user != null)
            {
                _context.Users.Remove(user);
                await _context.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Index));
        }

        // HELPER: Check if current user is Super Admin
        private bool IsSuperAdmin()
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var user = _context.Users.Find(userId);
            return user != null && user.ManagedCinemaId == null;
        }
    }
}
