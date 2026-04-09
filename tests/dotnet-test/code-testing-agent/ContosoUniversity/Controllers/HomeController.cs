using System.Collections.Generic;
using System.Linq;
using ContosoUniversity.Data;
using ContosoUniversity.Models.SchoolViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace ContosoUniversity.Controllers
{
    public class HomeController : BaseController
    {
        public HomeController(SchoolContext context) : base(context)
        {
        }

        public ActionResult Index()
        {
            return View();
        }

        public ActionResult About()
        {
            IQueryable<EnrollmentDateGroup> data = 
                from student in db.Students
                group student by student.EnrollmentDate into dateGroup
                select new EnrollmentDateGroup()
                {
                    EnrollmentDate = dateGroup.Key,
                    StudentCount = dateGroup.Count()
                };
            return View(data.ToList());
        }

        public ActionResult Contact()
        {
            ViewBag.Message = "Your contact page.";

            return View();
        }

        public ActionResult Error()
        {
            return View();
        }

        public new ActionResult Unauthorized()
        {
            ViewBag.Message = "You don't have permission to access this resource.";
            return View();
        }
    }
}
