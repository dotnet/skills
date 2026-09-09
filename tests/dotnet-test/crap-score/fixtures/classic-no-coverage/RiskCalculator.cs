namespace Legacy.Tests
{
    public static class RiskCalculator
    {
        public static int Classify(int value)
        {
            if (value < 0)
            {
                return -1;
            }

            return value == 0 ? 0 : 1;
        }
    }
}
