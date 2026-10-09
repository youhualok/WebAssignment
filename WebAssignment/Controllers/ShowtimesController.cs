using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using WebAssignment.Data;
using WebAssignment.Models;
using X.PagedList;
using X.PagedList.Extensions; // Ensure you have this for .ToPagedList

namespace WebAssignment.Controllers
{
    [Authorize(Roles = "Admin,SuperAdmin")]
    public class ShowtimesController : Controller
    {
        private readonly AppDbContext _context;

        public ShowtimesController(AppDbContext context)
        {
            _context = context;
        }

        // ==========================================
        // 1. INDEX (RESTORED: Search + Filter + Sort + Page)
        // ==========================================
        public async Task<IActionResult> Index(string sortOrder, string currentFilter, string searchString, int? branchId, int? page)
        {
            int? managedCinemaId = null;
            bool isSuperAdmin = true;

            var managedCinemaIdClaim = User.FindFirst("ManagedCinemaId")?.Value;
            if (!string.IsNullOrEmpty(managedCinemaIdClaim) && int.TryParse(managedCinemaIdClaim, out int id))
            {
                managedCinemaId = id;
                isSuperAdmin = false;
            }

            if (isSuperAdmin)
            {
                var branches = await _context.Cinemas.OrderBy(c => c.Name).ToListAsync();
                branches.Insert(0, new Cinema { CinemaId = 0, Name = "All Branches" });
                ViewBag.BranchList = new SelectList(branches, "CinemaId", "Name", branchId ?? 0);
            }
            else
            {
                branchId = managedCinemaId;
                ViewBag.IsBranchAdmin = true;
            }

            if (searchString != null) page = 1;
            else searchString = currentFilter;

            ViewData["CurrentSort"] = sortOrder;
            ViewData["CurrentFilter"] = searchString;
            ViewData["BranchFilter"] = branchId;

            ViewData["TitleSortParm"] = String.IsNullOrEmpty(sortOrder) ? "title_desc" : "";
            ViewData["DateSortParm"] = sortOrder == "Date" ? "date_desc" : "Date";
            ViewData["HallSortParm"] = sortOrder == "Hall" ? "hall_desc" : "Hall";
            ViewData["PriceSortParm"] = sortOrder == "Price" ? "price_desc" : "Price";

            var showtimesQuery = _context.Showtimes
                .Include(s => s.Movie)
                .Include(s => s.Hall).ThenInclude(h => h.Cinema)
                .AsNoTracking()
                .AsQueryable();

            if (branchId.HasValue && branchId.Value > 0)
            {
                showtimesQuery = showtimesQuery.Where(s => s.Hall.CinemaId == branchId.Value);
            }

            if (!string.IsNullOrEmpty(searchString))
            {
                showtimesQuery = showtimesQuery.Where(s => s.Movie.Title.Contains(searchString));
            }

            switch (sortOrder)
            {
                case "title_desc": showtimesQuery = showtimesQuery.OrderByDescending(s => s.Movie.Title); break;
                case "Date": showtimesQuery = showtimesQuery.OrderBy(s => s.ShowtimeDate).ThenBy(s => s.StartTime); break;
                case "date_desc": showtimesQuery = showtimesQuery.OrderByDescending(s => s.ShowtimeDate).ThenByDescending(s => s.StartTime); break;
                case "Hall": showtimesQuery = showtimesQuery.OrderBy(s => s.Hall.Name); break;
                case "hall_desc": showtimesQuery = showtimesQuery.OrderByDescending(s => s.Hall.Name); break;
                case "Price": showtimesQuery = showtimesQuery.OrderBy(s => s.TicketPrice); break;
                case "price_desc": showtimesQuery = showtimesQuery.OrderByDescending(s => s.TicketPrice); break;
                default: showtimesQuery = showtimesQuery.OrderByDescending(s => s.ShowtimeDate).ThenBy(s => s.StartTime); break;
            }

            int pageSize = 10;
            int pageNumber = (page ?? 1);

            var allItems = await showtimesQuery.ToListAsync();
            var pagedList = allItems.ToPagedList(pageNumber, pageSize);

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return PartialView("_ShowtimesTablePartial", pagedList);
            }

            return View(pagedList);
        }

        // ==========================================
        // 2. CREATE (GET) - RESTORED
        // ==========================================
        public async Task<IActionResult> Create()
        {
            int? managedCinemaId = null;
            var managedCinemaIdClaim = User.FindFirst("ManagedCinemaId")?.Value;
            if (!string.IsNullOrEmpty(managedCinemaIdClaim) && int.TryParse(managedCinemaIdClaim, out int id))
            {
                managedCinemaId = id;
            }

            ViewData["MovieId"] = new SelectList(_context.Movies.Where(m => m.Status == MovieStatus.NowShowing), "MovieId", "Title");

            IQueryable<Hall> hallsQuery = _context.Halls.Include(h => h.Cinema);
            if (managedCinemaId != null)
            {
                hallsQuery = hallsQuery.Where(h => h.CinemaId == managedCinemaId);
            }

            var hallList = await hallsQuery.Select(h => new { h.HallId, Name = h.Cinema.Name + " - " + h.Name }).ToListAsync();
            ViewData["HallId"] = new SelectList(hallList, "HallId", "Name");

            return View();
        }

        // ==========================================
        // 3. CREATE (POST) - MODIFIED FOR AUTO PRICE
        // ==========================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Showtime showtime)
        {
            if (showtime.MovieId > 0 && showtime.HallId > 0)
            {
                var movie = await _context.Movies.FindAsync(showtime.MovieId);
                var hall = await _context.Halls.FindAsync(showtime.HallId);

                if (movie != null && hall != null)
                {
                    if (!await UserHasAccessToHall(hall.HallId)) return Forbid();
                    if (showtime.StartTime <= DateTime.Now)
                        ModelState.AddModelError("StartTime", "Showtime must be in the future.");
                    if (await HasScheduleConflict(hall.HallId, showtime.StartTime, movie.DurationMinutes))
                        ModelState.AddModelError("StartTime", "This hall already has an overlapping showtime.");
                    if (!ModelState.IsValid)
                    {
                        await PrepareDropdownsForEdit(showtime);
                        return View(showtime);
                    }
                    if (showtime.ShowtimeDate == DateTime.MinValue)
                        showtime.ShowtimeDate = showtime.StartTime.Date;

                    // === AUTOMATIC PRICE LOGIC ===
                    // Uses Movie Base Price + Hall Surcharge
                    decimal surcharge = 0;
                    string hallName = hall.Name.ToUpper();

                    if (hallName.Contains("IMAX")) surcharge = 12.00m;
                    else if (hallName.Contains("INDULGE") || hallName.Contains("GOLD")) surcharge = 45.00m;
                    else if (hallName.Contains("BEANIE")) surcharge = 10.00m;
                    else if (hallName.Contains("ONYX")) surcharge = 8.00m;

                    showtime.TicketPrice = movie.Price + surcharge;
                    // ==============================

                    showtime.MaxSeats = hall.MaxSeats;
                    showtime.AvailableSeats = hall.MaxSeats;

                    _context.Add(showtime);
                    await _context.SaveChangesAsync();
                    return RedirectToAction(nameof(Index));
                }
            }
            // If validation fails, reload lists
            await PrepareDropdownsForEdit(showtime);
            return View(showtime);
        }

        // ==========================================
        // 4. EDIT (GET) - ADDED
        // ==========================================
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var showtime = await _context.Showtimes.FindAsync(id);
            if (showtime == null) return NotFound();

            // Check Permission
            if (!await UserHasAccessToHall(showtime.HallId)) return Forbid();

            await PrepareDropdownsForEdit(showtime);
            return View(showtime);
        }
        // ==========================================
        // 5. EDIT (POST) - FIXED
        // ==========================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Showtime showtime)
        {
            if (id != showtime.ShowtimeId) return NotFound();

            // 1. Get the ORIGINAL showtime from DB to compare values
            // Use AsNoTracking() to avoid conflict with the 'showtime' parameter
            var existingShowtime = await _context.Showtimes
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.ShowtimeId == id);

            if (existingShowtime == null) return NotFound();

            // 2. Check if there are any PAID bookings for this showtime
            bool hasBookings = await _context.Bookings
                .AnyAsync(b => b.ShowtimeId == id && b.Status == BookingStatus.Paid);

            if (showtime.MovieId > 0 && showtime.HallId > 0)
            {
                if (!await UserHasAccessToHall(showtime.HallId)) return Forbid();
                var requestedMovie = await _context.Movies.FindAsync(showtime.MovieId);
                if (requestedMovie == null) return BadRequest("Invalid movie.");
                if (showtime.StartTime <= DateTime.Now)
                    ModelState.AddModelError("StartTime", "Showtime must be in the future.");
                if (await HasScheduleConflict(showtime.HallId, showtime.StartTime, requestedMovie.DurationMinutes, id))
                    ModelState.AddModelError("StartTime", "This hall already has an overlapping showtime.");
                if (!ModelState.IsValid)
                {
                    await PrepareDropdownsForEdit(showtime);
                    return View(showtime);
                }
                // === SAFETY CHECK: PREVENT BREAKING EXISTING BOOKINGS ===
                if (hasBookings)
                {
                    // If tickets are sold, we CANNOT allow changing the Movie or the Hall.
                    // Changing Hall invalidates the specific Seat IDs in the booking.
                    if (showtime.HallId != existingShowtime.HallId || showtime.MovieId != existingShowtime.MovieId)
                    {
                        ModelState.AddModelError("", "Unable to change Movie or Hall because tickets have already been sold. You can only update the Time or Price.");
                        await PrepareDropdownsForEdit(showtime);
                        return View(showtime);
                    }

                    // Logic when Bookings exist:
                    // Keep the MaxSeats from the original (since Hall didn't change)
                    showtime.MaxSeats = existingShowtime.MaxSeats;

                    // IMPORTANT: Calculate AvailableSeats correctly (Total - Sold)
                    // Do NOT reset it to MaxSeats
                    int soldSeats = await _context.Bookings
                        .Where(b => b.ShowtimeId == id && b.Status == BookingStatus.Paid)
                        .SelectMany(b => b.BookingDetails)
                        .CountAsync();

                    showtime.AvailableSeats = showtime.MaxSeats - soldSeats;
                }
                else
                {
                    // Logic when NO Bookings exist:
                    // It is safe to reset everything (change Hall, reset seats)
                    var freshHall = await _context.Halls.FindAsync(showtime.HallId);
                    showtime.MaxSeats = freshHall.MaxSeats;
                    showtime.AvailableSeats = freshHall.MaxSeats; // Reset to full since no tickets sold
                }

                // === UPDATE PRICE & DATE (Allowed for both cases) ===
                var movie = await _context.Movies.FindAsync(showtime.MovieId);
                var hall = await _context.Halls.FindAsync(showtime.HallId);

                decimal surcharge = 0;
                string hallName = hall.Name.ToUpper();

                if (hallName.Contains("IMAX")) surcharge = 12.00m;
                else if (hallName.Contains("INDULGE") || hallName.Contains("GOLD")) surcharge = 45.00m;
                else if (hallName.Contains("BEANIE")) surcharge = 10.00m;
                else if (hallName.Contains("ONYX")) surcharge = 8.00m;

                showtime.TicketPrice = movie.Price + surcharge;

                if (showtime.ShowtimeDate == DateTime.MinValue)
                    showtime.ShowtimeDate = showtime.StartTime.Date;

                try
                {
                    _context.Update(showtime);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!_context.Showtimes.Any(e => e.ShowtimeId == showtime.ShowtimeId)) return NotFound();
                    else throw;
                }
                return RedirectToAction(nameof(Index));
            }

            await PrepareDropdownsForEdit(showtime);
            return View(showtime);
        }
        // ==========================================
        // 6. DELETE (GET) - RESTORED
        // ==========================================
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();

            var showtime = await _context.Showtimes
                .Include(s => s.Movie).Include(s => s.Hall).ThenInclude(h => h.Cinema)
                .FirstOrDefaultAsync(m => m.ShowtimeId == id);

            if (showtime == null) return NotFound();

            if (!await UserHasAccessToHall(showtime.HallId)) return Forbid();

            return View(showtime);
        }

        // ==========================================
        // 7. DELETE (POST) - RESTORED
        // ==========================================
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var showtime = await _context.Showtimes.Include(s => s.Hall).FirstOrDefaultAsync(s => s.ShowtimeId == id);
            if (showtime != null)
            {
                if (!await UserHasAccessToHall(showtime.HallId)) return Forbid();

                if (await _context.Bookings.AnyAsync(b => b.ShowtimeId == id))
                {
                    TempData["Error"] = "A showtime with bookings cannot be deleted.";
                    return RedirectToAction(nameof(Index));
                }
                _context.Showtimes.Remove(showtime);
                await _context.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Index));
        }

        // === HELPERS ===

        private async Task<bool> UserHasAccessToHall(int hallId)
        {
            var managedCinemaIdClaim = User.FindFirst("ManagedCinemaId")?.Value;
            if (!string.IsNullOrEmpty(managedCinemaIdClaim) && int.TryParse(managedCinemaIdClaim, out int managedId))
            {
                var hall = await _context.Halls.FindAsync(hallId);
                if (hall != null && hall.CinemaId != managedId) return false;
            }
            return true;
        }

        private async Task<bool> HasScheduleConflict(int hallId, DateTime start, int durationMinutes, int? excludedId = null)
        {
            var end = start.AddMinutes(durationMinutes + 20);
            var candidates = await _context.Showtimes.Include(x => x.Movie)
                .Where(x => x.HallId == hallId && (!excludedId.HasValue || x.ShowtimeId != excludedId.Value))
                .ToListAsync();
            return candidates.Any(x => start < x.StartTime.AddMinutes(x.Movie!.DurationMinutes + 20) && end > x.StartTime);
        }

        private async Task PrepareDropdownsForEdit(Showtime showtime)
        {
            int? managedCinemaId = null;
            var managedCinemaIdClaim = User.FindFirst("ManagedCinemaId")?.Value;
            if (!string.IsNullOrEmpty(managedCinemaIdClaim) && int.TryParse(managedCinemaIdClaim, out int id))
            {
                managedCinemaId = id;
            }

            ViewData["MovieId"] = new SelectList(_context.Movies, "MovieId", "Title", showtime.MovieId);

            IQueryable<Hall> hallsQuery = _context.Halls.Include(h => h.Cinema);
            if (managedCinemaId != null)
            {
                hallsQuery = hallsQuery.Where(h => h.CinemaId == managedCinemaId);
            }
            var hallList = await hallsQuery.Select(h => new { h.HallId, Name = h.Cinema.Name + " - " + h.Name }).ToListAsync();
            ViewData["HallId"] = new SelectList(hallList, "HallId", "Name", showtime.HallId);
        }
    }
}
