class EnvVarCheck
{
    void CheckFlag()
    {
        // On Linux, set via: export MY_FLAG=""
        var val = Environment.GetEnvironmentVariable("MY_FLAG");
        if (val == null)
        {
            Console.WriteLine("MY_FLAG not set, using default");
        }
    }
}
