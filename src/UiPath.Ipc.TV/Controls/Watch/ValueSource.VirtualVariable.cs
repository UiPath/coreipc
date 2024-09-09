namespace UiPath.Ipc.TV;

partial class ValueSource
{
    public sealed class VirtualVariable : ValueSource
    {
        public string CSharpTypeName { get; }
        private readonly string _name;
        private readonly object? _value;

        public VirtualVariable(string name, string cSharpTypeName, object? value)
        {
            _name = name;
            CSharpTypeName = cSharpTypeName;
            _value = value;
        }

        public override string GetName() => _name;
        public override Type GetDeclaredType() => throw new NotSupportedException();
        public override object? GetValue() => _value;
        public override string GetImageKey() => "Variable";
    }
}

