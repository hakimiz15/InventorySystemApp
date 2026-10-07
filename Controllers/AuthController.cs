using InventorySystemApp.Data;
using InventorySystemApp.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace InventorySystemApp.Controllers
{
    public class AuthController : Controller
    {
        private readonly MongoDBService _mongoService;
        private readonly ILogger<AuthController> _logger;

        public AuthController(MongoDBService mongoService, ILogger<AuthController> logger)
        {
            _mongoService = mongoService;
            _logger = logger;
        }

        // GET: Login Page
        public IActionResult Login()
        {
            // Clear previous session on GET login
            HttpContext.Session.Clear();
            return View();
        }

        // POST: Login
        [HttpPost]
        public IActionResult Login(string username, string password)
        {
            _logger.LogInformation("Login attempt for username: {Username}", username);

            var result = _mongoService.AttemptLogin(username, password);

            // --- Pass lockout / warning info down to the view (Login.cshtml uses these) ---
            ViewBag.IsLockedOut      = result.IsLockedOut;
            ViewBag.LockoutUntilUtc  = result.LockoutUntilUtc;
            ViewBag.FailedAttempts   = result.FailedAttempts;
            ViewBag.MaxAttempts      = MongoDBService.MAX_FAILED_ATTEMPTS_BEFORE_LOCKOUT;
            ViewBag.LockoutMinutes   = (int)MongoDBService.LOCKOUT_DURATION.TotalMinutes;
            ViewBag.LastUsername     = username;

            if (result.IsSuccess && result.User != null)
            {
                _logger.LogInformation("User authenticated: {Username} ({Role})", result.User.Username, result.User.Role);

                // Set session for username and role
                HttpContext.Session.SetString("Username", result.User.Username);
                HttpContext.Session.SetString("Role", result.User.Role);

                // Redirect based on role
                if (string.Equals(result.User.Role, "admin", StringComparison.OrdinalIgnoreCase))
                    return RedirectToAction("Dashboard", "Admin");
                else
                    return RedirectToAction("Dashboard", "User");
            }

            // --- Failed or locked ---
            if (result.IsLockedOut)
            {
                _logger.LogWarning("Login denied (locked out) for username: {Username}", username);
                ViewBag.ErrorMessage = result.Message;
            }
            else
            {
                _logger.LogWarning("Login failed for username: {Username}", username);
                ViewBag.ErrorMessage = "Invalid username or password.";
            }

            return View();
        }

        // GET: Logout
        public IActionResult Logout()
        {
            _logger.LogInformation("User logged out: {Username}", HttpContext.Session.GetString("Username"));

            HttpContext.Session.Clear();
            return RedirectToAction("Login");
        }
    }
}
