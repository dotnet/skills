namespace ProdOp;

public static class MathOps
{
    /// <summary>Computes the product of all elements.</summary>
    public static float Product(ReadOnlySpan<float> values)
    {
        if (values.IsEmpty) return 0f;
        float product = 1f;
        for (int i = 0; i < values.Length; i++)
            product *= values[i];
        return product;
    }
}
