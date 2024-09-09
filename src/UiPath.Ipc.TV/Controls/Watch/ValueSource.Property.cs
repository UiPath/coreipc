using System.Reflection;

namespace UiPath.Ipc.TV;

partial class ValueSource
{
    public sealed class Property : ValueSource
    {
        private readonly object _instance;
        private readonly PropertyInfo _propertyInfo;

        public Property(object instance, PropertyInfo propertyInfo)
        {
            _instance = instance;
            _propertyInfo = propertyInfo;
        }

        public override string GetName() => _propertyInfo.Name;
        public override Type GetDeclaredType() => _propertyInfo.PropertyType;
        public override object? GetValue() => _propertyInfo.GetValue(_instance);
        public override string GetImageKey() => "Property";
    }
}

