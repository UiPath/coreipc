namespace UiPath.CoreIpc.NamedPipe
{
    public static class NamedPipeServiceHostBuilderExtensions
    {
        public static ServiceHostBuilder AddEndpoint<TContract>(this ServiceHostBuilder builder, NamedPipeEndpointSettings<TContract> settings) where TContract : class =>
            builder.AddEndpoint(new NamedPipeServiceEndpoint<TContract>(builder.ServiceProvider, settings));
    }
}