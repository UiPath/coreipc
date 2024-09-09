using System.Collections;

namespace UiPath.Ipc.TV;

partial class ValueSource
{
    public sealed class Index : ValueSource
    {
        private readonly ICollection _collection;
        private readonly int _index;
        private readonly Type _elementType;

        public Index(ICollection collection, int index, Type elementType)
        {
            _collection = collection;
            _index = index;
            _elementType = elementType;
        }

        public override string GetName() => $"[{_index}]";
        public override Type GetDeclaredType() => _elementType;
        public override object? GetValue() => _collection.Cast<object>().ElementAt(_index);
        public override string GetImageKey() => "Variable";
    }
}

