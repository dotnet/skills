using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BookStore.Data;
using BookStore.Dtos;
using BookStore.Models;

namespace BookStore.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BooksController : ControllerBase
{
    private readonly BookStoreContext _context;

    public BooksController(BookStoreContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<List<BookResponse>>> GetAll()
    {
        var books = await _context.Books
            .Include(b => b.Author)
            .Include(b => b.Category)
            .ToListAsync();

        return books.Select(b => new BookResponse
        {
            Id = b.Id,
            Title = b.Title,
            Isbn = b.Isbn,
            Price = b.Price,
            AuthorName = b.Author.Name,
            CategoryName = b.Category != null ? b.Category.Name : "Uncategorized"
        }).ToList();
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<BookResponse>> GetById(int id)
    {
        var book = await _context.Books
            .Include(b => b.Author)
            .Include(b => b.Category)
            .FirstOrDefaultAsync(b => b.Id == id);

        if (book == null) return NotFound();

        return new BookResponse
        {
            Id = book.Id,
            Title = book.Title,
            Isbn = book.Isbn,
            Price = book.Price,
            AuthorName = book.Author.Name,
            CategoryName = book.Category != null ? book.Category.Name : "Uncategorized"
        };
    }

    [HttpPost]
    public async Task<ActionResult<BookResponse>> Create(CreateBookRequest request)
    {
        var author = await _context.Authors.FindAsync(request.AuthorId);
        if (author == null) return BadRequest("Author not found");

        var book = new Book
        {
            Title = request.Title,
            Isbn = request.Isbn,
            Price = request.Price,
            Summary = request.Summary,
            AuthorId = request.AuthorId,
            Author = author,
            CategoryId = request.CategoryId
        };

        _context.Books.Add(book);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = book.Id }, new BookResponse
        {
            Id = book.Id,
            Title = book.Title,
            Isbn = book.Isbn,
            Price = book.Price,
            AuthorName = author.Name,
            CategoryName = "Uncategorized"
        });
    }

    [HttpGet("search")]
    public async Task<ActionResult<List<BookResponse>>> Search(string query)
    {
        var books = await _context.Books
            .Include(b => b.Author)
            .Where(b => b.Title.Contains(query) || b.Author.Name.Contains(query))
            .ToListAsync();

        return books.Select(b => new BookResponse
        {
            Id = b.Id,
            Title = b.Title,
            Isbn = b.Isbn,
            Price = b.Price,
            AuthorName = b.Author.Name,
            CategoryName = b.Category != null ? b.Category.Name : "Uncategorized"
        }).ToList();
    }
}
