using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebAssignment.Data;
using WebAssignment.Models;

namespace WebAssignment.Controllers
{
    // 1. Authorize only Admins initially
    [Authorize(Roles = "Admin")]
    public class SnacksController : Controller
    {
        private readonly AppDbContext _context;

        public SnacksController(AppDbContext context)
        {
            _context = context;
        }

        // === HELPER: CHECK SUPER ADMIN ===
        // Returns TRUE if user is Super Admin (No ManagedCinemaId claim)
        private bool IsSuperAdmin()
        {
            // If the user has a "ManagedCinemaId" claim, they are a Branch Admin, NOT Super Admin.
            return !User.HasClaim(c => c.Type == "ManagedCinemaId");
        }

        // 1. LIST ALL SNACKS (READ)
        public async Task<IActionResult> Index()
        {
            if (!IsSuperAdmin()) return RedirectToAction("AccessDenied", "Account");
            return View(await _context.Snacks.ToListAsync());
        }

        // 2. CREATE SNACK (GET)
        public IActionResult Create()
        {
            if (!IsSuperAdmin()) return RedirectToAction("AccessDenied", "Account");
            return View();
        }

        // 2. CREATE SNACK (POST)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Snack snack)
        {
            if (!IsSuperAdmin()) return RedirectToAction("AccessDenied", "Account");

            if (ModelState.IsValid)
            {
                _context.Add(snack);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            return View(snack);
        }

        // 3. EDIT SNACK (GET)
        public async Task<IActionResult> Edit(int? id)
        {
            if (!IsSuperAdmin()) return RedirectToAction("AccessDenied", "Account");
            if (id == null) return NotFound();

            var snack = await _context.Snacks.FindAsync(id);
            if (snack == null) return NotFound();
            return View(snack);
        }

        // 3. EDIT SNACK (POST)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Snack snack)
        {
            if (!IsSuperAdmin()) return RedirectToAction("AccessDenied", "Account");
            if (id != snack.SnackId) return NotFound();

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(snack);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!_context.Snacks.Any(e => e.SnackId == snack.SnackId)) return NotFound();
                    else throw;
                }
                return RedirectToAction(nameof(Index));
            }
            return View(snack);
        }

        // 4. DELETE SNACK (GET)
        public async Task<IActionResult> Delete(int? id)
        {
            if (!IsSuperAdmin()) return RedirectToAction("AccessDenied", "Account");
            if (id == null) return NotFound();

            var snack = await _context.Snacks.FirstOrDefaultAsync(m => m.SnackId == id);
            if (snack == null) return NotFound();

            return View(snack);
        }

        // 4. DELETE SNACK (POST)
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            if (!IsSuperAdmin()) return RedirectToAction("AccessDenied", "Account");

            var snack = await _context.Snacks.FindAsync(id);
            if (snack != null)
            {
                _context.Snacks.Remove(snack);
                await _context.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Index));
        }
    }
}