class SyncJob
{
    void Run(BlogContext context)
    {
        var blogs = context.Blogs.ToList();
        foreach (var b in blogs) { b.Name = b.Name.Trim(); }
        context.SaveChanges();
    }
}
