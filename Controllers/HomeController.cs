using Login.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using Login.Data;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using System;
using System.IO;
using Microsoft.AspNetCore.Http;

namespace Login.Controllers
{
    public class HomeController : Controller
    {
        private readonly AppDbContext _context;

        public HomeController(AppDbContext context)
        {
            _context = context;
        }

        public IActionResult Index()
        {
            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }

        // POST: Saves registration data to the SQL Database table safely
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            if (ModelState.IsValid)
            {
                var cleanEmail = model.Email.Trim().ToLower();
                var userExists = await _context.Students.AnyAsync(s => s.Email.Trim().ToLower() == cleanEmail);

                if (userExists)
                {
                    ModelState.AddModelError("Email", "This email address is already registered.");
                    return View("Index", model);
                }

                var newStudent = new Student
                {
                    FullName = model.FullName,
                    Email = model.Email.Trim(),
                    Gender = model.Gender,
                    Course = model.Course,
                    Password = model.Password,
                    Status = "Pending",
                    RegistrationDate = DateTime.Now,

                    // 👇 CRITICAL FIX: Explicitly pass empty strings so SQL Server does not complain about NULL values
                    DocumentPath = "",
                    Remarks = ""
                };

                _context.Students.Add(newStudent);
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = "Registration Successful! Please wait for admin approval.";
                return RedirectToAction("Login");
            }
            return View("Index", model);
        }

        // GET: Loads the Login Page view
        [HttpGet]
        public IActionResult Login()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (ModelState.IsValid)
            {
                var cleanEmail = model.Email.Trim().ToLower();

                var user = await _context.Students
                    .FirstOrDefaultAsync(u => u.Email.Trim().ToLower() == cleanEmail);

                if (user != null && user.Password.Trim() == model.Password.Trim())
                {
                    // 🚀 The restriction check has been removed. All registered users can sign in!
                    var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, user.FullName),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim("StudentId", user.Id.ToString())
            };

                    var identity = new ClaimsIdentity(claims, "CookieAuth");
                    var principal = new ClaimsPrincipal(identity);

                    await HttpContext.SignInAsync("CookieAuth", principal);

                    TempData["SuccessMessage"] = "Logged in successfully!";
                    return RedirectToAction("Profile");
                }

                ModelState.AddModelError(string.Empty, "Invalid email or password combination.");
            }

            return View(model);
        }



        // GET: Loads the profile page with student details AND tracks uploaded files
        [Authorize]
        [HttpGet]
        public async Task<IActionResult> Profile()
        {
            var loggedInEmail = User.FindFirst(ClaimTypes.Email)?.Value;

            if (string.IsNullOrEmpty(loggedInEmail))
            {
                return RedirectToAction("Login");
            }

            var currentStudent = await _context.Students.FirstOrDefaultAsync(s => s.Email == loggedInEmail);

            if (currentStudent == null)
            {
                return NotFound();
            }

            // READ UPLOADED DOCUMENTS FROM SERVER STORAGE
            var uploadedFiles = new List<string>();
            var uploadFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "documents");

            if (Directory.Exists(uploadFolder))
            {
                var allFiles = Directory.GetFiles(uploadFolder);
                foreach (var file in allFiles)
                {
                    var fileName = Path.GetFileName(file);
                    // Filter files so the student only sees items prefixed with their unique ID
                    if (fileName.StartsWith(currentStudent.Id.ToString() + "_"))
                    {
                        uploadedFiles.Add(fileName);
                    }
                }
            }

            ViewBag.UploadedDocuments = uploadedFiles;
            return View(currentStudent);
        }

        // POST: Handles inline profile modifications instantly
        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Profile(Student model)
        {
            var existingStudent = await _context.Students.FindAsync(model.Id);
            if (existingStudent == null)
            {
                TempData["ErrorMessage"] = "Student profile could not be located.";
                return RedirectToAction("Profile");
            }

            if (string.IsNullOrWhiteSpace(model.FullName) || string.IsNullOrWhiteSpace(model.Email))
            {
                TempData["ErrorMessage"] = "Full Name and Email Address cannot be empty.";
                return RedirectToAction("Profile");
            }

            existingStudent.FullName = model.FullName;
            existingStudent.Email = model.Email;
            existingStudent.Course = model.Course;
            existingStudent.Gender = model.Gender;

            try
            {
                _context.Update(existingStudent);
                await _context.SaveChangesAsync();
                TempData.Remove("ErrorMessage");
                TempData["SuccessMessage"] = "Profile updated successfully!";
            }
            catch (Exception)
            {
                TempData["ErrorMessage"] = "An error occurred while saving records to the database.";
            }

            return RedirectToAction("Profile");
        }

        // POST: Handles document files processing, saving, and updating database link
        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UploadStudentDocument(IFormFile StudentDocument, int studentId)
        {
            if (StudentDocument == null || StudentDocument.Length == 0)
            {
                TempData["ErrorMessage"] = "Please select a document file to upload.";
                return RedirectToAction("Profile");
            }

            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png" };
            var extension = Path.GetExtension(StudentDocument.FileName).ToLower();

            if (!allowedExtensions.Contains(extension))
            {
                TempData["ErrorMessage"] = "Invalid file type. Only JPG, JPEG, and PNG files are allowed.";
                return RedirectToAction("Profile");
            }

            try
            {
                var uploadFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "documents");
                if (!Directory.Exists(uploadFolder))
                {
                    Directory.CreateDirectory(uploadFolder);
                }

                // Format: StudentID_GUID_FileName
                var uniqueFileName = $"{studentId}_{Guid.NewGuid()}_{Path.GetFileName(StudentDocument.FileName)}";
                var filePath = Path.Combine(uploadFolder, uniqueFileName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await StudentDocument.CopyToAsync(stream);
                }

                // 🌟 FIX FOR THE ADMIN DASHBOARD "NO FILE" ISSUE:
                // This updates the 'DocumentPath' field inside the database for this specific student record.
                var student = await _context.Students.FindAsync(studentId);
                if (student != null)
                {
                    // Storing the relative path path so the Admin view anchor tag can find it under /documents/filename.ext
                    student.DocumentPath = "/documents/" + uniqueFileName;
                    _context.Update(student);
                    await _context.SaveChangesAsync();
                }

                TempData["SuccessMessage"] = "Document uploaded successfully!";
            }
            catch (Exception)
            {
                TempData["ErrorMessage"] = "An error occurred while uploading your document.";
            }

            return RedirectToAction("Profile");
        }

        // POST: Deletes a specific verification document safely if it belongs to the logged-in student
        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteStudentDocument(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
            {
                TempData["ErrorMessage"] = "Invalid file deletion request.";
                return RedirectToAction("Profile");
            }

            try
            {
                var studentIdClaim = User.FindFirst("StudentId")?.Value;

                // Security check: Ensure the file strictly belongs to the active student session
                if (string.IsNullOrEmpty(studentIdClaim) || !fileName.StartsWith(studentIdClaim + "_"))
                {
                    TempData["ErrorMessage"] = "Unauthorized action. You cannot delete this file.";
                    return RedirectToAction("Profile");
                }

                var uploadFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "documents");
                var filePath = Path.Combine(uploadFolder, fileName);

                if (System.IO.File.Exists(filePath))
                {
                    System.IO.File.Delete(filePath);

                    // Clear out database link property to reset value state
                    if (int.TryParse(studentIdClaim, out int studentId))
                    {
                        var student = await _context.Students.FindAsync(studentId);
                        if (student != null)
                        {
                            student.DocumentPath = "";
                            _context.Update(student);
                            await _context.SaveChangesAsync();
                        }
                    }

                    TempData["SuccessMessage"] = "Document removed successfully.";
                }
                else
                {
                    TempData["ErrorMessage"] = "The document file could not be found on the server.";
                }
            }
            catch (Exception)
            {
                TempData["ErrorMessage"] = "An error occurred while removing the document.";
            }

            return RedirectToAction("Profile");
        }

        // POST: Handles profile picture updates
        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UploadProfilePicture(IFormFile ProfilePictureFile)
        {
            if (ProfilePictureFile == null || ProfilePictureFile.Length == 0)
            {
                TempData["ErrorMessage"] = "Please select an image file to upload.";
                return RedirectToAction("Profile");
            }

            var loggedInEmail = User.FindFirst(ClaimTypes.Email)?.Value;
            var currentStudent = await _context.Students.FirstOrDefaultAsync(s => s.Email == loggedInEmail);

            if (currentStudent == null)
            {
                return NotFound();
            }

            try
            {
                var uploadFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads");
                if (!Directory.Exists(uploadFolder))
                {
                    Directory.CreateDirectory(uploadFolder);
                }

                var uniqueFileName = $"{currentStudent.Id}_Avatar_{Guid.NewGuid()}{Path.GetExtension(ProfilePictureFile.FileName)}";
                var filePath = Path.Combine(uploadFolder, uniqueFileName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await ProfilePictureFile.CopyToAsync(stream);
                }

                currentStudent.ProfilePicture = uniqueFileName;
                _context.Update(currentStudent);
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = "Profile picture updated successfully!";
            }
            catch (Exception)
            {
                TempData["ErrorMessage"] = "An error occurred while changing your profile photo.";
            }

            return RedirectToAction("Profile");
        }
        [HttpGet]
        public IActionResult ForgotPassword()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> ForgotPassword(string email)
        {
            var student = await _context.Students.FirstOrDefaultAsync(s => s.Email == email);
            if (student == null)
            {
                ViewBag.Message = "There is no account in this emmail";
                return View();
            }

            string token = Guid.NewGuid().ToString();
            student.ResetToken = token;
            student.ResetTokenExpiry = DateTime.Now.AddMinutes(30);
            await _context.SaveChangesAsync();

            // 💡 Generate the route pointing to Home instead of Account
            string resetLink = Url.Action("ResetPassword", "Home", new { token = token }, Request.Scheme);

            // Pass the message context and the raw link address safely to the view
            ViewBag.Message = "Success";
            ViewBag.ResetLink = resetLink;

            return View();
        }

        [HttpGet]
        public async Task<IActionResult> ResetPassword(string token)
        {
            var student = await _context.Students.FirstOrDefaultAsync(
                s => s.ResetToken == token && s.ResetTokenExpiry > DateTime.Now);

            if (student == null)
            {
                ViewBag.Message = "Link invalid or expired";
                return View("Error");
            }

            ViewBag.Token = token;
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> ResetPassword(string token, string newPassword)
        {
            var student = await _context.Students.FirstOrDefaultAsync(
                s => s.ResetToken == token && s.ResetTokenExpiry > DateTime.Now);

            if (student == null)
            {
                ViewBag.Message = "Link expired";
                return View("Error");
            }

            student.Password = newPassword;
            student.ResetToken = null;
            student.ResetTokenExpiry = null;
            await _context.SaveChangesAsync();

            TempData["Success"] = "You can login afer resetting password";
            return RedirectToAction("Login");
        }

       


        // GET: Drops active cookie layout sessions securely
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync("CookieAuth");
            return RedirectToAction("Login");
        }
    }
}