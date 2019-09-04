using System.Threading;
using System.Threading.Tasks;

namespace UiPath.Ipc.TestServer
{
    public static class Contract
    {
        public sealed class Complex
        {
            public double X { get; set; }
            public double Y { get; set; }
        }

        public interface IService
        {
            Task<Complex> AddAsync(Complex a, Message<Complex> b, CancellationToken ct = default);
        }
        public interface ICallback
        {
            Task<double> AddAsync(double a, double b);
        }
    }
}
