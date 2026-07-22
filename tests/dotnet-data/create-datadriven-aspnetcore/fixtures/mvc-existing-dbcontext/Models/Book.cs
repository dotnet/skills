using System.ComponentModel.DataAnnotations;

namespace CatalogMvc.Models;

public class Book
{
    public int Id { get; set; }

    [Required]
    [StringLength(200)]
    public string Title { get; set; } = string.Empty;

    [Range(1, 10000)]
    public int PublicationYear { get; set; }

    public int AuthorId { get; set; }

    public Author Author { get; set; } = null!;
}
