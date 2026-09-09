using System;

namespace Contoso.Discounts
{
    public interface IProductRepository
    {
        Product Get(int id);
    }

    public sealed class Product
    {
        public int Id { get; set; }

        public decimal Price { get; set; }
    }

    public sealed class DiscountService
    {
        private readonly IProductRepository _repository;

        public DiscountService(IProductRepository repository)
        {
            _repository = repository;
        }

        public decimal Apply(int productId, decimal percentage)
        {
            if (percentage < 0m || percentage > 100m)
            {
                throw new ArgumentOutOfRangeException(nameof(percentage));
            }

            Product product = _repository.Get(productId);
            if (product == null)
            {
                throw new InvalidOperationException("Product not found.");
            }

            return product.Price * (1m - (percentage / 100m));
        }
    }
}
