using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using UiPath.Ipc;

namespace UiPath.Ipc.Tests
{
    public class ComputingService : IComputingService
    {
        private readonly ILogger<ComputingService> _logger;

        public ComputingService(ILogger<ComputingService> logger) // inject dependencies in constructor
        {
            _logger = logger;
        }

        public async Task<ComplexNumber> AddComplexNumber(ComplexNumber x, ComplexNumber y, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation($"{nameof(AddComplexNumber)} called.");
            return new ComplexNumber(x.A + y.A, x.B + y.B);
        }

        public async Task<ComplexNumber> AddComplexNumbers(IEnumerable<ComplexNumber> numbers, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation($"{nameof(AddComplexNumbers)} called.");
            var result = new ComplexNumber(0, 0);
            foreach (ComplexNumber number in numbers)
            {
                result = new ComplexNumber(result.A + number.A, result.B + number.B);
            }
            return result;
        }

        public async Task<float> AddFloat(float x, float y, CancellationToken cancellationToken = default)
        {
            //Trace.WriteLine($"{nameof(AddFloat)} called.");
            _logger.LogInformation($"{nameof(AddFloat)} called.");
            return x + y;
        }

        public async Task<bool> Infinite(CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return true;
        }

        public Task InfiniteVoid(CancellationToken cancellationToken = default) =>Task.Delay(Timeout.Infinite, cancellationToken);

        public async Task<string> SendMessage(SystemMessage message, CancellationToken cancellationToken = default)
        {
            await Task.Delay(message.Delay);
            var client = message.Client;
            var callback = client.GetCallback<IComputingCallback>();
            var clientId = await callback.GetId(message);
            string returnValue = "";
            client.Impersonate(() => returnValue = client.UserName + "_" + clientId + "_" + message.Text);
            return returnValue;
        }

        public async Task<string> GetCallbackThreadName(Message message, CancellationToken cancellationToken = default) => await message.GetCallback<IComputingCallback>().GetThreadName();
    }
}