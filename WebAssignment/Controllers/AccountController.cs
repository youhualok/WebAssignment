using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json;
using WebAssignment.Data;
using WebAssignment.Models;
using WebAssignment.Helpers;
using UserModel = WebAssignment.Models.User;
using System.Security.Cryptography;

namespace WebAssignment.Controllers
{
    public class AccountController : Controller
    {
        private readonly AppDbContext _context;
        private readonly EmailService _emailService;
        public AccountController(AppDbContext context, EmailService emailService)
        {
            _context = context;
            _emailService = emailService;
        }

        // ==========================================
        //  HELPER: GENERATE MATH CAPTCHA
        // ==========================================
        private void GenerateCaptcha()
        {
            Random r = new Random();
            int num1 = r.Next(1, 10);
            int num2 = r.Next(1, 10);
            HttpContext.Session.SetString("CaptchaAnswer", (num1 + num2).ToString());
            ViewBag.CaptchaQuestion = $"{num1} + {num2} = ?";
        }

        private static string GenerateOtp() => RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        private static bool HasValidImageSignature(
    byte[] bytes,
    string extension)
        {
            if (bytes == null || bytes.Length < 12)
            {
                return false;
            }

            bool isJpeg =
                bytes[0] == 0xFF &&
                bytes[1] == 0xD8 &&
                bytes[2] == 0xFF;

            bool isPng =
                bytes[0] == 0x89 &&
                bytes[1] == 0x50 &&
                bytes[2] == 0x4E &&
                bytes[3] == 0x47 &&
                bytes[4] == 0x0D &&
                bytes[5] == 0x0A &&
                bytes[6] == 0x1A &&
                bytes[7] == 0x0A;

            bool isWebp =
                bytes[0] == 0x52 &&
                bytes[1] == 0x49 &&
                bytes[2] == 0x46 &&
                bytes[3] == 0x46 &&
                bytes[8] == 0x57 &&
                bytes[9] == 0x45 &&
                bytes[10] == 0x42 &&
                bytes[11] == 0x50;

            extension = extension.ToLowerInvariant();

            return extension switch
            {
                ".jpg" => isJpeg,
                ".jpeg" => isJpeg,
                ".png" => isPng,
                ".webp" => isWebp,
                _ => false
            };
        }

        // ==========================================
        // 1. REGISTER (GET)
        // ==========================================
        [HttpGet]
        public IActionResult Register()
        {
            if (User.Identity != null && User.Identity.IsAuthenticated)
            {
                return RedirectToAction("Index", "Home");
            }
            GenerateCaptcha();
            return View();
        }

        // ==========================================
        // 2. REGISTER (POST)
        // ==========================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RegisterSubmit(RegisterViewModel model, string captchaAnswer)
        {
            // --- CAPTCHA CHECK ---
            string? correctAnswer = HttpContext.Session.GetString("CaptchaAnswer");
            if (string.IsNullOrEmpty(correctAnswer) || captchaAnswer != correctAnswer)
            {
                ModelState.AddModelError("Captcha", "Incorrect math answer. Please try again.");
                GenerateCaptcha();
                return View("Register", model);
            }

            if (!ModelState.IsValid)
            {
                GenerateCaptcha();
                return View("Register", model);
            }

            if (_context.Users.Any(u => u.Username == model.Username))
            {
                ModelState.AddModelError("Username", "Username taken.");
                GenerateCaptcha();
                return View("Register", model);
            }
            if (_context.Users.Any(u => u.Email == model.Email))
            {
                ModelState.AddModelError("Email", "Email already registered.");
                GenerateCaptcha();
                return View("Register", model);
            }

            string otp = GenerateOtp();
            string userJson = JsonSerializer.Serialize(model);
            HttpContext.Session.SetString("PendingRegUser", userJson);
            HttpContext.Session.SetString("RegisterOTP", otp);
            HttpContext.Session.SetString("RegisterExpiry", DateTime.Now.AddSeconds(120).ToString());

            await HttpContext.Session.CommitAsync();

            string subject = "Verify your Account - Waliu Cinema";
            string body = $"<h3>Your Registration Code: <b style='color:#d4af37;'>{otp}</b></h3><p>This code expires in 2 minutes.</p>";

            try
            {
                await _emailService.SendEmailAsync(model.Email, subject, body);
                await Task.Delay(500);
                return RedirectToAction("VerifyRegister");
            }
            catch (Exception ex)
            {
                ViewBag.Error = "Email System Error: " + ex.Message;
                GenerateCaptcha();
                return View("Register", model);
            }
        }

        // ==========================================
        // 3. VERIFY REGISTRATION (GET)
        // ==========================================
        [HttpGet]
        public IActionResult VerifyRegister()
        {
            if (string.IsNullOrEmpty(HttpContext.Session.GetString("PendingRegUser")))
            {
                return RedirectToAction("Register");
            }
            return View();
        }

        // ==========================================
        // 4. VERIFY REGISTRATION (POST)
        // ==========================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> VerifyRegister(string code)
        {
            string? sessionOtp = HttpContext.Session.GetString("RegisterOTP");
            string? userJson = HttpContext.Session.GetString("PendingRegUser");
            string? expiryStr = HttpContext.Session.GetString("RegisterExpiry");

            if (string.IsNullOrEmpty(sessionOtp) || string.IsNullOrEmpty(userJson) || string.IsNullOrEmpty(expiryStr))
            {
                TempData["Error"] = "Session expired. Please register again.";
                return RedirectToAction("Register");
            }

            if (DateTime.Parse(expiryStr) < DateTime.Now)
            {
                ViewBag.Error = "Code expired! Please click 'Resend Code'.";
                return View();
            }

            if (code != sessionOtp)
            {
                ViewBag.Error = "Invalid Code. Please try again.";
                return View();
            }

            RegisterViewModel? model = JsonSerializer.Deserialize<RegisterViewModel>(userJson);
            if (model == null) return RedirectToAction("Register");

            if (await _context.Users.AnyAsync(u => u.Username == model.Username || u.Email == model.Email))
            {
                TempData["Error"] = "Username or email is already registered.";
                return RedirectToAction("Register");
            }

            UserModel newUser = new UserModel
            {
                FullName = model.FullName,
                Username = model.Username,
                Email = model.Email,
                PhoneNumber = model.PhoneNumber,
                // [MODIFIED] Hash password before saving
                Password = PasswordHelper.HashPassword(model.Password),
                Role = UserRole.User,
                Points = 0,
                ProfilePhotoPath = "https://ui-avatars.com/api/?name=" + model.Username + "&background=random",
                ManagedCinemaId = null,
                ThemeColor = "Gold"
            };

            _context.Users.Add(newUser);
            await _context.SaveChangesAsync();

            try
            {
                string subject = "Welcome to Waliu Cinema! 🎬";
                string body = $@"
                    <div style='font-family: Arial, sans-serif; color: #333; max-width: 600px; margin: 0 auto; border: 1px solid #ddd; padding: 20px; border-radius: 10px;'>
                        <h2 style='color: #d4af37; text-align: center;'>Welcome, {newUser.FullName}!</h2>
                        <p>Congratulations! Your account has been successfully activated.</p>
                        <p>You can now login to experience the magic of cinema.</p>
                    </div>";

                await _emailService.SendEmailAsync(newUser.Email, subject, body);
            }
            catch (Exception) { }

            HttpContext.Session.Remove("PendingRegUser");
            HttpContext.Session.Remove("RegisterOTP");
            HttpContext.Session.Remove("RegisterExpiry");

            TempData["Success"] = "Account verified! You can now login.";
            return RedirectToAction("Login");
        }

        // ==========================================
        // 5. RESEND CODE
        // ==========================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResendRegisterCode()
        {
            string? userJson = HttpContext.Session.GetString("PendingRegUser");
            if (string.IsNullOrEmpty(userJson)) return Json(new { success = false, message = "Session expired." });

            RegisterViewModel? model = JsonSerializer.Deserialize<RegisterViewModel>(userJson);
            if (model == null) return Json(new { success = false, message = "Invalid session." });
            string newOtp = GenerateOtp();
            HttpContext.Session.SetString("RegisterOTP", newOtp);
            HttpContext.Session.SetString("RegisterExpiry", DateTime.Now.AddSeconds(120).ToString());

            try
            {
                await _emailService.SendEmailAsync(model.Email, "New Code", $"<h3>New Code: {newOtp}</h3>");
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ==========================================
        // 6. LOGIN (GET)
        // ==========================================
        [HttpGet]
        public IActionResult Login()
        {
            if (User.Identity != null && User.Identity.IsAuthenticated)
            {
                if (User.IsInRole("Admin")) return RedirectToAction("Dashboard", "Movies");
                return RedirectToAction("Index", "Home");
            }
            GenerateCaptcha();
            return View();
        }

        // ==========================================
        // 7. LOGIN (POST)
        // ==========================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(string username, string password, string captchaAnswer, bool rememberMe)
        {
            // --- CAPTCHA CHECK ---
            string? correctAnswer = HttpContext.Session.GetString("CaptchaAnswer");
            if (string.IsNullOrEmpty(correctAnswer) || captchaAnswer != correctAnswer)
            {
                ViewBag.Error = "Incorrect math answer.";
                GenerateCaptcha();
                return View();
            }

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                ViewBag.Error = "Please enter both Username and Password.";
                GenerateCaptcha();
                return View();
            }

            var dbUser = await _context.Users
                .FirstOrDefaultAsync(u => u.Username.ToLower() == username.Trim().ToLower());

            if (dbUser == null)
            {
                ViewBag.Error = "Invalid Username or Password";
                GenerateCaptcha();
                return View();
            }

            if (dbUser.LockoutEnd.HasValue && dbUser.LockoutEnd.Value > DateTime.Now)
            {
                var remainingSeconds = Math.Ceiling((dbUser.LockoutEnd.Value - DateTime.Now).TotalSeconds);
                ViewBag.Error = $"Account locked. Try again in {remainingSeconds} seconds.";
                GenerateCaptcha();
                return View();
            }

            // [MODIFIED] Use PasswordHelper to verify hash
            if (PasswordHelper.VerifyPassword(password.Trim(), dbUser.Password))
            {
                bool passwordWasLegacy = PasswordHelper.NeedsRehash(dbUser.Password);
                if (passwordWasLegacy)
                    dbUser.Password = PasswordHelper.HashPassword(password.Trim());
                // Reset failures on successful login
                if (dbUser.FailedLoginAttempts > 0 || dbUser.LockoutEnd != null || passwordWasLegacy)
                {
                    dbUser.FailedLoginAttempts = 0;
                    dbUser.LockoutEnd = null;
                    await _context.SaveChangesAsync();
                }

                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.Name, dbUser.Username),
                    new Claim(ClaimTypes.Role, dbUser.Role.ToString()),
                    new Claim(ClaimTypes.NameIdentifier, dbUser.UserId.ToString()),
                    new Claim("ProfilePic", dbUser.ProfilePhotoPath ?? "")
                };

                if (dbUser.ManagedCinemaId.HasValue)
                {
                    claims.Add(new Claim("ManagedCinemaId", dbUser.ManagedCinemaId.Value.ToString()));
                }

                var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

                var authProperties = new AuthenticationProperties
                {
                    IsPersistent = rememberMe,
                    ExpiresUtc = rememberMe ? DateTime.UtcNow.AddDays(30) : DateTime.UtcNow.AddHours(1)
                };

                Response.Cookies.Append("UserTheme", dbUser.ThemeColor ?? "Gold", new CookieOptions
                {
                    Expires = DateTime.UtcNow.AddDays(30),
                    IsEssential = true
                });

                await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(claimsIdentity), authProperties);

                if (dbUser.Role == UserRole.Admin) return RedirectToAction("Dashboard", "Movies");
                return RedirectToAction("Index", "Home");
            }
            else
            {
                dbUser.FailedLoginAttempts++;
                if (dbUser.FailedLoginAttempts >= 3)
                {
                    dbUser.LockoutEnd = DateTime.Now.AddSeconds(30);
                    dbUser.FailedLoginAttempts = 0;
                    ViewBag.Error = "Maximum attempts reached. Account locked for 30 seconds.";
                }
                else
                {
                    ViewBag.Error = $"Invalid Password. {3 - dbUser.FailedLoginAttempts} attempts remaining.";
                }
                await _context.SaveChangesAsync();
                GenerateCaptcha();
                return View();
            }
        }

        // ==========================================
        // 8. LOGOUT
        // ==========================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(
                CookieAuthenticationDefaults.AuthenticationScheme
            );

            HttpContext.Session.Clear();

            Response.Cookies.Delete("UserTheme");

            return RedirectToAction("Login", "Account");
        }

        // ==========================================
        // 9. FORGOT PASSWORD & RESET
        // ==========================================
        [HttpGet] public IActionResult ForgotPassword() { return View(); }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
        {
            if (!ModelState.IsValid) return View(model);
            var user = _context.Users.FirstOrDefault(u => u.Email == model.Email);
            if (user == null) { ModelState.AddModelError("Email", "Email not found."); return View(model); }

            string otp = GenerateOtp();
            HttpContext.Session.SetString("ResetEmail", model.Email);
            HttpContext.Session.SetString("ResetCode", otp);
            HttpContext.Session.SetString("ResetExpiry", DateTime.Now.AddSeconds(120).ToString());

            await _emailService.SendEmailAsync(model.Email, "Reset Password", $"Your Code: {otp}");
            return RedirectToAction("VerifyCode");
        }

        [HttpGet] public IActionResult VerifyCode() { return View(); }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult VerifyCode(string code)
        {
            var expiryText = HttpContext.Session.GetString("ResetExpiry");
            if (!DateTime.TryParse(expiryText, out var expiry) || expiry < DateTime.Now)
            {
                ViewBag.Error = "Code expired.";
                return View();
            }
            if (code == HttpContext.Session.GetString("ResetCode"))
            {
                HttpContext.Session.SetString("ResetVerified", "true");
                HttpContext.Session.Remove("ResetCode");
                return RedirectToAction("ResetPassword");
            }
            ViewBag.Error = "Invalid Code"; return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResendCode()
        {
            string? email = HttpContext.Session.GetString("ResetEmail");
            if (string.IsNullOrEmpty(email)) return Json(new { success = false });
            string newOtp = GenerateOtp();
            HttpContext.Session.SetString("ResetCode", newOtp);
            HttpContext.Session.SetString("ResetExpiry", DateTime.Now.AddMinutes(2).ToString("O"));
            HttpContext.Session.Remove("ResetVerified");
            await _emailService.SendEmailAsync(email, "New Code", $"New Code: {newOtp}");
            return Json(new { success = true });
        }

        [HttpGet] public IActionResult ResetPassword()
        {
            return HttpContext.Session.GetString("ResetVerified") == "true"
                ? View()
                : RedirectToAction("ForgotPassword");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            string? email = HttpContext.Session.GetString("ResetEmail");
            if (string.IsNullOrEmpty(email) || HttpContext.Session.GetString("ResetVerified") != "true")
            {
                TempData["Error"] = "Session expired. Please try again.";
                return RedirectToAction("ForgotPassword");
            }

            var user = _context.Users.FirstOrDefault(u => u.Email == email);

            if (user != null)
            {
                // [MODIFIED] Verify against hash instead of plain text
                if (PasswordHelper.VerifyPassword(model.NewPassword, user.Password))
                {
                    ModelState.AddModelError("NewPassword", "You cannot use your previous password.");
                    return View(model);
                }

                // [MODIFIED] Hash the new password
                user.Password = PasswordHelper.HashPassword(model.NewPassword);
                await _context.SaveChangesAsync();

                HttpContext.Session.Clear();
                TempData["Success"] = "Password reset successfully! You can now login.";
                return RedirectToAction("Login");
            }
            return RedirectToAction("Login");
        }

        // ==========================================
        // 10. PROFILE & EDIT
        // ==========================================
        [HttpGet]
        [Microsoft.AspNetCore.Authorization.Authorize]
        public async Task<IActionResult> Profile()
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdString)) return RedirectToAction("Login");
            var user = await _context.Users.FindAsync(int.Parse(userIdString));
            return user == null ? NotFound() : View(user);
        }

        [HttpGet]
        [Microsoft.AspNetCore.Authorization.Authorize]
        public async Task<IActionResult> EditProfile()
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdString)) return RedirectToAction("Login");

            var user = await _context.Users.FindAsync(int.Parse(userIdString));
            if (user == null) return NotFound();

            var viewModel = new EditProfileViewModel
            {
                UserId = user.UserId,
                Username = user.Username,
                FullName = user.FullName,
                Email = user.Email,
                PhoneNumber = user.PhoneNumber,
                ProfilePhotoPath = user.ProfilePhotoPath
            };

            return View(viewModel);
        }

        [HttpPost]
        [Microsoft.AspNetCore.Authorization.Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditProfile(EditProfileViewModel model, IFormFile? profileImage, string? base64Image)
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdString)) return RedirectToAction("Login");

            var userInDb = await _context.Users.FindAsync(int.Parse(userIdString));
            if (userInDb == null) return NotFound();

            if (_context.Users.Any(u => u.Email == model.Email && u.UserId != userInDb.UserId))
            {
                ViewBag.Error = "Email is already in use.";
                return View(model);
            }

            if (ModelState.IsValid)
            {
                userInDb.FullName = model.FullName;
                userInDb.Email = model.Email;
                userInDb.PhoneNumber = model.PhoneNumber;

                var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads");
                if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);

                if (profileImage != null && profileImage.Length > 0)
                {
                    var permittedExtensions = new[]
                    {
                        ".jpg",
                        ".jpeg",
                        ".png",
                        ".webp"
                    };

                    const long maximumFileSize =
                        5 * 1024 * 1024;

                    var extension = Path
                        .GetExtension(profileImage.FileName)
                        .ToLowerInvariant();

                    if (profileImage.Length > maximumFileSize)
                    {
                        ModelState.AddModelError(
                            "",
                            "Profile image cannot exceed 5 MB."
                        );

                        return View(model);
                    }

                    if (!permittedExtensions.Contains(extension))
                    {
                        ModelState.AddModelError(
                            "",
                            "Only JPG, JPEG, PNG and WEBP images are allowed."
                        );

                        return View(model);
                    }

                    byte[] fileHeader = new byte[12];

                    await using (
                        var validationStream =
                            profileImage.OpenReadStream())
                    {
                        int bytesRead =
                            await validationStream.ReadAsync(
                                fileHeader,
                                0,
                                fileHeader.Length
                            );

                        if (bytesRead < fileHeader.Length)
                        {
                            ModelState.AddModelError(
                                "",
                                "The uploaded image is invalid."
                            );

                            return View(model);
                        }
                    }

                    if (!HasValidImageSignature(
                        fileHeader,
                        extension))
                    {
                        ModelState.AddModelError(
                            "",
                            "The file content does not match its image extension."
                        );

                        return View(model);
                    }

                    var fileName =
                        Guid.NewGuid().ToString() +
                        extension;

                    var fullFilePath = Path.Combine(
                        uploadsFolder,
                        fileName
                    );

                    await using (
                        var outputStream =
                            new FileStream(
                                fullFilePath,
                                FileMode.CreateNew))
                    {
                        await profileImage.CopyToAsync(
                            outputStream
                        );
                    }
                    userInDb.ProfilePhotoPath =
                        "/uploads/" + fileName;
                }
                else if (!string.IsNullOrWhiteSpace(base64Image) &&
                         base64Image.Contains("base64,", StringComparison.OrdinalIgnoreCase))
                {
                    const long maximumFileSize = 5 * 1024 * 1024;
                    int dataIndex = base64Image.IndexOf("base64,", StringComparison.OrdinalIgnoreCase) + 7;
                    byte[] imageBytes;

                    try
                    {
                        imageBytes = Convert.FromBase64String(base64Image[dataIndex..]);
                    }
                    catch (FormatException)
                    {
                        ModelState.AddModelError("", "The webcam image data is invalid.");
                        return View(model);
                    }

                    if (imageBytes.Length == 0 || imageBytes.Length > maximumFileSize)
                    {
                        ModelState.AddModelError("", "The webcam image must be valid and no larger than 5 MB.");
                        return View(model);
                    }

                    if (!HasValidImageSignature(imageBytes, ".jpg"))
                    {
                        ModelState.AddModelError("", "The webcam upload is not a valid JPEG image.");
                        return View(model);
                    }

                    var fileName = "webcam_" + Guid.NewGuid() + ".jpg";
                    await System.IO.File.WriteAllBytesAsync(Path.Combine(uploadsFolder, fileName), imageBytes);
                    userInDb.ProfilePhotoPath = "/uploads/" + fileName;
                }

                _context.Users.Update(userInDb);
                await _context.SaveChangesAsync();

                var claims = new List<Claim> {
                    new Claim(ClaimTypes.Name, userInDb.Username),
                    new Claim(ClaimTypes.Role, userInDb.Role.ToString()),
                    new Claim(ClaimTypes.NameIdentifier, userInDb.UserId.ToString()),
                    new Claim("ProfilePic", userInDb.ProfilePhotoPath ?? "")
                };
                if (userInDb.ManagedCinemaId.HasValue)
                    claims.Add(new Claim("ManagedCinemaId", userInDb.ManagedCinemaId.Value.ToString()));
                var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

                return RedirectToAction("Profile");
            }
            return View(model);
        }

        // ==========================================
        // 12. REWARDS (GET)
        // ==========================================
        [HttpGet]
        [Microsoft.AspNetCore.Authorization.Authorize]
        public async Task<IActionResult> Redeem()
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdString)) return RedirectToAction("Login");
            var user = await _context.Users.Include(u => u.UserRewards).FirstOrDefaultAsync(u => u.UserId == int.Parse(userIdString));
            return user == null ? NotFound() : View(user);
        }

        // ==========================================
        // 13. CLAIM REWARD (POST)
        // ==========================================
        [HttpPost]
        [Microsoft.AspNetCore.Authorization.Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ClaimReward(string type)
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdString)) return RedirectToAction("Login");
            var user = await _context.Users.FindAsync(int.Parse(userIdString));

            if (user == null) return NotFound();
            var rewardDefinition = type switch
            {
                "SNACK5" => (Cost: 500, Prefix: "SNACK-", Name: "RM 5 Off Snacks"),
                "TICKET10" => (Cost: 1000, Prefix: "BUNDLE-", Name: "RM 10 Off Bundle"),
                "FREETICKET" => (Cost: 2500, Prefix: "FREE-", Name: "Free Ticket"),
                _ => (Cost: 0, Prefix: "", Name: "")
            };
            if (rewardDefinition.Cost == 0) return BadRequest("Invalid reward type.");
            int cost = rewardDefinition.Cost;
            if (user.Points < cost) { TempData["Error"] = "Insufficient points!"; return RedirectToAction("Redeem"); }

            string newCode = rewardDefinition.Prefix + Convert.ToHexString(RandomNumberGenerator.GetBytes(6));
            string rewardName = rewardDefinition.Name;

            user.Points -= cost;

            _context.UserRewards.Add(new UserReward
            {
                UserId = user.UserId,
                RewardName = rewardName,
                PointsCost = cost,
                PromoCode = newCode,
                RedeemedDate = DateTime.Now
            });

            await _context.SaveChangesAsync();

            TempData["Success"] = $"Redeemed {rewardName}! Code: {newCode}";
            return RedirectToAction("Redeem");
        }

        // ==========================================
        // 14. CHANGE THEME & SETTINGS
        // ==========================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangeTheme(string selectedColor)
        {
            var allowedThemes = new[] { "Gold", "Blue", "Red", "Pink", "Yellow" };
            if (!allowedThemes.Contains(selectedColor)) return BadRequest("Invalid theme.");
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrEmpty(userIdString))
            {
                int userId = int.Parse(userIdString);
                var user = await _context.Users.FindAsync(userId);
                if (user != null)
                {
                    user.ThemeColor = selectedColor;
                    await _context.SaveChangesAsync();
                }
            }

            Response.Cookies.Append("UserTheme", selectedColor, new CookieOptions
            {
                Expires = DateTime.UtcNow.AddDays(30),
                IsEssential = true
            });

            return RedirectToAction("Settings");
        }

        [HttpGet]
        public IActionResult Settings()
        {
            ViewBag.CurrentTheme = Request.Cookies["UserTheme"] ?? "Gold";
            return View();
        }
    }
}
