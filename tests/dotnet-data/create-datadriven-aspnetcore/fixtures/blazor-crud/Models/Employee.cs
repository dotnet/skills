using System.ComponentModel.DataAnnotations;

namespace BlazorApp.Models;

public class Employee
{
    public int Id { get; set; }

    [Required]
    [StringLength(100)]
    public string FirstName { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string LastName { get; set; } = string.Empty;

    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    public int DepartmentId { get; set; }
    public Department Department { get; set; } = null!;
}
