using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Catalog.Pages.Products;

public sealed class CreateModel : PageModel
{
    [BindProperty, Required]
    public string Name { get; set; } = "";

    [BindProperty]
    public decimal Price { get; set; }

    public IActionResult OnPost() => ModelState.IsValid ? Redirect("/") : Page();
}
