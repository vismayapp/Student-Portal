using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Login.Data;
using Login.Models;

namespace Login.Controllers
{
    [Route("Admin")]
    public class AdminController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly AppDbContext _context;

        public AdminController(IConfiguration configuration, AppDbContext context)
        {
            _configuration = configuration;
            _context = context;
        }

        [HttpGet("")]
        public IActionResult Admin([FromQuery] int logPage = 1, [FromQuery] string searchTerm = "", [FromQuery] string dateFilter = "")
        {
            if (HttpContext.Session.GetString("IsAdminLoggedIn") == "true")
            {
                if (logPage < 1) logPage = 1;

                searchTerm = searchTerm?.Trim() ?? "";
                dateFilter = dateFilter?.Trim() ?? "";

                var studentQuery = _context.Students.AsQueryable();

                if (!string.IsNullOrEmpty(searchTerm))
                {
                    studentQuery = studentQuery.Where(s =>
                        s.FullName.Contains(searchTerm) ||
                        s.Email.Contains(searchTerm) ||
                        s.Course.Contains(searchTerm));
                }

                if (!string.IsNullOrEmpty(dateFilter) && DateTime.TryParse(dateFilter, out DateTime parsedDate))
                {
                    studentQuery = studentQuery.Where(s => s.RegistrationDate.Date == parsedDate.Date);
                }

                // 4. Separate Pending Students straight at the Database Layer
                var pendingList = studentQuery
                    .Where(s => string.IsNullOrEmpty(s.Status) || s.Status.Trim().ToLower() == "pending")
                    .Select(s => new StudentViewModel
                    {
                        Id = s.Id,
                        Name = s.FullName,
                        Email = s.Email,
                        Course = s.Course,
                        Status = s.Status,
                        RegistrationDate = s.RegistrationDate.ToString("yyyy-MM-dd"),
                        DocumentPath = s.DocumentPath,
                        Remarks = s.Remarks
                    }).ToList();

                // 5. Isolate Processed Students query pointer
                var processedQuery = studentQuery
                    .Where(s => !string.IsNullOrEmpty(s.Status) &&
                                (s.Status.Trim().ToLower() == "approved" || s.Status.Trim().ToLower() == "rejected"));

                int pageSize = 5;
                int totalProcessedCount = processedQuery.Count();
                int totalPages = (int)Math.Ceiling((double)totalProcessedCount / pageSize);

                if (totalPages < 1) totalPages = 1;
                if (logPage > totalPages) logPage = totalPages;

                // 7. Paginate data efficiently ordering by latest registration
                var paginatedProcessedList = processedQuery
                    .OrderByDescending(s => s.RegistrationDate)
                    .Skip((logPage - 1) * pageSize)
                    .Take(pageSize)
                    .Select(s => new StudentViewModel
                    {
                        Id = s.Id,
                        Name = s.FullName,
                        Email = s.Email,
                        Course = s.Course,
                        Status = s.Status,
                        RegistrationDate = s.RegistrationDate.ToString("yyyy-MM-dd"),
                        DocumentPath = s.DocumentPath,
                        Remarks = s.Remarks
                    }).ToList();

                var dashboardModel = new AdminDashboardViewModel
                {
                    PendingStudents = pendingList,
                    ProcessedStudentsLog = paginatedProcessedList
                };

                ViewBag.IsLoggedIn = true;
                ViewBag.CurrentPage = logPage;
                ViewBag.TotalPages = totalPages;
                ViewBag.SearchTerm = searchTerm;
                ViewBag.DateFilter = dateFilter;

                return View("~/Views/Home/Admin.cshtml", dashboardModel);
            }

            ViewBag.IsLoggedIn = false;
            return View("~/Views/Home/Admin.cshtml", new AdminDashboardViewModel());
        }

        [HttpPost("Login")]
        public IActionResult Login(string username, string password)
        {
            string configUsername = _configuration["AdminSettings:Username"];
            string configPassword = _configuration["AdminSettings:Password"];

            if (username == configUsername && password == configPassword)
            {
                HttpContext.Session.SetString("IsAdminLoggedIn", "true");
                return RedirectToAction("Admin");
            }

            TempData["ErrorMessage"] = "Invalid admin username or password.";
            return RedirectToAction("Admin");
        }

        [HttpGet("Logout")]
        public IActionResult Logout()
        {
            HttpContext.Session.Remove("IsAdminLoggedIn");
            return RedirectToAction("Admin");
        }

        [HttpPost("ApproveStudent")]
        public IActionResult ApproveStudent([FromForm] int id, [FromForm] string remarks)
        {
            if (HttpContext.Session.GetString("IsAdminLoggedIn") != "true") return RedirectToAction("Admin");

            var student = _context.Students.FirstOrDefault(s => s.Id == id);
            if (student != null)
            {
                student.Status = "Approved";
                student.Remarks = remarks;
                _context.SaveChanges();
                TempData["SuccessMessage"] = $"{student.FullName} has been approved successfully.";
            }
            return RedirectToAction("Admin");
        }

        [HttpPost("RejectStudent")]
        public IActionResult RejectStudent([FromForm] int id, [FromForm] string remarks)
        {
            if (HttpContext.Session.GetString("IsAdminLoggedIn") != "true") return RedirectToAction("Admin");

            var student = _context.Students.FirstOrDefault(s => s.Id == id);
            if (student != null)
            {
                student.Status = "Rejected";
                student.Remarks = remarks;
                _context.SaveChanges();
                TempData["ErrorMessage"] = $"{student.FullName} has been rejected.";
            }
            return RedirectToAction("Admin");
        }
    }

    public class StudentViewModel
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Email { get; set; }
        public string Course { get; set; }
        public string Status { get; set; }
        public string RegistrationDate { get; set; }
        public string DocumentPath { get; set; }
        public string Remarks { get; set; }
    }

    public class AdminDashboardViewModel
    {
        public List<StudentViewModel> PendingStudents { get; set; } = new List<StudentViewModel>();
        public List<StudentViewModel> ProcessedStudentsLog { get; set; } = new List<StudentViewModel>();
    }
}