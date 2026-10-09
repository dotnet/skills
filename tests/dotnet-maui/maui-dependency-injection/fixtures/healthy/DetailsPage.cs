namespace Store;

public sealed class ProductDetailsPage : ContentPage
{
    public ProductDetailsPage(ProductDetailsViewModel viewModel)
    {
        BindingContext = viewModel;
    }
}
