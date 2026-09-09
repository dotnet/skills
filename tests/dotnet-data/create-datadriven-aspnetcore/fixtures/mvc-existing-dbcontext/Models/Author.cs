using System.ComponentModel.DataAnnotations;

namespace CatalogMvc.Models;

public class Author
{
    public int Id { get; set; }

    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    public ICollection<Book> Books { get; set; } = new List<Book>();
}
