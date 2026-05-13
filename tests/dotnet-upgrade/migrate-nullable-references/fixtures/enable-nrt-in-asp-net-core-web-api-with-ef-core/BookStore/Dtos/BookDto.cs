namespace BookStore.Dtos;

public class CreateBookRequest
{
    public string Title { get; set; }
    public string Isbn { get; set; }
    public decimal Price { get; set; }
    public string Summary { get; set; }
    public int AuthorId { get; set; }
    public int? CategoryId { get; set; }
}

public class BookResponse
{
    public int Id { get; set; }
    public string Title { get; set; }
    public string Isbn { get; set; }
    public decimal Price { get; set; }
    public string AuthorName { get; set; }
    public string CategoryName { get; set; }
}
