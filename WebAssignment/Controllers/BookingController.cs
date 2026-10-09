using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using WebAssignment.Data;
using WebAssignment.Models;
using Microsoft.AspNetCore.SignalR;
using WebAssignment.Hubs;
using System.Text.Json; // REQUIRED FOR PRG PATTERN
using System.Data;

namespace WebAssignment.Controllers
{
    [Authorize]
    public class BookingController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IHubContext<ChatHub> _hubContext;

        public BookingController(AppDbContext context, IHubContext<ChatHub> hubContext)
        {
            _context = context;
            _hubContext = hubContext;
        }

        // =========================================================
        // 1. SELECT SEATS (With Back Button Logic)
        // =========================================================
        public IActionResult SelectSeats(int showtimeId, string? cachedSeats)
        {
            var showtime = _context.Showtimes
                .Include(s => s.Movie).Include(s => s.Hall).ThenInclude(h => h.Seats).Include(s => s.Hall.Cinema)
                .FirstOrDefault(s => s.ShowtimeId == showtimeId);

            if (showtime == null) return RedirectToAction("Index", "Home");

            var bookedSeatIds = _context.BookingDetails
                .Where(bd => bd.Booking.ShowtimeId == showtimeId)
                .Select(bd => bd.SeatId).ToList();

            ViewBag.BookedSeatIds = bookedSeatIds;

            // Restore previously selected seats
            List<int> preSelected = new List<int>();
            if (!string.IsNullOrEmpty(cachedSeats))
            {
                try
                {
                    preSelected = cachedSeats.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                             .Select(int.Parse)
                                             .ToList();
                }
                catch { /* Ignore parsing errors */ }
            }
            ViewBag.PreSelectedSeats = preSelected;

            return View(showtime);
        }

        // 2. SELECT SNACKS
        [HttpPost]
        [ValidateAntiForgeryToken]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public async Task<IActionResult> SelectSnacks(int showtimeId, List<int>? selectedSeatIds)
        {
            ViewBag.SelectedSeatIds = selectedSeatIds ?? new List<int>();
            ViewBag.ShowtimeId = showtimeId;
            var snacks = await _context.Snacks.ToListAsync();
            return View(snacks);
        }

        // =========================================================
        // 3. SNACK CHECKOUT (PRG PATTERN APPLIED)
        // =========================================================

        // STEP 1: RECEIVE DATA (POST) -> SAVE TO SESSION -> REDIRECT
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SnackCheckout(int? showtimeId, List<int>? selectedSeatIds, List<SnackCartItem>? cartItems)
        {
            // 1. Save data to Session so it persists after redirect
            HttpContext.Session.SetInt32("ShowtimeId", showtimeId ?? 0);
            HttpContext.Session.SetString("SelectedSeatIds", JsonSerializer.Serialize(selectedSeatIds ?? new List<int>()));
            HttpContext.Session.SetString("CartItems", JsonSerializer.Serialize(cartItems ?? new List<SnackCartItem>()));

            // 2. Redirect to the GET version
            return RedirectToAction("PaymentPage");
        }

        // STEP 2: DISPLAY PAGE (GET) -> SAFE TO RELOAD
        [HttpGet("Booking/Payment")]
        public async Task<IActionResult> PaymentPage()
        {
            // 1. Retrieve data from Session
            int? showtimeId = HttpContext.Session.GetInt32("ShowtimeId");
            if (showtimeId == 0) showtimeId = null;

            var seatIdsJson = HttpContext.Session.GetString("SelectedSeatIds");
            var selectedSeatIds = seatIdsJson != null
                ? JsonSerializer.Deserialize<List<int>>(seatIdsJson)
                : new List<int>();

            var cartItemsJson = HttpContext.Session.GetString("CartItems");
            var cartItems = cartItemsJson != null
                ? JsonSerializer.Deserialize<List<SnackCartItem>>(cartItemsJson)
                : new List<SnackCartItem>();

            // --- ORIGINAL LOGIC STARTS HERE ---

            Showtime? showtime = null;
            if (showtimeId.HasValue && showtimeId.Value > 0)
            {
                showtime = await _context.Showtimes
                    .Include(s => s.Movie).Include(s => s.Hall.Cinema)
                    .FirstOrDefaultAsync(s => s.ShowtimeId == showtimeId);
            }

            // A. Calculate Tickets
            decimal ticketTotal = 0;
            var seatObjects = await _context.Seats.Where(s => selectedSeatIds.Contains(s.SeatId)).ToListAsync();

            if (seatObjects.Any() && showtime != null)
            {
                decimal basePrice = showtime.TicketPrice;
                if (new[] { DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday }.Contains(showtime.StartTime.DayOfWeek))
                {
                    basePrice += 3.00m;
                }

                foreach (var seat in seatObjects)
                {
                    decimal seatPrice = basePrice;
                    if (seat.Row == "E") seatPrice = (basePrice * 2) + 4;
                    ticketTotal += seatPrice;
                }
            }
            ViewBag.SelectedSeats = seatObjects.OrderBy(s => s.Row).ThenBy(s => s.Number).ToList();

            // B. Calculate Snacks
            decimal snackTotal = 0;
            var displaySnacks = new List<dynamic>();

            foreach (var item in cartItems)
            {
                if (item.Quantity > 0)
                {
                    var s = await _context.Snacks.FindAsync(item.SnackId);
                    if (s != null)
                    {
                        decimal itemPrice = s.Price;
                        string cust = item.Customization ?? "";

                        if (cust.Contains("Large")) itemPrice += 2.00m;
                        if (cust.Contains("Royale")) itemPrice += 3.00m;
                        if (cust.Contains("Extra Butter")) itemPrice += 1.00m;

                        decimal lineTotal = itemPrice * item.Quantity;
                        snackTotal += lineTotal;

                        displaySnacks.Add(new
                        {
                            Snack = s,
                            Quantity = item.Quantity,
                            Customization = cust,
                            LineTotal = lineTotal
                        });
                    }
                }
            }

            decimal subTotal = ticketTotal + snackTotal;
            decimal processingFee = seatObjects.Count * 0.50m;
            decimal grandTotal = subTotal + processingFee;

            if (grandTotal <= 0) return RedirectToAction("Index", "Home");

            // D. === SMART SUGGESTION ENGINE ===
            bool hasMixedCart = (ticketTotal > 0) && (snackTotal > 0);
            bool hitMinSpend = (grandTotal >= 200);
            bool canStack = hasMixedCart && hitMinSpend;

            ViewBag.CanStack = canStack;

            bool isPremiumHall = showtime != null &&
                                 (showtime.Hall.Name.Contains("Beanie") || showtime.Hall.Name.Contains("IMAX") || showtime.Hall.Name.Contains("Indulge"));

            var suggestions = new List<dynamic>();

            // 1. Check Personal Rewards
            if (User.Identity.IsAuthenticated)
            {
                var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (int.TryParse(userIdString, out int userId))
                {
                    var myRewards = await _context.UserRewards
                        .Where(r => r.UserId == userId && !r.IsUsed)
                        .ToListAsync();

                    foreach (var r in myRewards)
                    {
                        decimal potentialDiscount = 0;
                        string desc = r.RewardName;

                        if (r.PromoCode.StartsWith("SNACK"))
                        {
                            if (ticketTotal == 0 && snackTotal > 0)
                            {
                                potentialDiscount = 5.00m;
                                if (potentialDiscount > snackTotal) potentialDiscount = snackTotal;
                            }
                        }
                        else if (r.PromoCode.StartsWith("BUNDLE"))
                        {
                            // BUNDLE: Valid for mixed cart (Standard or Premium)
                            if (ticketTotal > 0 && snackTotal > 0)
                            {
                                potentialDiscount = 10.00m;
                            }
                        }
                        else if (r.PromoCode.StartsWith("FREE"))
                        {
                            // FREE: Valid only if NOT Premium
                            if (ticketTotal > 0 && !isPremiumHall)
                            {
                                potentialDiscount = 25.00m;
                            }
                        }

                        if (potentialDiscount > 0)
                        {
                            suggestions.Add(new { Code = r.PromoCode, Desc = desc, Amount = potentialDiscount, Type = "Personal", IsStackable = false });
                        }
                    }
                }
            }

            // 2. Check Global Promos
            var globalPromos = await _context.PromoCodes.Where(p => p.IsActive).ToListAsync();
            foreach (var p in globalPromos)
            {
                decimal potentialDiscount = 0;
                bool isStackable = false;

                if (p.Code == "WALIU10")
                {
                    if (hasMixedCart)
                    {
                        potentialDiscount = subTotal * 0.10m;
                        if (canStack) isStackable = true;
                    }
                }
                else if (p.Code == "YUMMY5")
                {
                    if (snackTotal > 0 && ticketTotal == 0) potentialDiscount = snackTotal * 0.05m;
                }
                else
                {
                    potentialDiscount = p.DiscountAmount;
                }

                if (potentialDiscount > 0)
                {
                    suggestions.Add(new { Code = p.Code, Desc = "Special Promo", Amount = potentialDiscount, Type = "Global", IsStackable = isStackable });
                }
            }

            // --- FIX: INCREASED LIMIT TO 6 SO BUNDLE DOESN'T GET HIDDEN ---
            ViewBag.SuggestedPromos = suggestions.OrderByDescending(x => x.Amount).Take(6).ToList();

            ViewBag.Showtime = showtime;
            ViewBag.SelectedSeatIds = selectedSeatIds;
            ViewBag.CartItems = cartItems;
            ViewBag.DisplaySnacks = displaySnacks;

            ViewBag.SubTotal = subTotal;
            ViewBag.ProcessingFee = processingFee;
            ViewBag.GrandTotal = grandTotal;

            ViewBag.TicketTotal = ticketTotal;
            ViewBag.SnackTotal = snackTotal;

            return View("PaymentPage");
        }

        // ==========================================
        // 4. VALIDATE PROMO
        // ==========================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ValidatePromo(string code, int showtimeId, string currentAppliedCode, decimal ticketTotal, decimal snackTotal)
        {
            decimal grandTotal = ticketTotal + snackTotal;
            string cleanCode = code?.Trim().ToUpper();

            var showtime = await _context.Showtimes.Include(s => s.Hall).FirstOrDefaultAsync(s => s.ShowtimeId == showtimeId);
            bool isPremiumHall = showtime != null && (showtime.Hall.Name.Contains("Beanie") || showtime.Hall.Name.Contains("IMAX") || showtime.Hall.Name.Contains("Indulge"));

            async Task<(decimal amount, string type, string finalCode)> CalculateDiscount(string promoCode)
            {
                if (User.Identity.IsAuthenticated)
                {
                    var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
                    if (int.TryParse(userIdString, out int userId))
                    {
                        var reward = await _context.UserRewards.FirstOrDefaultAsync(r => r.PromoCode == promoCode && r.UserId == userId);
                        if (reward != null)
                        {
                            if (reward.IsUsed) return (0, "Used", promoCode);

                            decimal val = 0;
                            if (promoCode.StartsWith("SNACK"))
                            {
                                if (ticketTotal > 0) return (0, "ReqNotMet", promoCode);
                                val = 5.00m;
                            }
                            else if (promoCode.StartsWith("BUNDLE"))
                            {
                                if (ticketTotal == 0 || snackTotal == 0) return (0, "ReqNotMet", promoCode);
                                val = 10.00m;
                            }
                            else if (promoCode.StartsWith("FREE"))
                            {
                                if (isPremiumHall) return (0, "PremiumError", promoCode);
                                val = 25.00m;
                            }
                            return (val, "Personal", promoCode);
                        }
                    }
                }

                var promo = await _context.PromoCodes.FirstOrDefaultAsync(p => p.Code == promoCode && p.IsActive);
                if (promo != null)
                {
                    decimal val = 0;
                    if (promo.Code == "WALIU10")
                    {
                        if (ticketTotal > 0 && snackTotal > 0) val = grandTotal * 0.10m;
                        else return (0, "ReqNotMet_Waliu", promoCode);
                    }
                    else if (promo.Code == "YUMMY5")
                    {
                        if (snackTotal > 0 && ticketTotal == 0) val = snackTotal * 0.05m;
                        else return (0, "ReqNotMet_Yummy", promoCode);
                    }
                    else val = promo.DiscountAmount;

                    return (val, "Global", promoCode);
                }

                return (0, "NotFound", promoCode);
            }

            var newResult = await CalculateDiscount(cleanCode);

            if (newResult.type == "NotFound") return Json(new { success = false, message = "Invalid code." });
            if (newResult.type == "Used") return Json(new { success = false, message = "Code already used." });
            if (newResult.type == "PremiumError") return Json(new { success = false, message = "Free Ticket is valid for Standard Halls only." });

            if (newResult.type.StartsWith("ReqNotMet"))
            {
                if (newResult.type == "ReqNotMet_Waliu") return Json(new { success = false, message = "WALIU10 requires both Tickets & Snacks in cart." });
                if (newResult.type == "ReqNotMet_Yummy") return Json(new { success = false, message = "YUMMY5 is for Snack-Only orders (No Tickets)." });

                if (cleanCode.StartsWith("SNACK")) return Json(new { success = false, message = "RM5 Off Voucher is valid for Snack-Only orders." });
                if (cleanCode.StartsWith("BUNDLE")) return Json(new { success = false, message = "Bundle Voucher requires both Tickets and Snacks." });

                return Json(new { success = false, message = "Cart requirements not met." });
            }

            bool isMixedCart = (ticketTotal > 0) && (snackTotal > 0);
            bool hitStackingMinSpend = (grandTotal >= 200);
            bool canStack = isMixedCart && hitStackingMinSpend;

            if (!string.IsNullOrEmpty(currentAppliedCode) && currentAppliedCode != cleanCode && !currentAppliedCode.Contains(','))
            {
                var oldResult = await CalculateDiscount(currentAppliedCode);

                bool isGlobalNew = newResult.finalCode == "WALIU10";
                bool isPersonalNew = newResult.type == "Personal";

                bool isGlobalOld = oldResult.finalCode == "WALIU10";
                bool isPersonalOld = oldResult.type == "Personal";

                bool isStackablePair = (isGlobalNew && isPersonalOld) || (isGlobalOld && isPersonalNew);

                if (canStack && isStackablePair)
                {
                    decimal totalDiscount = newResult.amount + oldResult.amount;
                    if (totalDiscount > grandTotal) totalDiscount = grandTotal;

                    return Json(new
                    {
                        success = true,
                        discount = totalDiscount,
                        combinedCode = oldResult.finalCode + "," + newResult.finalCode,
                        message = "Super Stack Applied! (WALIU10 + Voucher)"
                    });
                }
            }

            decimal finalDiscount = newResult.amount;
            if (finalDiscount > grandTotal) finalDiscount = grandTotal;

            return Json(new
            {
                success = true,
                discount = finalDiscount,
                combinedCode = newResult.finalCode,
                message = "Promo Applied!"
            });
        }

        // ==========================================
        // 5. CONFIRM BOOKING
        // ==========================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmBooking(int? showtimeId, List<int>? selectedSeatIds, List<SnackCartItem>? cartItems,
            string? promoCodeInput, string? paymentType, string? cardNumber, string? cardExpiry, string? cardCvv, string? selectedBank)
        {
            paymentType = paymentType?.Trim();
            if (paymentType is not ("Card" or "TNG" or "FPX")) return BadRequest("Invalid payment method.");
            if (paymentType == "Card" && !IsValidCard(cardNumber, cardExpiry, cardCvv))
                return BadRequest("Invalid card details.");
            if (paymentType == "FPX" && string.IsNullOrWhiteSpace(selectedBank))
                return BadRequest("Please select a bank.");
            await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            int? validShowtimeId = (showtimeId.HasValue && showtimeId.Value > 0) ? showtimeId : null;
            selectedSeatIds = (selectedSeatIds ?? new List<int>()).Where(x => x > 0).Distinct().ToList();
            cartItems = cartItems ?? new List<SnackCartItem>();
            if (cartItems.Any(x => x.Quantity < 0 || x.Quantity > 20)) return BadRequest("Invalid snack quantity.");
            cartItems = cartItems
                .Where(x => x.Quantity > 0)
                .GroupBy(x => new { x.SnackId, x.Customization })
                .Select(x => new SnackCartItem
                {
                    SnackId = x.Key.SnackId,
                    Customization = x.Key.Customization,
                    Quantity = x.Sum(y => y.Quantity)
                })
                .ToList();

            if (cartItems.Any(x => x.Quantity > 20))
            {
                return BadRequest("A snack item cannot exceed 20 units.");
            }
            if (!validShowtimeId.HasValue && !cartItems.Any()) return BadRequest("The order is empty.");

            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (!int.TryParse(userIdString, out int userId))
            {
                return RedirectToAction("Login", "Account");
            }

            string userName = User.Identity?.Name ?? "User";

            var booking = new Booking
            {
                UserId = userId,
                ShowtimeId = validShowtimeId,
                BookingDate = DateTime.Now,
                Status = BookingStatus.Paid,
                BookingDetails = new List<BookingDetail>(),
                BookingSnacks = new List<BookingSnack>()
            };

            decimal ticketTotal = 0;
            decimal snackTotal = 0;
            string movieTitle = "Snack Order";
            string branchName = "Online Store";
            bool isPremiumHall = false;

            if (validShowtimeId.HasValue && selectedSeatIds.Any())
            {
                var showtime = await _context.Showtimes
                    .Include(s => s.Hall).ThenInclude(h => h.Cinema)
                    .Include(s => s.Movie)
                    .FirstOrDefaultAsync(s => s.ShowtimeId == validShowtimeId);

                var seats = await _context.Seats.Where(s => selectedSeatIds.Contains(s.SeatId)).ToListAsync();

                if (showtime != null)
                {
                    if (showtime.StartTime <= DateTime.Now) return BadRequest("This showtime has already started.");
                    if (seats.Count != selectedSeatIds.Count || seats.Any(s => s.HallId != showtime.HallId || !s.IsAvailable))
                        return BadRequest("One or more selected seats are invalid.");
                    var alreadyBooked = await _context.BookingDetails
                        .AnyAsync(d => selectedSeatIds.Contains(d.SeatId) && d.Booking!.ShowtimeId == validShowtimeId && d.Booking.Status == BookingStatus.Paid);
                    if (alreadyBooked) return Conflict("One or more seats have just been booked. Please select again.");
                    movieTitle = showtime.Movie.Title;
                    branchName = showtime.Hall.Cinema.Name;

                    if (showtime.Hall.Name.Contains("Beanie") || showtime.Hall.Name.Contains("IMAX")) isPremiumHall = true;

                    decimal basePrice = showtime.TicketPrice;
                    if (new[] { DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday }.Contains(showtime.StartTime.DayOfWeek))
                        basePrice += 3.00m;

                    foreach (var seat in seats)
                    {
                        decimal seatPrice = basePrice;
                        if (seat.Row == "E") seatPrice = (basePrice * 2) + 4;
                        ticketTotal += seatPrice;
                        booking.BookingDetails.Add(new BookingDetail
                        {
                            SeatId = seat.SeatId,
                            ShowtimeId = validShowtimeId
                        });
                    }
                    if (showtime.AvailableSeats < seats.Count) return Conflict("Not enough seats remain.");
                    showtime.AvailableSeats -= seats.Count;
                }
                else return NotFound("Showtime not found.");
            }
            else if (validShowtimeId.HasValue) return BadRequest("Please select at least one seat.");

            foreach (var item in cartItems)
            {
                if (item.Quantity > 0)
                {
                    var s = await _context.Snacks.FindAsync(item.SnackId);
                    if (s != null)
                    {
                        decimal itemPrice = s.Price;
                        string custString = NormalizeCustomization(item.Customization);

                        if (custString.Contains("Large")) itemPrice += 2.00m;
                        if (custString.Contains("Extra Butter")) itemPrice += 1.00m;
                        if (custString.Contains("Royale")) itemPrice += 3.00m;

                        snackTotal += itemPrice * item.Quantity;

                        booking.BookingSnacks.Add(new BookingSnack
                        {
                            SnackId = item.SnackId,
                            Quantity = item.Quantity,
                            Customization = custString
                        });
                    }
                }
            }

            decimal discount = 0;
            List<UserReward> rewardsToMarkUsed = new List<UserReward>();
            decimal combinedTotal = ticketTotal + snackTotal;
            bool isMixedCart = (ticketTotal > 0 && snackTotal > 0);

            if (!string.IsNullOrEmpty(promoCodeInput))
            {
                var codes = promoCodeInput.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim().ToUpperInvariant()).Distinct().Take(2).ToArray();

                if (codes.Length > 1)
                {
                    if (!isMixedCart || combinedTotal < 200)
                    {
                        codes = new[] { codes[0] };
                    }
                }

                foreach (var c in codes)
                {
                    var code = c;

                    if (code == "WALIU10")
                    {
                        if (isMixedCart) discount += combinedTotal * 0.10m;
                    }
                    else if (code == "YUMMY5" && snackTotal > 0 && ticketTotal == 0)
                        discount += snackTotal * 0.05m;
                    else
                    {
                        var p = await _context.PromoCodes.FirstOrDefaultAsync(x => x.Code == code && x.IsActive);
                        if (p != null) discount += p.DiscountAmount;

                        var r = await _context.UserRewards.FirstOrDefaultAsync(x => x.PromoCode == code && x.UserId == userId);
                        if (r != null && !r.IsUsed)
                        {
                            bool applied = false;
                            if (r.PromoCode.StartsWith("SNACK") && ticketTotal == 0 && snackTotal > 0) { discount += 5m; applied = true; }
                            else if (r.PromoCode.StartsWith("BUNDLE") && ticketTotal > 0 && snackTotal > 0) { discount += 10m; applied = true; }
                            else if (r.PromoCode.StartsWith("FREE") && ticketTotal > 0 && !isPremiumHall) { discount += Math.Min(25m, ticketTotal); applied = true; }
                            if (applied) rewardsToMarkUsed.Add(r);
                        }
                    }
                }
            }
            if (discount > combinedTotal) discount = combinedTotal;

            booking.DiscountAmount = discount;
            booking.AppliedPromoCode = promoCodeInput;

            decimal processingFee = booking.BookingDetails.Count * 0.50m;
            booking.TotalAmount = (combinedTotal - discount) + processingFee;
            if (booking.TotalAmount < 0) booking.TotalAmount = 0;

            if (User.Identity.IsAuthenticated)
            {
                int pointsEarned = (int)Math.Floor(booking.TotalAmount);
                if (pointsEarned > 0)
                {
                    var currentUser = await _context.Users.FindAsync(userId);
                    if (currentUser != null)
                    {
                        currentUser.Points += pointsEarned;
                        _context.Users.Update(currentUser);
                    }
                }
            }

            _context.Bookings.Add(booking);
            foreach (var r in rewardsToMarkUsed) { r.IsUsed = true; }

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            HttpContext.Session.Remove("CartItems");
            HttpContext.Session.Remove("SelectedSeatIds");
            HttpContext.Session.Remove("ShowtimeId");
            HttpContext.Session.SetInt32("LastBookingId", booking.BookingId);

            try
            {
                await _hubContext.Clients.Group("Admins").SendAsync("ReceiveOrderUpdate",
                    booking.BookingId,
                    movieTitle,
                    branchName,
                    userName,
                    booking.TotalAmount.ToString("N2"),
                    booking.BookingDate.ToString("HH:mm"),
                    booking.UserId
                );
            }
            catch (Exception ex)
            {
                Console.WriteLine("SignalR Error: " + ex.Message);
            }

            return RedirectToAction("BookingSuccess", new { id = booking.BookingId });
        }

        private static bool IsValidCard(string? number, string? expiry, string? cvv)
        {
            var digits = new string((number ?? "").Where(char.IsDigit).ToArray());
            if (digits.Length is < 13 or > 19 || cvv == null || !cvv.All(char.IsDigit) || cvv.Length is < 3 or > 4) return false;
            if (!DateTime.TryParseExact(expiry, "MM/yy", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var expiryMonth)) return false;
            if (expiryMonth.AddMonths(1) <= new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1)) return false;
            int sum = 0; bool alternate = false;
            for (int i = digits.Length - 1; i >= 0; i--)
            {
                int n = digits[i] - '0';
                if (alternate && (n *= 2) > 9) n -= 9;
                sum += n; alternate = !alternate;
            }
            return sum % 10 == 0;
        }

        private static string NormalizeCustomization(string? customization)
        {
            if (string.IsNullOrWhiteSpace(customization))
            {
                return string.Empty;
            }

            var allowedOptions = new[]
            {
        "Large",
        "Royale",
        "Extra Butter"
    };

            var selectedOptions = allowedOptions.Where(option =>
                customization.Contains(
                    option,
                    StringComparison.OrdinalIgnoreCase
                )
            );

            return string.Join(", ", selectedOptions);
        }

        public IActionResult BookingSuccess(int id)
        {
            var booking = _context.Bookings
                .Include(b => b.Showtime.Movie).Include(b => b.Showtime.Hall.Cinema)
                .Include(b => b.BookingDetails).ThenInclude(bd => bd.Seat)
                .Include(b => b.BookingSnacks).ThenInclude(bs => bs.Snack)
                .FirstOrDefault(b => b.BookingId == id);

            if (booking == null) return NotFound();
            var currentId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            bool ownsBooking =
                int.TryParse(currentId, out int parsedId) &&
                booking.UserId == parsedId;

            if (!User.IsInRole("Admin") && !ownsBooking)
            {
                return Forbid();
            }
            return View(booking);
        }

        [Authorize]
        public async Task<IActionResult> MyBookings()
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdString)) return RedirectToAction("Login", "Account");
            int userId = int.Parse(userIdString);
            var bookings = await _context.Bookings
                .Include(b => b.Showtime.Movie).Include(b => b.Showtime.Hall.Cinema)
                .Include(b => b.BookingDetails).ThenInclude(bd => bd.Seat)
                .Include(b => b.BookingSnacks).ThenInclude(bs => bs.Snack)
                .Where(b => b.UserId == userId).OrderByDescending(b => b.BookingDate).ToListAsync();
            return View(bookings);
        }
    }

    public class SnackCartItem
    {
        public int SnackId { get; set; }
        public int Quantity { get; set; }
        public string Customization { get; set; }
    }

}
