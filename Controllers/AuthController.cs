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

            var user = _mongoService.AuthenticateUser(username, password);

            if (user != null)
            {
                _logger.LogInformation("User authenticated: {Username} ({Role})", user.Username, user.Role);

                // Set session for username and role
                HttpContext.Session.SetString("Username", user.Username);
                HttpContext.Session.SetString("Role", user.Role);

                // Redirect based on role
                if (user.Role.ToLower() == "admin")
                    return RedirectToAction("Dashboard", "Admin");
                else
                    return RedirectToAction("Dashboard", "User");
            }

            _logger.LogWarning("Login failed for username: {Username}", username);

            ViewBag.ErrorMessage = "Invalid username or password";
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
