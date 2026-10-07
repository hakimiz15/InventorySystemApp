using InventorySystem.Data;
using InventorySystemApp.Data;
using InventorySystemApp.Models;
using Microsoft.AspNetCore.Identity;

var builder = WebApplication.CreateBuilder(args);

// Add MongoDB services
builder.Services.AddScoped<MongoDBService>();
builder.Services.AddScoped<MongoDBContext>();

// Add MVC services
builder.Services.AddControllersWithViews();

// Password hashing (production-grade: salted PBKDF2 via ASP.NET Core Identity PasswordHasher)
builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();

// Add session and distributed memory cache
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// Add IHttpContextAccessor so Razor views can access session safely
builder.Services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();

var app = builder.Build();

// Error handling & HTTPS
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

// Enable session middleware
app.UseSession();
app.UseAuthorization();

// Set default route
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Auth}/{action=Login}/{id?}");

app.Run();
