using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using System.Collections.Generic;
using WebAssignment.Models;
using WebAssignment.Helpers; // [MODIFIED] Added namespace for helper

namespace WebAssignment.Data
{
    public static class SeedData
    {
        public static void Initialize(IServiceProvider serviceProvider)
        {
            using (var context = new AppDbContext(
                serviceProvider.GetRequiredService<DbContextOptions<AppDbContext>>()))
            {
                // =============================================================
                // 1. ENSURE CINEMAS EXIST
                // =============================================================
                var cinemas = new List<Cinema>
                {
                    new Cinema { Name = "Waliu Cinema (KLCC)", Location = "Kuala Lumpur", Address = "Suria KLCC" },
                    new Cinema { Name = "Waliu Cinema (Mid Valley)", Location = "Kuala Lumpur", Address = "Mid Valley Megamall" },
                    new Cinema { Name = "Waliu Cinema (One Utama)", Location = "Petaling Jaya", Address = "1 Utama Shopping Centre" }
                };

                foreach (var c in cinemas)
                {
                    if (!context.Cinemas.Any(dbC => dbC.Name == c.Name))
                    {
                        context.Cinemas.Add(c);
                    }
                }
                context.SaveChanges();

                // =============================================================
                // 2. ENSURE HALLS EXIST (Fixed: Added Seat Counts)
                // =============================================================
                var dbCinemas = context.Cinemas.Include(c => c.Halls).ToList();

                foreach (var cinema in dbCinemas)
                {
                    if (cinema.Halls == null || !cinema.Halls.Any())
                    {
                        var newHalls = new List<Hall>
                        {
                            new Hall { Name = "Hall 1 (Standard 2D)", CinemaId = cinema.CinemaId, MaxSeats = 60, TotalSeats = 60 },
                            new Hall { Name = "Hall 2 (IMAX 3D Laser)", CinemaId = cinema.CinemaId, MaxSeats = 50, TotalSeats = 50 },
                            new Hall { Name = "Hall 3 (Beanie Comfort)", CinemaId = cinema.CinemaId, MaxSeats = 40, TotalSeats = 40 },
                            new Hall { Name = "Hall 4 (Platinum Indulge)", CinemaId = cinema.CinemaId, MaxSeats = 30, TotalSeats = 30 }
                        };
                        context.Halls.AddRange(newHalls);
                    }
                }
                context.SaveChanges();

                // =============================================================
                // 3. SEED USERS & ADMINS
                // =============================================================
                if (!context.Users.Any())
                {
                    var klcc = context.Cinemas.FirstOrDefault(c => c.Name.Contains("KLCC"));
                    var midvalley = context.Cinemas.FirstOrDefault(c => c.Name.Contains("Mid Valley"));
                    var oneutama = context.Cinemas.FirstOrDefault(c => c.Name.Contains("One Utama"));

                    var users = new List<User>
                    {
                        // [MODIFIED] Passwords hashed here
                        new User { Username = "admin", Password = PasswordHelper.HashPassword("DemoAdmin!2026"), Email = "admin@waliu.com", FullName = "Super Admin", Role = UserRole.Admin, ManagedCinemaId = null, ProfilePhotoPath = "https://ui-avatars.com/api/?name=Super+Admin&background=000&color=fff" },
                        new User { Username = "user", Password = PasswordHelper.HashPassword("DemoUser!2026"), Email = "user@gmail.com", FullName = "John Doe", Role = UserRole.User, ManagedCinemaId = null, ProfilePhotoPath = "https://ui-avatars.com/api/?name=User&background=random" }
                    };

                    if (!context.Users.Any(u => u.Username == "guest"))
                    {
                        context.Users.Add(new User
                        {
                            Username = "guest",
                            // [MODIFIED] Password hashed here
                            Password = PasswordHelper.HashPassword("GuestPassword123!"),
                            Email = "guest@waliu.com",
                            FullName = "Guest Customer",
                            Role = UserRole.User,
                            ManagedCinemaId = null,
                            ProfilePhotoPath = "https://ui-avatars.com/api/?name=Guest&background=random"
                        });
                        context.SaveChanges();
                    }

                    // [MODIFIED] Passwords hashed here for branch managers
                    if (klcc != null) users.Add(new User { Username = "admin_klcc", Password = PasswordHelper.HashPassword("BranchAdmin!2026"), Email = "klcc@waliu.com", FullName = "Manager KLCC", Role = UserRole.Admin, ManagedCinemaId = klcc.CinemaId, ProfilePhotoPath = "https://ui-avatars.com/api/?name=KLCC&background=d4af37&color=000" });
                    if (midvalley != null) users.Add(new User { Username = "admin_mv", Password = PasswordHelper.HashPassword("BranchAdmin!2026"), Email = "mv@waliu.com", FullName = "Manager MidValley", Role = UserRole.Admin, ManagedCinemaId = midvalley.CinemaId, ProfilePhotoPath = "https://ui-avatars.com/api/?name=MV&background=d4af37&color=000" });
                    if (oneutama != null) users.Add(new User { Username = "admin_ou", Password = PasswordHelper.HashPassword("BranchAdmin!2026"), Email = "ou@waliu.com", FullName = "Manager OneUtama", Role = UserRole.Admin, ManagedCinemaId = oneutama.CinemaId, ProfilePhotoPath = "https://ui-avatars.com/api/?name=OU&background=d4af37&color=000" });

                    context.Users.AddRange(users);
                    context.SaveChanges();
                }

                // =============================================================
                // 4. SEED SEATS
                // =============================================================
                var allHalls = context.Halls.Include(h => h.Seats).ToList();
                var newSeats = new List<Seat>();
                string[] rows = { "A", "B", "C", "D", "E" };

                foreach (var hall in allHalls)
                {
                    if (hall.Seats == null || !hall.Seats.Any())
                    {
                        foreach (var row in rows)
                        {
                            var seatsPerRow = (int)Math.Ceiling(hall.MaxSeats / (double)rows.Length);
                            for (int i = 1; i <= seatsPerRow; i++)
                            {
                                if (newSeats.Count(s => s.HallId == hall.HallId) >= hall.MaxSeats) break;
                                newSeats.Add(new Seat { Row = row, Number = i, HallId = hall.HallId, Type = SeatType.Standard });
                            }
                        }
                    }
                }
                if (newSeats.Any())
                {
                    context.Seats.AddRange(newSeats);
                    context.SaveChanges();
                }

                // 5. SEED MOVIES
                if (!context.Movies.Any())
                {
                    context.Movies.AddRange(
                        new Movie { Title = "Dune: Part Two", Genre = "Sci-Fi", DurationMinutes = 166, ReleaseDate = DateTime.Parse("2024-03-01"), Description = "Paul Atreides unites with Chani and the Fremen while on a warpath of revenge.", ImageUrl = "https://image.tmdb.org/t/p/original/1pdfLvkbY9ohJlCjQH2CZjjYVvJ.jpg", TrailerUrl = "https://www.youtube.com/watch?v=Way9Dexny3w", Status = MovieStatus.NowShowing, Price = 25.00m },
                        new Movie { Title = "Oppenheimer", Genre = "Biography", DurationMinutes = 180, ReleaseDate = DateTime.Parse("2023-07-21"), Description = "The story of American scientist J. Robert Oppenheimer.", ImageUrl = "https://image.tmdb.org/t/p/original/8Gxv8gSFCU0XGDykEGv7zR1n2ua.jpg", TrailerUrl = "https://www.youtube.com/watch?v=uYPbbksJxIg", Status = MovieStatus.NowShowing, Price = 22.00m },
                        new Movie { Title = "John Wick: Chapter 4", Genre = "Action", DurationMinutes = 169, ReleaseDate = DateTime.Parse("2023-03-24"), Description = "John Wick uncovers a path to defeating The High Table.", ImageUrl = "https://image.tmdb.org/t/p/original/vZloFAK7NmvMGKE7VkF5UHaz0I.jpg", TrailerUrl = "https://www.youtube.com/watch?v=qEVUtrk8_B4", Status = MovieStatus.NowShowing, Price = 24.00m },
                        new Movie { Title = "Godzilla x Kong: The New Empire", Genre = "Action/Sci-Fi", DurationMinutes = 115, ReleaseDate = DateTime.Parse("2024-03-29"), Description = "Two ancient titans, Godzilla and Kong, clash in an epic battle.", ImageUrl = "https://i.pinimg.com/originals/f7/9d/e4/f79de4eb88c11ec0f0055fef13ff73b3.jpg", TrailerUrl = "https://www.youtube.com/watch?v=lV1OOlGwExM", Status = MovieStatus.NowShowing, Price = 26.00m },
                        new Movie { Title = "Kung Fu Panda 4", Genre = "Animation", DurationMinutes = 94, ReleaseDate = DateTime.Parse("2024-03-08"), Description = "Po is gearing up to become the spiritual leader of his Valley of Peace.", ImageUrl = "https://image.tmdb.org/t/p/original/kDp1vUBnMpe8ak4rjgl3cLELqjU.jpg", TrailerUrl = "https://www.youtube.com/watch?v=_inKs4eeHiI", Status = MovieStatus.NowShowing, Price = 20.00m },
                        new Movie { Title = "Civil War", Genre = "Thriller", DurationMinutes = 109, ReleaseDate = DateTime.Parse("2024-04-12"), Description = "A journey across a dystopian future America.", ImageUrl = "https://image.tmdb.org/t/p/original/sh7Rg8Er3tFcN9BpKIPOMvALgZd.jpg", TrailerUrl = "https://youtu.be/aDyQxtg0V2w?si=HJdgjyW5s27uhSWo", Status = MovieStatus.NowShowing, Price = 23.00m },
                        new Movie { Title = "Inside Out 2", Genre = "Animation", DurationMinutes = 100, ReleaseDate = DateTime.Parse("2024-06-14"), Description = "Riley is making new memories and navigating new emotions.", ImageUrl = "https://image.tmdb.org/t/p/original/vpnVM9B6NMmQpWeZvzLvDESb2QY.jpg", TrailerUrl = "https://www.youtube.com/watch?v=LEjhY15eCx0", Status = MovieStatus.NowShowing, Price = 21.00m },
                        new Movie { Title = "Despicable Me 4", Genre = "Animation", DurationMinutes = 95, ReleaseDate = DateTime.Parse("2024-07-03"), Description = "Gru faces a new nemesis in Maxime Le Mal.", ImageUrl = "https://image.tmdb.org/t/p/original/dbtxg1bQYYuWMQvtEuoiUe4uvFJ.jpg", TrailerUrl = "https://www.youtube.com/watch?v=qQlr9-rF32A", Status = MovieStatus.NowShowing, Price = 21.00m },
                        new Movie { Title = "The Fall Guy", Genre = "Action/Comedy", DurationMinutes = 126, ReleaseDate = DateTime.Parse("2026-01-15"), Description = "A battered and past-his-prime stuntman works on a movie set.", ImageUrl = "https://image.tmdb.org/t/p/w1280/jDX7mgz1AJHLPMm6UDZmTeK0hZx.jpg", TrailerUrl = "https://www.youtube.com/watch?v=j7jPnwVGdZ8", Status = MovieStatus.ComingSoon, Price = 21.00m },
                        new Movie { Title = "Avatar: The Way of Water", Genre = "Sci-Fi", DurationMinutes = 192, ReleaseDate = DateTime.Parse("2026-02-10"), Description = "Jake Sully lives with his newfound family...", ImageUrl = "https://image.tmdb.org/t/p/original/t6HIqrRAclMCA60NsSmeqe9RmNV.jpg", TrailerUrl = "https://youtu.be/d9MyW72ELq0?si=yy8GM41YDRJhEGw-", Status = MovieStatus.ComingSoon, Price = 28.00m },
                        new Movie { Title = "Top Gun: Maverick", Genre = "Action", DurationMinutes = 130, ReleaseDate = DateTime.Parse("2026-03-05"), Description = "Maverick is still pushing the envelope...", ImageUrl = "https://image.tmdb.org/t/p/original/62HCnUTziyWcpDaBO2i1DX17ljH.jpg", TrailerUrl = "https://youtu.be/giXco2jaZ_4?si=YdovT31jGBDYOaJI", Status = MovieStatus.ComingSoon, Price = 20.00m },
                        new Movie { Title = "Captain America: Brave New World", Genre = "Action", DurationMinutes = 145, ReleaseDate = DateTime.Parse("2026-04-20"), Description = "Sam Wilson finds himself in the middle of an international incident.", ImageUrl = "https://image.tmdb.org/t/p/original/pA3vdhadJPxF5GA1uo8OPTiNQDT.jpg", TrailerUrl = "https://www.youtube.com/watch?v=1pHDWnXmK7Y", Status = MovieStatus.ComingSoon, Price = 20.00m }
                    );
                    context.SaveChanges();
                }

                // =============================================================
                // 6. SEED SHOWTIMES (Force Schedule per Movie)
                // =============================================================
                if (!context.Showtimes.Any())
                {
                    var movies = context.Movies.Where(m => m.Status == MovieStatus.NowShowing).ToList();
                    var allCinemas = context.Cinemas.Include(c => c.Halls).ToList();
                    var showtimes = new List<Showtime>();
                    var random = new Random();
                    DateTime today = DateTime.Today;

                    if (movies.Any() && allCinemas.Any())
                    {
                        for (int i = 0; i < 7; i++)
                        {
                            DateTime date = today.AddDays(i);
                            bool isWeekend = (date.DayOfWeek == DayOfWeek.Friday || date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday);
                            int[] slots = isWeekend ? new[] { 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24 } : new[] { 11, 13, 15, 17, 19, 21, 23 };

                            foreach (var cinema in allCinemas)
                            {
                                var halls = cinema.Halls.ToList();
                                if (!halls.Any()) continue;

                                foreach (var movie in movies)
                                {
                                    int showsPerDay = isWeekend ? 8 : 5;
                                    var selectedSlots = slots.OrderBy(x => random.Next()).Take(showsPerDay).OrderBy(x => x).ToList();

                                    foreach (var hour in selectedSlots)
                                    {
                                        var proposedStart = date.Date.AddHours(hour);
                                        var proposedEnd = proposedStart.AddMinutes(movie.DurationMinutes + 20);
                                        var hall = halls.FirstOrDefault(candidate => !showtimes.Any(existing =>
                                            existing.HallId == candidate.HallId &&
                                            proposedStart < existing.StartTime.AddMinutes(
                                                movies.First(m => m.MovieId == existing.MovieId).DurationMinutes + 20) &&
                                            proposedEnd > existing.StartTime));
                                        if (hall == null || proposedStart <= DateTime.Now) continue;
                                        decimal hallSurcharge = 0;
                                        if (hall.Name.Contains("IMAX")) hallSurcharge = 12.00m;
                                        else if (hall.Name.Contains("Beanie")) hallSurcharge = 20.00m;
                                        else if (hall.Name.Contains("Indulge")) hallSurcharge = 45.00m;
                                        if (isWeekend) hallSurcharge += 4.00m;

                                        showtimes.Add(new Showtime
                                        {
                                            MovieId = movie.MovieId,
                                            HallId = hall.HallId,
                                            ShowtimeDate = date.Date,
                                            StartTime = proposedStart,
                                            TicketPrice = movie.Price + hallSurcharge,
                                            MaxSeats = hall.MaxSeats,
                                            AvailableSeats = hall.MaxSeats
                                        });
                                    }
                                }
                            }
                        }
                        context.Showtimes.AddRange(showtimes);
                        context.SaveChanges();
                    }
                }

                // 7. SEED SNACKS & PROMO
                if (!context.Snacks.Any())
                {
                    context.Snacks.AddRange(
                        new Snack { Name = "Popcorn", Price = 18.00m, ImageUrl = "https://adashofcinema.wordpress.com/wp-content/uploads/2013/01/a.jpg" },
                        new Snack { Name = "Soft Drink", Price = 8.00m, ImageUrl = "https://images.unsplash.com/photo-1622483767028-3f66f32aef97?q=80&w=300&auto=format&fit=crop" },
                        new Snack { Name = "Cheesy Nachos", Price = 15.00m, ImageUrl = "https://encrypted-tbn0.gstatic.com/images?q=tbn:ANd9GcS5H5nr30pf9AYA7y_MZ1j8vweAi5gdSQkfaA&s" },
                        new Snack { Name = "Solo Power Pack", Price = 24.00m, ImageUrl = "https://encrypted-tbn0.gstatic.com/images?q=tbn:ANd9GcQliqHjJ-K5MxzfcnLngNuNtso2oWUgINgHJQ&s" },
                        new Snack { Name = "Sweet Couple Combo", Price = 32.00m, ImageUrl = "https://c8.alamy.com/comp/2S2G8YR/tasty-popcorn-drinks-and-movie-tickets-isolated-on-white-2S2G8YR.jpg" },
                        new Snack { Name = "Family Feast Box", Price = 55.00m, ImageUrl = "https://m.media-amazon.com/images/I/81BrhD7JlOL._UF1000,1000_QL80_.jpg" },
                        new Snack { Name = "Waliu Royale Set", Price = 38.00m, ImageUrl = "https://preview.free3d.com/img/2014/11/2206025899910891304/6dubyxik.jpg" }
                    );
                    context.SaveChanges();
                }

                if (!context.PromoCodes.Any())
                {
                    context.PromoCodes.AddRange(
                        new PromoCode { Code = "YUMMY5", DiscountAmount = 0.00m, IsActive = true, Description = "5% Off Snacks Only" },
                        new PromoCode { Code = "WALIU10", DiscountAmount = 0.00m, IsActive = true, Description = "10% Off Total Bill (Stackable)" }
                    );
                    context.SaveChanges();
                }

                // =============================================================
                // 8. CREATE GUEST USER (Double-check)
                // =============================================================
                if (!context.Users.Any(u => u.Username == "guest"))
                {
                    context.Users.Add(new User
                    {
                        Username = "guest",
                        // [MODIFIED] Password hashed here
                        Password = PasswordHelper.HashPassword("GuestPassword123!"),
                        Email = "guest@waliu.com",
                        FullName = "Guest Customer",
                        Role = UserRole.User,
                        ManagedCinemaId = null,
                        ProfilePhotoPath = "https://ui-avatars.com/api/?name=Guest&background=random"
                    });
                    context.SaveChanges();
                }
            }
        }
    }
}
