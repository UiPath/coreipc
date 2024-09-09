namespace UiPath.Ipc.TV;

partial class ValueSource
{
    public sealed class VirtualProperty : ValueSource
    {
        private readonly string _name;
        private readonly Type _declaredType;
        private readonly object? _value;

        public bool Reference { get; init; }

        public VirtualProperty(string name, Type declaredType, object? value)
        {
            _name = name;
            _declaredType = declaredType;
            _value = value;
        }

        public override string GetName() => _name;
        public override Type GetDeclaredType() => _declaredType;
        public override object? GetValue() => _value;
        public override string GetImageKey() => "Property";

        public override bool IsReference(out string? id)
        {
            if (Reference)
            {
                id = _value as string;
            }
            else
            {
                id = null;
            }

            return Reference;
        }
    }
}

