using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq.Expressions;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace UiPath.CoreIpc
{
    using BeforeCallHandler = Func<CallInfo, CancellationToken, Task>;
    using MethodExecutor = Func<object, object[], Task>;
    using static Expression;
    public class ServiceHostBuilder
    {
        private readonly List<Listener> _listeners = new();
        public ServiceHostBuilder(IServiceProvider serviceProvider) => ServiceProvider = serviceProvider;
        internal IServiceProvider ServiceProvider { get; }
        internal Dictionary<string, EndpointSettings> Endpoints { get; } = new();
        public ServiceHostBuilder AddEndpoint(EndpointSettings settings)
        {
            settings.ServiceProvider = ServiceProvider;
            Endpoints.Add(settings.Name, settings);
            return this;
        }
        internal ServiceHostBuilder AddListener(Listener listener)
        {
            listener.Settings.ServiceProvider = ServiceProvider;
            listener.Settings.Endpoints = Endpoints;
            _listeners.Add(listener);
            return this;
        }
        public ServiceHost Build() => new(_listeners, Endpoints, ServiceProvider);
    }
    public static class ServiceHostBuilderExtensions
    {
        public static ServiceHostBuilder AddEndpoints(this ServiceHostBuilder serviceHostBuilder, IEnumerable<EndpointSettings> endpoints)
        {
            foreach (var endpoint in endpoints)
            {
                serviceHostBuilder.AddEndpoint(endpoint);
            }
            return serviceHostBuilder;
        }
        public static ServiceHostBuilder AddEndpoint<TContract>(this ServiceHostBuilder serviceHostBuilder, TContract serviceInstance = null) where TContract : class => 
            serviceHostBuilder.AddEndpoint(new EndpointSettings<TContract>(serviceInstance));
        public static ServiceHostBuilder AddEndpoint<TContract, TCallbackContract>(this ServiceHostBuilder serviceHostBuilder, TContract serviceInstance = null) where TContract : class where TCallbackContract : class =>
            serviceHostBuilder.AddEndpoint(new EndpointSettings<TContract, TCallbackContract>(serviceInstance));
    }
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddIpc(this IServiceCollection services)
        {
            services.AddSingleton<ISerializer, JsonSerializer>();
            return services;
        }
    }
    public class EndpointSettings
    {
        readonly ConcurrentDictionaryWrapper<string, Method> _methods;
        public EndpointSettings(Type contract, object serviceInstance = null, Type callbackContract = null)
        {
            Contract = contract ?? throw new ArgumentNullException(nameof(contract));
            Name = contract.Name;
            ServiceInstance = serviceInstance;
            CallbackContract = callbackContract;
            _methods = new(CreateMethod);
        }
        internal string Name { get; }
        internal TaskScheduler Scheduler { get; set; }
        internal object ServiceInstance { get; }
        internal Type Contract { get; }
        internal Type CallbackContract { get; }
        internal IServiceProvider ServiceProvider { get; set; }
        public BeforeCallHandler BeforeCall { get; set; }
        public void Validate() => Validator.Validate(Contract, CallbackContract);
        internal Method GetMethod(string name) => _methods.GetOrAdd(name);
        Method CreateMethod(string methodName)
        {
            var method = Contract.GetInterfaceMethod(methodName);
            return new Method(method);
        }
    }
    readonly struct Method
    {
        readonly MethodExecutor _executor;
        readonly MethodInfo _methodInfo;
        public ParameterInfo[] Parameters { get; }
        public object[] Defaults { get; }
        public Type ReturnType => _methodInfo.ReturnType;
        public Method(MethodInfo method)
        {
            // Parameters to executor
            var targetParameter = Parameter(typeof(object), "target");
            var parametersParameter = Parameter(typeof(object[]), "parameters");
            // Build parameter list
            Parameters = method.GetParameters();
            var parameters = new List<Expression>(Parameters.Length);
            Defaults = new object[Parameters.Length];
            for (int index = 0; index < Parameters.Length; index++)
            {
                var paramInfo = Parameters[index];
                Defaults[index] = paramInfo.GetDefaultValue();
                var valueObj = ArrayIndex(parametersParameter, Constant(index));
                var valueCast = Convert(valueObj, paramInfo.ParameterType);
                // valueCast is "(Ti) parameters[i]"
                parameters.Add(valueCast);
            }
            // Call method
            var instanceCast = Convert(targetParameter, method.DeclaringType);
            var methodCall = Call(instanceCast, method, parameters);
            // methodCall is "((Ttarget) target) method((T0) parameters[0], (T1) parameters[1], ...)"
            // must coerce methodCall to match ActionExecutor signature
            var lambda = Lambda<MethodExecutor>(methodCall, targetParameter, parametersParameter);
            _executor = lambda.Compile();
            _methodInfo = method;
        }
        public Task Invoke(object service, object[] arguments) => _executor.Invoke(service, arguments);
        public override string ToString() => _methodInfo.ToString();
    }
    public class EndpointSettings<TContract> : EndpointSettings where TContract : class
    {
        public EndpointSettings(TContract serviceInstance = null, Type callbackContract = null) : base(typeof(TContract), serviceInstance, callbackContract) { }
    }
    public class EndpointSettings<TContract, TCallbackContract> : EndpointSettings<TContract> where TContract : class where TCallbackContract : class
    {
        public EndpointSettings(TContract serviceInstance = null) : base(serviceInstance, typeof(TCallbackContract)) { }
    }
}