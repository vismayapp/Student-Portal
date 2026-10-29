using Microsoft.EntityFrameworkCore;
using Login.Data;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

// =========================================================================
// 🔒 FIX: Hardening Session Configurations to Stop Disconnects on Uploads
// =========================================================================
builder.Services.AddDistributedMemoryCache(); // Backing store for session data
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(60); // Retain your login state
    options.Cookie.HttpOnly = true;                 // Mitigates cross-site scripting risks
    options.Cookie.IsEssential = true;              // CRITICAL: Survives app-pool refreshes when saving files
});

// Database Configuration
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Authentication Configuration
builder.Services.AddAuthentication("CookieAuth")
    .AddCookie("CookieAuth", config =>
    {
        config.Cookie.Name = "StudentLoginCookie";
        config.LoginPath = "/Home/Login";
        config.LogoutPath = "/Course/Logout";
        config.ExpireTimeSpan = TimeSpan.FromMinutes(60); // Keep auth cookie aligned with session lifespan
    });

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

// UseStaticFiles must handle newly added files smoothly
app.UseStaticFiles();

app.UseRouting();

// Session middleware must run BEFORE Authentication so users can be verified
app.UseSession();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();