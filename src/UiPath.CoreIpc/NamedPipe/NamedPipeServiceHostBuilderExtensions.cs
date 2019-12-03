namespace UiPath.CoreIpc.NamedPipe
{
    public static class NamedPipeServiceHostBuilderExtensions
    {
        public static ServiceHostBuilder AddNamedPipes(this ServiceHostBuilder builder, NamedPipeSettings settings) =>
            builder.AddListener(new NamedPipeListener(builder.ServiceProvider, settings));
    }
}