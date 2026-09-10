public static class Product
{
    public static int Calculate(ReadOnlySpan<int> values)
    {
        if (values.IsEmpty)
        {
            return 0;
        }

        int product = 1;
        foreach (int value in values)
        {
            product *= value;
        }

        return product;
    }
}
