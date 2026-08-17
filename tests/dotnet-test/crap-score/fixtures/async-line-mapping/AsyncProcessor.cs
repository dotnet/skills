using System.Threading.Tasks;

namespace Contoso.Risk
{
    public sealed class AsyncProcessor
    {
        public async Task<int> ProcessAsync(int value)
        {
            await Task.Yield();

            if (value < 0)
            {
                return -1;
            }

            if (value == 0 || value > 10)
            {
                return 0;
            }

            return value * 2;
        }
    }
}
