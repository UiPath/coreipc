namespace UiPath.CoreIpc.NamedPipe
{
    public static class NamedPipeServiceHostBuilderExtensions
    {
        public static ServiceHostBuilder AddNamedPipes(this ServiceHostBuilder builder, NamedPipeSettings settings)
        {
            settings.ServiceProvider = builder.ServiceProvider;
            settings.Endpoints = builder.Endpoints;
            builder.AddListener(new NamedPipeListener(settings));
            return builder;
        }
    }
}