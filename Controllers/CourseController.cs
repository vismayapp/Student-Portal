using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using Login.Models;
using Login.Data;

namespace Login.Controllers
{
    public class CourseController : Controller
    {
        private readonly AppDbContext _context;

        public CourseController(AppDbContext context)
        {
            _context = context;
        }

        // =========================================================================
        // ✅ FIX: Added the missing helper method required by your action endpoints
        // =========================================================================
        private Student GetCurrentStudent()
        {
            string loggedInEmail = HttpContext.Session.GetString("UserEmail");
            if (string.IsNullOrEmpty(loggedInEmail)) return null;

            return _context.Students.FirstOrDefault(s => s.Email == loggedInEmail);
        }

        // GET: /Course/Profile
        [HttpGet]
        public IActionResult Profile()
        {
            var studentModel = GetCurrentStudent();

            if (studentModel == null)
            {
                TempData["ErrorMessage"] = "Please sign in to view your dashboard profile context.";
                return RedirectToAction("Login", "Home");
            }

            LoadUploadedDocuments(studentModel.Id);

            // Explicit root layout target paths since no separate Course view folder exists
            return View("~/Views/Home/Profile.cshtml", studentModel);
        }

        // POST: /Course/Profile
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Profile(Student updatedStudent)
        {
            var existingStudent = GetCurrentStudent();
            if (existingStudent == null)
            {
                TempData["ErrorMessage"] = "Active session expired. Please log in.";
                return RedirectToAction("Login", "Home");
            }

            // Remove internal system parameters from model validation loop
            ModelState.Remove("Password");
            ModelState.Remove("Status");
            ModelState.Remove("Remarks");

            if (ModelState.IsValid)
            {
                try
                {
                    // Secure Field Mapping: Only overwrite explicitly exposed client properties
                    existingStudent.FullName = updatedStudent.FullName;
                    existingStudent.Email = updatedStudent.Email;
                    existingStudent.Course = updatedStudent.Course;
                    existingStudent.Gender = updatedStudent.Gender;

                    _context.Students.Update(existingStudent);
                    _context.SaveChanges();

                    // Keep active session synchronized with modifications
                    HttpContext.Session.SetString("UserEmail", existingStudent.Email);

                    TempData["SuccessMessage"] = "Profile records modified successfully!";
                    return RedirectToAction("Profile");
                }
                catch (Exception ex)
                {
                    TempData["ErrorMessage"] = "Database context persistence sync failure: " + ex.Message;
                    return RedirectToAction("Profile");
                }
            }

            LoadUploadedDocuments(existingStudent.Id);
            TempData["ErrorMessage"] = "Form validation context failed. Verify field inputs.";
            return View("~/Views/Home/Profile.cshtml", updatedStudent);
        }

        // POST: /Course/UploadProfilePicture
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UploadProfilePicture(IFormFile ProfilePictureFile)
        {
            var student = GetCurrentStudent();
            if (student == null)
            {
                TempData["ErrorMessage"] = "Active session expired. Please log in.";
                return RedirectToAction("Login", "Home");
            }

            if (ProfilePictureFile != null && ProfilePictureFile.Length > 0)
            {
                try
                {
                    string folder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads");
                    if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

                    // Drop legacy file track system asset before binding replacement data
                    if (!string.IsNullOrEmpty(student.ProfilePicture) && student.ProfilePicture != "default-avatar.png")
                    {
                        string oldFilePath = Path.Combine(folder, student.ProfilePicture);
                        if (System.IO.File.Exists(oldFilePath))
                        {
                            System.IO.File.Delete(oldFilePath);
                        }
                    }

                    string uniqueName = $"{student.Id}_{Guid.NewGuid().ToString().Substring(0, 5)}{Path.GetExtension(ProfilePictureFile.FileName)}";
                    string fullPath = Path.Combine(folder, uniqueName);

                    using (var stream = new FileStream(fullPath, FileMode.Create))
                    {
                        await ProfilePictureFile.CopyToAsync(stream);
                    }

                    student.ProfilePicture = uniqueName;
                    _context.Students.Update(student);
                    _context.SaveChanges();

                    TempData["SuccessMessage"] = "Profile avatar icon updated successfully!";
                }
                catch (Exception ex)
                {
                    TempData["ErrorMessage"] = "File storage layer allocation error: " + ex.Message;
                }
            }
            else
            {
                TempData["ErrorMessage"] = "Please select a valid image tracking resource file.";
            }

            return RedirectToAction("Profile");
        }

        // POST: /Course/UploadStudentDocument
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UploadStudentDocument(int studentId, IFormFile StudentDocument)
        {
            var student = GetCurrentStudent();
            if (student == null || student.Id != studentId)
            {
                TempData["ErrorMessage"] = "Unauthorized profile context action verification failed.";
                return RedirectToAction("Login", "Home");
            }

            if (StudentDocument != null && StudentDocument.Length > 0)
            {
                try
                {
                    string folder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "documents");
                    if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

                    string filename = $"{student.Id}_{Guid.NewGuid().ToString().Substring(0, 8)}_{Path.GetFileName(StudentDocument.FileName)}";
                    string fullPath = Path.Combine(folder, filename);

                    using (var stream = new FileStream(fullPath, FileMode.Create))
                    {
                        await StudentDocument.CopyToAsync(stream);
                    }

                    TempData["SuccessMessage"] = "Verification document cataloged into filesystem safe.";
                }
                catch (Exception ex)
                {
                    TempData["ErrorMessage"] = "Failed to preserve attachment element: " + ex.Message;
                }
            }
            return RedirectToAction("Profile");
        }

        // POST: /Course/DeleteStudentDocument
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteStudentDocument(string fileName)
        {
            var student = GetCurrentStudent();
            if (student == null)
            {
                TempData["ErrorMessage"] = "Active session expired. Please log in.";
                return RedirectToAction("Login", "Home");
            }

            // Extract file name safely and isolate resource parameter scope to current student ID
            string targetedCleanFile = Path.GetFileName(fileName);
            if (!targetedCleanFile.StartsWith($"{student.Id}_"))
            {
                TempData["ErrorMessage"] = "Unauthorized execution context: Access to resource denied.";
                return RedirectToAction("Profile");
            }

            try
            {
                string docFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "documents");
                string safeFilePath = Path.Combine(docFolder, targetedCleanFile);

                if (System.IO.File.Exists(safeFilePath))
                {
                    System.IO.File.Delete(safeFilePath);
                    TempData["SuccessMessage"] = "Document dropped from secure server storage.";
                }
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Error removing resource element: " + ex.Message;
            }

            return RedirectToAction("Profile");
        }

        // GET: /Course/GetCoursesData
        [HttpGet]
        public IActionResult GetCoursesData()
        {
            var dataMap = new Dictionary<string, List<object>>
            {
                {
                    "Information Technology", new List<object> {
                        new { id = "cloud-arch", name = "Cloud Architecture (AWS/Azure)", fee = 12500, institutions = new List<string>{"IIT Madras", "BITS Pilani Virtual"} },
                        new { id = "fullstack-dev", name = "Advanced Fullstack Web Systems", fee = 9800, institutions = new List<string>{"NIT Calicut", "EduPortal Hub"} }
                    }
                },
                {
                    "Computer Science", new List<object> {
                        new { id = "ai-ml", name = "Artificial Intelligence & Deep Learning", fee = 15000, institutions = new List<string>{"IISc Bangalore", "IIT Bombay"} },
                        new { id = "cyber-sec", name = "Cyber Security Operations", fee = 11200, institutions = new List<string>{"Amrita Vishwa Vidyapeetham"} }
                    }
                },
                {
                    "Mechanical Engineering", new List<object> {
                        new { id = "cad-cam", name = "Industrial Robotics and CAD/CAM Automation", fee = 8500, institutions = new List<string>{"IIT Roorkee"} }
                    }
                },
                {
                    "Electrical Engineering", new List<object> {
                        new { id = "iot-embedded", name = "Smart Power Grids & Embedded IoT systems", fee = 9500, institutions = new List<string>{"NIT Trichy"} }
                    }
                }
            };

            return Json(dataMap);
        }

        private void LoadUploadedDocuments(int studentId)
        {
            var documentList = new List<string>();
            string docFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "documents");

            if (Directory.Exists(docFolder))
            {
                documentList = Directory.GetFiles(docFolder)
                                        .Select(Path.GetFileName)
                                        .Where(f => f.StartsWith(studentId + "_"))
                                        .ToList();
            }
            ViewBag.UploadedDocuments = documentList;
        }

        // GET: /Course/GooglePaySimulator
        [HttpGet]
        public IActionResult GooglePaySimulator(string amount, string item, string institution)
        {
            ViewBag.Amount = amount;
            ViewBag.Item = item;
            ViewBag.Institution = institution;

            return View("~/Views/Home/GooglePayGateway.cshtml");
        }

        // GET: /Course/PaymentSuccess
        [HttpGet]
        public IActionResult PaymentSuccess(string amount, string item, string institution)
        {
            ViewBag.Amount = amount;
            ViewBag.Item = item;
            ViewBag.Institution = institution;

            return View("~/Views/Home/PaymentSuccess.cshtml");
        }

        // GET: /Course/Logout
        [HttpGet]
        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            return RedirectToAction("Login", "Home");
        }
    }
}